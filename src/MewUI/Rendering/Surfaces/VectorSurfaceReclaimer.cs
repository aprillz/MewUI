namespace Aprillz.MewUI.Rendering;

/// <summary>
/// A size-keyed pool of offscreen vector-cache surfaces that detached controls parked, so a
/// virtualized re-realize can reuse a same-size surface instead of rebuilding the offscreen surface
/// every time a tile scrolls in and out. Any control can reuse any parked surface of the matching
/// pixel size (the content is repainted on reuse). Surfaces are owned strongly here: a parked
/// surface is always reachable for deterministic disposal (render surfaces carry no finalizer).
/// Retention is bounded by a memory budget (oldest evicted first) rather than a timer, so a surface
/// is never disposed while it could still be reused unless memory pressure forces it; whatever
/// remains is freed on window teardown. One instance per window; UI-thread only.
/// </summary>
internal sealed class VectorSurfaceReclaimer : IDisposable
{
    // Memory ceiling for retained surfaces. Past it, the oldest parked surface is evicted (it is
    // rebuilt on demand), bounding peak cost under heavy scrolling. Counts color pixels only; real
    // device memory is somewhat higher (depth/stencil attachment).
    private const long MaxBytes = 64L * 1024 * 1024;

    // Per size, parked surfaces in ascending park order (oldest at index 0). Rent takes the newest
    // (warm) from the end; budget eviction takes the globally oldest from a bucket front.
    private readonly Dictionary<(int Width, int Height), List<ParkedSurface>> _buckets = new();
    private long _currentBytes;
    private long _parkSequence;
    private bool _disposed;

    /// <summary>Hands a detached control's cache surface over for retention. Disposes it immediately if the reclaimer is already disposed.</summary>
    public void Park(IRenderSurface surface, int pixelWidth, int pixelHeight)
    {
        ArgumentNullException.ThrowIfNull(surface);

        if (_disposed)
        {
            surface.Dispose();
            return;
        }

        var key = (pixelWidth, pixelHeight);
        if (!_buckets.TryGetValue(key, out var bucket))
        {
            bucket = new List<ParkedSurface>();
            _buckets[key] = bucket;
        }

        long bytes = (long)Math.Max(1, pixelWidth) * Math.Max(1, pixelHeight) * 4;
        bucket.Add(new ParkedSurface(surface, _parkSequence++, bytes));
        _currentBytes += bytes;

        EvictToBudget();
    }

    /// <summary>Returns a parked surface of the exact pixel size (handing ownership back), or null if none is retained.</summary>
    public IRenderSurface? Rent(int pixelWidth, int pixelHeight)
    {
        if (_disposed || !_buckets.TryGetValue((pixelWidth, pixelHeight), out var bucket) || bucket.Count == 0)
        {
            return null;
        }

        int last = bucket.Count - 1;
        var entry = bucket[last];
        bucket.RemoveAt(last);
        _currentBytes -= entry.Bytes;
        if (bucket.Count == 0)
        {
            _buckets.Remove((pixelWidth, pixelHeight));
        }

        return entry.Surface;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;

        foreach (var bucket in _buckets.Values)
        {
            foreach (var entry in bucket)
            {
                entry.Surface.Dispose();
            }
        }
        _buckets.Clear();
        _currentBytes = 0;
    }

    // Evicts the globally oldest parked surface (the front of some bucket) until the retained total is
    // within budget. Keeps at least one surface even if a single one exceeds the budget on its own.
    private void EvictToBudget()
    {
        while (_currentBytes > MaxBytes && TotalCountAtLeastTwo())
        {
            (int Width, int Height) oldestKey = default;
            long oldestSequence = long.MaxValue;
            bool found = false;
            foreach (var pair in _buckets)
            {
                if (pair.Value.Count > 0 && pair.Value[0].Sequence < oldestSequence)
                {
                    oldestSequence = pair.Value[0].Sequence;
                    oldestKey = pair.Key;
                    found = true;
                }
            }

            if (!found)
            {
                break;
            }

            var bucket = _buckets[oldestKey];
            _currentBytes -= bucket[0].Bytes;
            bucket[0].Surface.Dispose();
            bucket.RemoveAt(0);
            if (bucket.Count == 0)
            {
                _buckets.Remove(oldestKey);
            }
        }
    }

    private bool TotalCountAtLeastTwo()
    {
        int count = 0;
        foreach (var bucket in _buckets.Values)
        {
            count += bucket.Count;
            if (count >= 2)
            {
                return true;
            }
        }
        return false;
    }

    private readonly struct ParkedSurface
    {
        public ParkedSurface(IRenderSurface surface, long sequence, long bytes)
        {
            Surface = surface;
            Sequence = sequence;
            Bytes = bytes;
        }

        public IRenderSurface Surface { get; }
        public long Sequence { get; }
        public long Bytes { get; }
    }
}
