using System.Buffers;
using System.Runtime.InteropServices;
using Aprillz.MewUI.Text;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// Keeps rasterized text runs as MewVG images. Rasterizing goes through the browser's Canvas2D,
/// which costs a JS call and a pixel readback, so every distinct run is drawn at most once.
/// </summary>
internal sealed class BrowserTextCache : IDisposable
{
    private const int MAX_ENTRIES = 512;

    private readonly NanoVG _vg;
    private readonly BoundedCache<Key, Entry> _images;
    private bool _disposed;

    internal BrowserTextCache(NanoVG vg)
    {
        _vg = vg;
        _images = new BoundedCache<Key, Entry>(MAX_ENTRIES, entry => _vg.DeleteImage(entry.ImageId));
    }

    internal int GetOrCreateImage(
        ReadOnlySpan<char> text,
        BackendTextLayout layout,
        string cssFont,
        int widthPx,
        int heightPx,
        double scale,
        Color color)
    {
        if (_disposed || widthPx <= 0 || heightPx <= 0)
        {
            return 0;
        }

        // What the run says, not which object said it. A layout is built fresh for every draw, so
        // keying on its identity never matched and the cache rasterized the same text again each
        // frame. The desktop caches key on the text and its font for the same reason.
        var key = new Key(string.GetHashCode(text), cssFont, color.ToArgb(), widthPx, heightPx);
        if (_images.TryGetValue(key, out var cached) && text.SequenceEqual(cached.Text))
        {
            return cached.ImageId;
        }

        // The key carries a hash, so an entry that disagrees on the text is a collision and the
        // image it holds belongs to different text; it is replaced rather than returned.
        var content = text.ToString();
        var imageId = Rasterize(content, cssFont, widthPx, heightPx, scale, color);
        if (imageId == 0)
        {
            return 0;
        }

        _images.Add(key, new Entry(imageId, content));
        return imageId;
    }

    private int Rasterize(string text, string cssFont, int widthPx, int heightPx, double scale, Color color)
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
                    0, 0, 0,
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

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _images.Dispose();
    }

    private readonly record struct Key(int TextHash, string CssFont, uint Color, int WidthPx, int HeightPx);

    // The text is kept so a hash collision can be told from a hit.
    private readonly record struct Entry(int ImageId, string Text);
}
