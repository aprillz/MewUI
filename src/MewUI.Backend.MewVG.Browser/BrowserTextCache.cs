using System.Buffers;
using System.Runtime.InteropServices;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// Keeps rasterized text runs as MewVG images. Rasterizing goes through the browser's Canvas2D,
/// which costs a JS call and a pixel readback, so every distinct run is drawn at most once.
/// </summary>
internal sealed class BrowserTextCache : IDisposable
{
    private const int MAX_ENTRIES = 512;

    private readonly Dictionary<Key, Entry> _entries = new();
    private readonly NanoVG _vg;
    private long _clock;
    private bool _disposed;

    internal BrowserTextCache(NanoVG vg) => _vg = vg;


    internal int GetOrCreateImage(
        ReadOnlySpan<char> text,
        string cssFont,
        int widthPx,
        int heightPx,
        double scale,
        Color color,
        TextAlignment horizontalAlignment,
        TextAlignment verticalAlignment,
        TextWrapping wrapping)
    {
        if (_disposed || widthPx <= 0 || heightPx <= 0)
        {
            return 0;
        }

        var content = text.ToString();
        var key = new Key(
            content,
            cssFont,
            widthPx,
            heightPx,
            color.ToArgb(),
            (int)horizontalAlignment,
            (int)verticalAlignment,
            wrapping == TextWrapping.NoWrap ? 0 : 1);

        if (_entries.TryGetValue(key, out var cached))
        {
            cached.LastUsed = ++_clock;
            return cached.ImageId;
        }

        var imageId = Rasterize(content, cssFont, widthPx, heightPx, scale, color, key.HorizontalAlignment,
            key.VerticalAlignment, key.Wrap);
        if (imageId == 0)
        {
            return 0;
        }

        if (_entries.Count >= MAX_ENTRIES)
        {
            EvictOldest();
        }

        _entries[key] = new Entry(imageId) { LastUsed = ++_clock };
        return imageId;
    }

    private int Rasterize(string text, string cssFont, int widthPx, int heightPx, double scale, Color color,
        int horizontalAlignment, int verticalAlignment, int wrap)
    {
        var byteCount = widthPx * heightPx * 4;
        var buffer = ArrayPool<byte>.Shared.Rent(byteCount);
        try
        {
            var handle = GCHandle.Alloc(buffer, GCHandleType.Pinned);
            try
            {
                var lines = BrowserNative.RasterizeText(
                    text, cssFont, widthPx, heightPx, scale,
                    color.R, color.G, color.B, color.A,
                    horizontalAlignment, verticalAlignment, wrap,
                    handle.AddrOfPinnedObject());
                if (lines <= 0)
                {
                    return 0;
                }
            }
            finally
            {
                handle.Free();
            }

            // Canvas2D hands back straight alpha, so the flag stays off and the shader premultiplies.
            return _vg.CreateImageRGBA(widthPx, heightPx, NVGimageFlags.None, buffer.AsSpan(0, byteCount));
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private void EvictOldest()
    {
        Key? oldestKey = null;
        var oldestUse = long.MaxValue;
        foreach (var pair in _entries)
        {
            if (pair.Value.LastUsed < oldestUse)
            {
                oldestUse = pair.Value.LastUsed;
                oldestKey = pair.Key;
            }
        }

        if (oldestKey is Key key && _entries.Remove(key, out var evicted))
        {
            _vg.DeleteImage(evicted.ImageId);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var entry in _entries.Values)
        {
            _vg.DeleteImage(entry.ImageId);
        }

        _entries.Clear();
    }

    private readonly record struct Key(
        string Text,
        string CssFont,
        int WidthPx,
        int HeightPx,
        uint Color,
        int HorizontalAlignment,
        int VerticalAlignment,
        int Wrap);

    private sealed class Entry(int imageId)
    {
        internal int ImageId { get; } = imageId;
        internal long LastUsed { get; set; }
    }
}
