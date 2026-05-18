using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;

using SkiaSharp;

namespace Aprillz.MewUI.Skia.Interop.Gdi;

/// <summary>
/// Zero-copy GDI host. Allocates a top-down 32-bpp BGRA DIB section, wraps its bits as an
/// <see cref="SKSurface"/> for Skia to paint into, and exposes the same DIB as a GDI
/// <see cref="IImage"/> via <see cref="GdiGraphicsFactory.CreateImageOverDibSection"/>.
/// The DIB memory is shared — no per-frame copy.
/// </summary>
internal sealed class GdiSkiaSurfaceHost : ISkiaSurfaceHost, IOpaqueAwareSurfaceHost
{
    private readonly GdiGraphicsFactory _factory;

    private nint _dibHandle;
    private nint _dibBits;
    private SKSurface? _skSurface;
    private IImage? _image;

    private int _pixelWidth;
    private int _pixelHeight;
    private bool _disposed;

    public GdiSkiaSurfaceHost(GdiGraphicsFactory factory)
    {
        _factory = factory;
    }

    public int PixelWidth => _pixelWidth;
    public int PixelHeight => _pixelHeight;
    public bool SurfaceInvalidated => false;
    public string Description => "Software zero-copy (Skia CPU → DIB → GDI BitBlt)";

    public bool IsOpaque
    {
        get;
        set
        {
            field = value;
            if (_image is not null) _factory.SetImageOpaque(_image, value);
        }
    }

    public bool EnsureSurface(int pixelWidth, int pixelHeight)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (pixelWidth <= 0 || pixelHeight <= 0) return false;
        if (_skSurface != null && pixelWidth == _pixelWidth && pixelHeight == _pixelHeight) return true;

        ReleaseSurfaceResources();
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;

        var bmi = GdiDibInterop.Create32bppTopDown(pixelWidth, pixelHeight);
        nint screenDc = GdiDibInterop.GetDC(0);
        try
        {
            _dibHandle = GdiDibInterop.CreateDIBSection(screenDc, ref bmi, GdiDibInterop.DIB_RGB_COLORS, out _dibBits, 0, 0);
        }
        finally
        {
            GdiDibInterop.ReleaseDC(0, screenDc);
        }

        if (_dibHandle == 0 || _dibBits == 0)
        {
            ReleaseSurfaceResources();
            return false;
        }

        var info = new SKImageInfo(pixelWidth, pixelHeight, SKColorType.Bgra8888, SKAlphaType.Premul);
        _skSurface = SKSurface.Create(info, _dibBits, info.RowBytes);
        if (_skSurface is null)
        {
            ReleaseSurfaceResources();
            return false;
        }

        _image = _factory.CreateImageOverDibSection(pixelWidth, pixelHeight, _dibHandle, _dibBits);
        if (_image is not null) _factory.SetImageOpaque(_image, IsOpaque);
        return _image is not null;
    }

    public IImage? Paint(Action<SKSurface> painter)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(painter);

        if (_skSurface is null || _image is null) return null;

        painter(_skSurface);
        _skSurface.Flush();

        // Invalidate GdiImage's cached scaled/transformed bitmaps so the next render
        // resamples from the freshly painted DIB.
        _factory.MarkExternalImageBitsChanged(_image);
        return _image;
    }

    private void ReleaseSurfaceResources()
    {
        _image?.Dispose();
        _image = null;
        _skSurface?.Dispose();
        _skSurface = null;
        if (_dibHandle != 0)
        {
            GdiDibInterop.DeleteObject(_dibHandle);
            _dibHandle = 0;
            _dibBits = 0;
        }
        _pixelWidth = 0;
        _pixelHeight = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseSurfaceResources();
    }
}
