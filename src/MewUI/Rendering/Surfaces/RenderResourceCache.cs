namespace Aprillz.MewUI.Rendering;

public sealed class RenderResourceCache : IRenderResourceCache, IDisposable
{
    // Scratch pool ceilings. The count ceiling is sized ABOVE the working set of a maximized
    // icon grid (a QHD window shows ~2,300 tiles): evicting below the active working set makes
    // every presenter rebind destroy and recreate GPU surfaces in bulk, which costs far more
    // than the retention it saves. The byte budget only has to cover that same working set,
    // which is small because grid tiles are small; a window-sized surface pooled during a
    // resize is what the budget is there to throw away, since every resize step asks for a
    // size the pool has never seen (measured on a 600-step sweep: 129 hits against 383 misses,
    // identical step times at 320 MB and at 16 MB, 344 MB less retained).
    private const long SCRATCH_BUDGET_BYTES = 16L * 1024 * 1024;
    private const int SCRATCH_MAX_COUNT = 2560;

    // Per-surface floor for the budget accounting. A pooled surface is a GPU texture plus a
    // stencil attachment, and drivers commit at least a page-granular allocation for each, so
    // thousands of tiny tiles cost far more device memory than width x height x 4 suggests
    // (measured: 3,312 pooled 32-64px tiles = 27 MB by pixel math, ~500 MB GPU committed).
    private const long SCRATCH_MIN_ACCOUNTED_BYTES = 128L * 1024;

    // A pooled surface untouched this long is dead weight (the view that used it is gone), so a
    // periodic sweep disposes it. This is what returns memory after scrolling stops, without
    // ever evicting the surfaces an active grid is cycling through.
    private const long SCRATCH_IDLE_EVICT_MS = 1_500;
    private const long SCRATCH_SWEEP_INTERVAL_MS = 1_000;
    private long _scratchLastSweepTicks;

    private readonly object _gate = new();
    private readonly Dictionary<RenderCacheKey, RenderCacheEntry> _entries = new();
    private readonly List<PendingRelease> _pendingReleases = new();

    // Scratch buckets are lists: several surfaces can share one key. Rent pops the warm end.
    private readonly Dictionary<ScratchSurfaceKey, List<PooledScratchSurface>> _scratchBuckets = new();
    private long _scratchBytes;
    private bool _disposed;

