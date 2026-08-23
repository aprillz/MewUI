using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

// Linear distinguishes rotated draws (which need smooth interpolation) from axis-aligned ones (crisp nearest),
// so the two cache as separate atlas images.
internal readonly record struct MewVGTextCacheKey(TextCacheKey Core, bool Linear = false);

internal readonly record struct MewVGTextEntry(
    int ImageId,
    int AtlasWidthPx,
    int AtlasHeightPx,
    int X,
    int Y,
    int WidthPx,
    int HeightPx);

internal sealed class MewVGTextCache : IDisposable
{
    private const long DefaultMaxBytes = 16L * 1024 * 1024;

    private readonly NanoVG _vg;
    private readonly Dictionary<MewVGTextCacheKey, LinkedListNode<CacheEntry>> _map = new();
    private readonly LinkedList<CacheEntry> _lru = new();
    private readonly Queue<int> _pendingDeletes = new();
    // Scratch textures for transient text, one per transient draw in a frame. A slot is only
    // repainted in place the frame after its last use, once ReleasePendingDeletes has reset the
    // index past the flush that consumed it.
    private readonly List<TransientSlot> _transientSlots = new();
    private int _transientIndex;
    private byte[]? _transientBuffer;
    private long _currentBytes;
    private bool _disposed;

    public long MaxBytes
    {
        get;
        set => field = Math.Max(0, value);
    } = DefaultMaxBytes;

    public MewVGTextCache(NanoVG vg)
    {
        _vg = vg;
    }

