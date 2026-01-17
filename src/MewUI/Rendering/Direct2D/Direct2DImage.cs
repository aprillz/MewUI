using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.Direct2D;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Direct2D;

internal sealed class Direct2DImage : IImage
{
    private const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;

    private readonly IPixelBufferSource _pixels;
    private int _pixelsVersion = -1;
    private byte[]? _premultiplied;
    private nint _renderTarget;
    private int _renderTargetGeneration;
    private nint _bitmap;
    private bool _disposed;

    public int PixelWidth { get; }
    public int PixelHeight { get; }

    public Direct2DImage(DecodedBitmap bmp)
    {
        if (bmp.PixelFormat != BitmapPixelFormat.Bgra32)
        {
            throw new NotSupportedException($"Unsupported pixel format: {bmp.PixelFormat}");
        }

        PixelWidth = bmp.WidthPx;
        PixelHeight = bmp.HeightPx;
        _pixels = new StaticPixelBufferSource(bmp.WidthPx, bmp.HeightPx, bmp.Data);
        _pixelsVersion = _pixels.Version;
    }

    public Direct2DImage(IPixelBufferSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.PixelFormat != BitmapPixelFormat.Bgra32)
        {
            throw new NotSupportedException($"Unsupported pixel format: {source.PixelFormat}");
        }

        PixelWidth = source.PixelWidth;
        PixelHeight = source.PixelHeight;
        _pixels = source;
        _pixelsVersion = source.Version;
    }

    public nint GetOrCreateBitmap(nint renderTarget, int renderTargetGeneration)
    {
        if (_disposed || renderTarget == 0)
        {
            return 0;
        }

        int v = _pixels.Version;
        if (_pixelsVersion != v)
        {
            _pixelsVersion = v;
            _premultiplied = null;

            if (_bitmap != 0)
            {
                ComHelpers.Release(_bitmap);
                _bitmap = 0;
                _renderTarget = 0;
                _renderTargetGeneration = 0;
            }
        }

        if (_bitmap != 0 && _renderTarget == renderTarget && _renderTargetGeneration == renderTargetGeneration)
        {
            return _bitmap;
        }

        // The bitmap is tied to a specific render target. If the render target changes (e.g. during resize),
        // the bitmap must be recreated for the new target.
        if (_bitmap != 0)
        {
            ComHelpers.Release(_bitmap);
            _bitmap = 0;
            _renderTarget = 0;
            _renderTargetGeneration = 0;
        }

        // Direct2D expects premultiplied alpha for typical UI bitmaps.
        byte[] premul;
        using (var l = _pixels.Lock())
        {
            if (l.Buffer.Length == 0)
            {
                return 0;
            }

            premul = _premultiplied ??= PremultiplyIfNeeded(l.Buffer);
        }

        var props = new D2D1_BITMAP_PROPERTIES(
            pixelFormat: new D2D1_PIXEL_FORMAT(DXGI_FORMAT_B8G8R8A8_UNORM, D2D1_ALPHA_MODE.PREMULTIPLIED),
            dpiX: 96,
            dpiY: 96);

        nint bmpHandle = 0;
        unsafe
        {
            fixed (byte* p = premul)
            {
                int hr = D2D1VTable.CreateBitmap(
                    (ID2D1RenderTarget*)renderTarget,
                    new D2D1_SIZE_U((uint)PixelWidth, (uint)PixelHeight),
                    srcData: (nint)p,
                    pitch: (uint)(PixelWidth * 4),
                    props: props,
                    bitmap: out bmpHandle);

                if (hr < 0 || bmpHandle == 0)
                {
                    throw new InvalidOperationException($"ID2D1RenderTarget::CreateBitmap failed: 0x{hr:X8}");
                }
            }
        }

        _bitmap = bmpHandle;
        _renderTarget = renderTarget;
        _renderTargetGeneration = renderTargetGeneration;
        return _bitmap;
    }

    private static byte[] PremultiplyIfNeeded(byte[] bgra)
    {
        // Fast path: if no alpha < 255, return original buffer.
        for (int i = 3; i < bgra.Length; i += 4)
        {
            if (bgra[i] != 0xFF)
            {
                return Premultiply(bgra);
            }
        }
        return bgra;
    }

    private static byte[] Premultiply(ReadOnlySpan<byte> bgra)
    {
        var dst = new byte[bgra.Length];
        for (int i = 0; i < bgra.Length; i += 4)
        {
            byte b = bgra[i + 0];
            byte g = bgra[i + 1];
            byte r = bgra[i + 2];
            byte a = bgra[i + 3];

            uint alpha = a;
            dst[i + 3] = a;
            dst[i + 0] = (byte)((b * alpha + 127) / 255);
            dst[i + 1] = (byte)((g * alpha + 127) / 255);
            dst[i + 2] = (byte)((r * alpha + 127) / 255);
        }
        return dst;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        ComHelpers.Release(_bitmap);
        _bitmap = 0;
        _renderTarget = 0;
        _premultiplied = null;
    }
}