    public bool TryGet(RenderCacheKey key, out IRenderCacheEntry entry)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            DrainCompletedReleases_NoLock();
            if (_entries.TryGetValue(key, out var cached))
            {
                entry = cached;
                return true;
            }
        }

        entry = null!;
        return false;
    }

    public IRenderCacheEntry Add(
        RenderCacheKey key,
        IRenderSurface surface,
        IImage image,
        IRenderOperation? safeToDisposeAfter = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(image);

        lock (_gate)
        {
            ThrowIfDisposed();
            DrainCompletedReleases_NoLock();

            if (_entries.Remove(key, out var existing))
            {
                ReleaseEntry_NoLock(existing);
            }

            var entry = new RenderCacheEntry(key, surface, image, safeToDisposeAfter);
            _entries.Add(key, entry);
            return entry;
        }
    }

    public void Release(RenderCacheKey key)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_entries.Remove(key, out var existing))
            {
                ReleaseEntry_NoLock(existing);
            }
        }
    }

    public void ReleaseLater(IDisposable resource, IRenderOperation safeAfter)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(safeAfter);

        lock (_gate)
        {
            if (_disposed)
            {
                resource.Dispose();
                safeAfter.Dispose();
                return;
            }

            if (safeAfter.IsCompleted)
            {
                resource.Dispose();
                safeAfter.Dispose();
            }
            else
            {
                _pendingReleases.Add(new PendingRelease(resource, safeAfter));
            }
        }
    }

    public void Trim(RenderCacheTrimReason reason)
    {
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var entry in _entries.Values)
            {
                ReleaseEntry_NoLock(entry);
            }

            _entries.Clear();
            ClearScratchPoolNoLock();
            DrainCompletedReleases_NoLock();
        }
    }

    public IRenderSurface? RentScratchSurface(ScratchSurfaceKey key)
    {
        lock (_gate)
        {
            ThrowIfDisposed();
            SweepIdleScratchNoLock();
            if (!_scratchBuckets.TryGetValue(key, out var bucket) || bucket.Count == 0)
            {
                return null;
            }

            // Most recently returned first; a surface the calling thread cannot render into stays
            // in the bucket for the thread that owns it.
            for (int i = bucket.Count - 1; i >= 0; i--)
            {
                var pooled = bucket[i];
                if (pooled.Surface is IReusableScratchSurface reusable && !reusable.CanRenderFromCurrentThread)
                {
                    continue;
                }

                bucket.RemoveAt(i);
                _scratchBytes -= pooled.Bytes;
                if (bucket.Count == 0)
                {
                    _scratchBuckets.Remove(key);
                }

                return pooled.Surface;
            }

            return null;
        }
    }

    public void ReturnScratchSurface(ScratchSurfaceKey key, IRenderSurface surface)
    {
        ArgumentNullException.ThrowIfNull(surface);

        lock (_gate)
        {
            if (_disposed)
            {
                surface.Dispose();
                return;
            }

            if (!_scratchBuckets.TryGetValue(key, out var bucket))
            {
                bucket = new List<PooledScratchSurface>();
                _scratchBuckets[key] = bucket;
            }

            long bytes = Math.Max(SCRATCH_MIN_ACCOUNTED_BYTES, (long)Math.Max(1, key.PixelWidth) * Math.Max(1, key.PixelHeight) * 4);
            bucket.Add(new PooledScratchSurface(surface, bytes, Environment.TickCount64));
            _scratchBytes += bytes;

            SweepIdleScratchNoLock();
            EvictScratchToBudgetNoLock();
        }
    }

    /// <summary>Disposes pooled surfaces that have sat unused past the idle window, at most once per sweep interval.</summary>
    private void SweepIdleScratchNoLock()
    {
        long now = Environment.TickCount64;
        if (now - _scratchLastSweepTicks < SCRATCH_SWEEP_INTERVAL_MS)
        {
            return;
        }
        _scratchLastSweepTicks = now;

        List<ScratchSurfaceKey>? emptied = null;
        foreach (var pair in _scratchBuckets)
        {
            var bucket = pair.Value;
            for (int i = bucket.Count - 1; i >= 0; i--)
            {
                if (now - bucket[i].LastUse < SCRATCH_IDLE_EVICT_MS)
                {
                    continue;
                }
                _scratchBytes -= bucket[i].Bytes;
                bucket[i].Surface.Dispose();
                bucket.RemoveAt(i);
            }
            if (bucket.Count == 0)
            {
                (emptied ??= new List<ScratchSurfaceKey>()).Add(pair.Key);
            }
        }

        if (emptied != null)
        {
            foreach (var key in emptied)
            {
                _scratchBuckets.Remove(key);
            }
        }
    }

    /// <summary>Evicts least-recently-used pooled surfaces until within budget, keeping at least one.</summary>
    private void EvictScratchToBudgetNoLock()
    {
        while ((_scratchBytes > SCRATCH_BUDGET_BYTES || CountScratchNoLock() > SCRATCH_MAX_COUNT) && CountScratchNoLock() > 1)
        {
            ScratchSurfaceKey oldestKey = default;
            int oldestIndex = -1;
            long oldestTimestamp = long.MaxValue;
            foreach (var pair in _scratchBuckets)
            {
                var bucket = pair.Value;
                for (int i = 0; i < bucket.Count; i++)
                {
                    if (bucket[i].LastUse < oldestTimestamp)
                    {
                        oldestTimestamp = bucket[i].LastUse;
                        oldestKey = pair.Key;
                        oldestIndex = i;
                    }
                }
            }

            if (oldestIndex < 0)
            {
                return;
            }

            var victimBucket = _scratchBuckets[oldestKey];
            var victim = victimBucket[oldestIndex];
            victimBucket.RemoveAt(oldestIndex);
            _scratchBytes -= victim.Bytes;
            if (victimBucket.Count == 0)
            {
                _scratchBuckets.Remove(oldestKey);
            }

            victim.Surface.Dispose();
        }
    }

    private int CountScratchNoLock()
    {
        int count = 0;
        foreach (var bucket in _scratchBuckets.Values)
        {
            count += bucket.Count;
        }
        return count;
    }

    private void ClearScratchPoolNoLock()
    {
        foreach (var bucket in _scratchBuckets.Values)
        {
            foreach (var pooled in bucket)
            {
                pooled.Surface.Dispose();
            }
        }
        _scratchBuckets.Clear();
        _scratchBytes = 0;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;

            foreach (var entry in _entries.Values)
            {
                entry.Dispose();
            }

            _entries.Clear();
            ClearScratchPoolNoLock();

            foreach (var pending in _pendingReleases)
            {
                pending.Resource.Dispose();
                pending.Operation.Dispose();
            }

            _pendingReleases.Clear();
        }
    }

    private void ReleaseEntry_NoLock(RenderCacheEntry entry)
    {
        if (entry.SafeToDisposeAfter is { } operation && !operation.IsCompleted)
        {
            _pendingReleases.Add(new PendingRelease(entry, operation));
            return;
        }

        entry.Dispose();
    }

    private void DrainCompletedReleases_NoLock()
    {
        for (int i = _pendingReleases.Count - 1; i >= 0; i--)
        {
            var pending = _pendingReleases[i];
            if (!pending.Operation.IsCompleted)
            {
                continue;
            }

            _pendingReleases.RemoveAt(i);
            pending.Resource.Dispose();
            pending.Operation.Dispose();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private sealed class RenderCacheEntry : IRenderCacheEntry
    {
        private bool _disposed;

        public RenderCacheEntry(RenderCacheKey key, IRenderSurface surface, IImage image, IRenderOperation? safeToDisposeAfter)
        {
            Key = key;
            Surface = surface;
            Image = image;
            SafeToDisposeAfter = safeToDisposeAfter;
        }

        public RenderCacheKey Key { get; }

        public IRenderSurface Surface { get; }

        public IImage Image { get; }

        public IRenderOperation? SafeToDisposeAfter { get; }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Image.Dispose();
            Surface.Dispose();
            SafeToDisposeAfter?.Dispose();
        }
    }

    private readonly record struct PendingRelease(IDisposable Resource, IRenderOperation Operation);

    private readonly record struct PooledScratchSurface(IRenderSurface Surface, long Bytes, long LastUse);
}