    public bool TryGet(MewVGTextCacheKey key, ReadOnlySpan<char> text, out MewVGTextEntry entry)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MewVGTextCache));
        }

        // Key.Core.TextHash only narrows the dictionary bucket; two different strings can share
        // a hash, so the actual text is checked here and a mismatch is treated as a miss.
        if (_map.TryGetValue(key, out var node) && text.SequenceEqual(node.Value.Text))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            entry = node.Value.Entry;
            return true;
        }

        entry = default;
        return false;
    }

    public MewVGTextEntry CreateImage(MewVGTextCacheKey key, ReadOnlySpan<char> text, ref TextBitmap bmp)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MewVGTextCache));
        }

        if (bmp.WidthPx <= 0 || bmp.HeightPx <= 0)
        {
            return default;
        }

        // Source bitmap is BGRA - feed straight to NVG; GL backend uses GL_BGRA upload, no
        // CPU swap. Backends without native BGRA fall back to a one-time conversion in
        // NanoVG.CreateImageBGRA's default implementation.
        // Nearest keeps pixel-snapped axis-aligned text crisp; rotated text asks for linear so it interpolates
        // smoothly instead of breaking up into jaggies.
        var flags = key.Linear ? NVGimageFlags.None : NVGimageFlags.Nearest;
        int imageId = _vg.CreateImageBGRA(bmp.WidthPx, bmp.HeightPx, flags, bmp.Data);
        if (imageId == 0)
        {
            return default;
        }

        var entry = new MewVGTextEntry(imageId, bmp.WidthPx, bmp.HeightPx, 0, 0, bmp.WidthPx, bmp.HeightPx);
        long bytes = EstimateBytes(bmp.WidthPx, bmp.HeightPx);
        if (_map.TryGetValue(key, out var replaced))
        {
            _lru.Remove(replaced);
            _currentBytes -= replaced.Value.Bytes;
            if (replaced.Value.Entry.ImageId != 0)
            {
                _pendingDeletes.Enqueue(replaced.Value.Entry.ImageId);
            }
        }

        var newNode = new LinkedListNode<CacheEntry>(new CacheEntry(key, text.ToString(), entry, bytes));
        _lru.AddFirst(newNode);
        _map[key] = newNode;
        _currentBytes += bytes;

        EvictIfNeeded();
        return entry;
    }

    /// <summary>Returns a reusable raster buffer of at least <paramref name="bytes"/> bytes for a transient draw.</summary>
    public byte[] RentTransientBuffer(int bytes)
    {
        if (_transientBuffer == null || _transientBuffer.Length < bytes)
        {
            _transientBuffer = new byte[Math.Max(bytes, (_transientBuffer?.Length ?? 0) * 2)];
        }

        return _transientBuffer;
    }

    /// <summary>
    /// Uploads <paramref name="pixels"/> into the next transient scratch texture of this frame and
    /// returns it; the texture is neither keyed nor retained beyond reuse by a later frame.
    /// </summary>
    public MewVGTextEntry UseTransient(ReadOnlySpan<byte> pixels, int widthPx, int heightPx, bool linear)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MewVGTextCache));
        }

        if (widthPx <= 0 || heightPx <= 0)
        {
            return default;
        }

        if (_transientIndex >= _transientSlots.Count)
        {
            _transientSlots.Add(new TransientSlot());
        }

        var slot = _transientSlots[_transientIndex++];
        if (slot.ImageId != 0 && slot.WidthPx == widthPx && slot.HeightPx == heightPx && slot.Linear == linear)
        {
            _vg.UpdateImageBGRA(slot.ImageId, pixels);
        }
        else
        {
            ReleaseSlot(slot);
            var flags = linear ? NVGimageFlags.None : NVGimageFlags.Nearest;
            slot.ImageId = _vg.CreateImageBGRA(widthPx, heightPx, flags, pixels);
            if (slot.ImageId == 0)
            {
                return default;
            }

            slot.WidthPx = widthPx;
            slot.HeightPx = heightPx;
            slot.Linear = linear;
            slot.Bytes = EstimateBytes(widthPx, heightPx);
        }

        return new MewVGTextEntry(slot.ImageId, widthPx, heightPx, 0, 0, widthPx, heightPx);
    }

    private void ReleaseSlot(TransientSlot slot)
    {
        if (slot.ImageId != 0)
        {
            _pendingDeletes.Enqueue(slot.ImageId);
            slot.ImageId = 0;
            slot.Bytes = 0;
        }
    }

    private static long EstimateBytes(int widthPx, int heightPx)
    {
        if (widthPx <= 0 || heightPx <= 0)
        {
            return 0;
        }

        return (long)widthPx * heightPx * 4;
    }

    private void EvictIfNeeded()
    {
        if (MaxBytes <= 0)
        {
            Clear();
            return;
        }

        while (_currentBytes > MaxBytes && _lru.Last is { } last)
        {
            _lru.RemoveLast();
            if (_map.TryGetValue(last.Value.Key, out var mapped) && ReferenceEquals(mapped, last))
            {
                _map.Remove(last.Value.Key);
            }

            int imageId = last.Value.Entry.ImageId;
            if (imageId != 0)
            {
                // Defer the actual glDeleteTextures: eviction happens during
                // mid-frame text rendering (CreateImage), but the main NVG has
                // already buffered draw calls referencing this imageId. Deleting
                // the GL texture before NVG.Flush leaves those draws sampling a
                // freed texture name - they render as opaque black, with a
                // different set of text boxes affected each frame as the LRU
                // boundary moves. The graphics context drains this queue right
                // after Flush, when no draw call still references the IDs.
                _pendingDeletes.Enqueue(imageId);
            }

            _currentBytes -= last.Value.Bytes;
        }
    }

    /// <summary>
    /// Releases NVG image handles whose deletion was deferred by
    /// <see cref="EvictIfNeeded"/>. Must be called after the main NVG's
    /// EndFrame/Flush so no pending draw call still references the IDs.
    /// </summary>
    public void ReleasePendingDeletes()
    {
        if (_disposed)
        {
            return;
        }

        _transientIndex = 0;
        while (_pendingDeletes.Count > 0)
        {
            int imageId = _pendingDeletes.Dequeue();
            if (imageId != 0)
            {
                _vg.DeleteImage(imageId);
            }
        }
    }

    public void Clear()
    {
        if (_disposed)
        {
            return;
        }

        var node = _lru.First;
        while (node != null)
        {
            int imageId = node.Value.Entry.ImageId;
            if (imageId != 0)
            {
                _vg.DeleteImage(imageId);
            }

            node = node.Next;
        }

        _lru.Clear();
        _map.Clear();
        _currentBytes = 0;

        foreach (var slot in _transientSlots)
        {
            ReleaseSlot(slot);
        }

        _transientSlots.Clear();
        _transientIndex = 0;
        _transientBuffer = null;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        // Clear() and ReleasePendingDeletes() both early-return once _disposed is set,
        // so both must run before the flag flips or their releases would be dropped.
        Clear();
        ReleasePendingDeletes();
        _disposed = true;
    }

    private readonly record struct CacheEntry(MewVGTextCacheKey Key, string Text, MewVGTextEntry Entry, long Bytes);

    private sealed class TransientSlot
    {
        public int ImageId;
        public int WidthPx;
        public int HeightPx;
        public bool Linear;
        public long Bytes;
    }
}
