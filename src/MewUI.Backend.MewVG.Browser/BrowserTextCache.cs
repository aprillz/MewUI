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
    private readonly BoundedCache<Key, int> _images;
    private bool _disposed;

    internal BrowserTextCache(NanoVG vg)
    {
        _vg = vg;
        _images = new BoundedCache<Key, int>(MAX_ENTRIES, imageId => _vg.DeleteImage(imageId));
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

        // The managed text engine hands back the same layout for a realized run, so its identity plus
        // the baked-in colour names the image. Keying on the run text instead would force it to be
        // materialised on every lookup.
        var key = new Key(layout, color.ToArgb(), widthPx, heightPx);
        if (_images.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var imageId = Rasterize(text.ToString(), cssFont, widthPx, heightPx, scale, color);
        if (imageId == 0)
        {
            return 0;
        }

        _images.Add(key, imageId);
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

    private readonly record struct Key(BackendTextLayout Layout, uint Color, int WidthPx, int HeightPx);
}
