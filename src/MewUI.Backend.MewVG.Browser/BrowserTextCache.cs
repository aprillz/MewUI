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
        Color color,
        TextInkInsetPx inset)
    {
        if (_disposed || widthPx <= 0 || heightPx <= 0)
        {
            return 0;
        }

        // What the run says, not which object said it. A layout is built fresh for every draw, so
        // keying on its identity never matched and the cache rasterized the same text again each
        // frame. The desktop caches key on the text and its font for the same reason.
        var key = new Key(string.GetHashCode(text), cssFont, color.ToArgb(), widthPx, heightPx, inset.Left, inset.Top);
        if (_images.TryGetValue(key, out var cached) && text.SequenceEqual(cached.Text))
        {
            return cached.ImageId;
        }

        // The key carries a hash, so an entry that disagrees on the text is a collision and the
        // image it holds belongs to different text; it is replaced rather than returned.
        var content = text.ToString();
        var imageId = Rasterize(content, cssFont, widthPx, heightPx, scale, color, inset);
        if (imageId == 0)
        {
            return 0;
        }

        _images.Add(key, new Entry(imageId, content));
        return imageId;
    }

    private int Rasterize(string text, string cssFont, int widthPx, int heightPx, double scale, Color color, TextInkInsetPx inset)
    {
        // Storage only: the run is drawn straight into the texture on the JS side, so the pixels
        // never visit a managed buffer. Canvas2D content stays straight alpha across that upload,
        // so the flag stays off and the shader premultiplies, as it did for the readback path.
        var imageId = _vg.CreateImageRGBA(widthPx, heightPx, NVGimageFlags.None, ReadOnlySpan<byte>.Empty);
        if (imageId == 0)
        {
            return 0;
        }

        int texture = _vg.ImageHandle(imageId);
        int drawn = texture == 0
            ? 0
            : BrowserNative.DrawTextToTexture(
                text, cssFont, widthPx, heightPx, scale,
                color.R, color.G, color.B, color.A,
                inset.Left, inset.Top, 0,
                texture);
        if (drawn <= 0)
        {
            _vg.DeleteImage(imageId);
            return 0;
        }

        return imageId;
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

    private readonly record struct Key(int TextHash, string CssFont, uint Color, int WidthPx, int HeightPx, int InsetLeftPx, int InsetTopPx);

    // The text is kept so a hash collision can be told from a hit.
    private readonly record struct Entry(int ImageId, string Text);
}
