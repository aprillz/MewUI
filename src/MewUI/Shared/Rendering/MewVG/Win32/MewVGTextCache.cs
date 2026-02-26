using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

internal readonly record struct MewVGTextCacheKey(TextCacheKey Core);

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

    public bool TryGet(MewVGTextCacheKey key, out MewVGTextEntry entry)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MewVGTextCache));
        }

        if (_map.TryGetValue(key, out var node))
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
            entry = node.Value.Entry;
            return true;
        }

        entry = default;
        return false;
    }

    public MewVGTextEntry CreateImage(MewVGTextCacheKey key, ref TextBitmap bmp)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MewVGTextCache));
        }

        if (bmp.WidthPx <= 0 || bmp.HeightPx <= 0)
        {
            return default;
        }

        byte[] rgba = ImagePixelUtils.ConvertBgraToRgba(bmp.Data);
        int imageId = _vg.CreateImageRGBA(bmp.WidthPx, bmp.HeightPx, NVGimageFlags.Nearest, rgba);
        if (imageId == 0)
        {
            return default;
        }

        var entry = new MewVGTextEntry(imageId, bmp.WidthPx, bmp.HeightPx, 0, 0, bmp.WidthPx, bmp.HeightPx);
        long bytes = EstimateBytes(bmp.WidthPx, bmp.HeightPx);
        var newNode = new LinkedListNode<CacheEntry>(new CacheEntry(key, entry, bytes));
        _lru.AddFirst(newNode);
        _map[key] = newNode;
        _currentBytes += bytes;

        EvictIfNeeded();
        return entry;
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
            _map.Remove(last.Value.Key);

            int imageId = last.Value.Entry.ImageId;
            if (imageId != 0)
            {
                _vg.DeleteImage(imageId);
            }

            _currentBytes -= last.Value.Bytes;
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
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Clear();
    }

    private readonly record struct CacheEntry(MewVGTextCacheKey Key, MewVGTextEntry Entry, long Bytes);
}
