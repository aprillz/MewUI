using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.Direct2D;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Direct2D;

/// <summary>
/// IImage that retains an external <c>IDXGISurface*</c> and materializes an
/// <c>ID2D1Bitmap*</c> against the consuming render target on demand. This avoids
/// cross-domain bitmap reuse when the same frame is drawn into different Direct2D target
/// types, such as a device-context-backed offscreen target and a legacy HWND render
/// target.
/// </summary>
internal sealed unsafe class Direct2DDxgiSurfaceImage : IImage
{
    private nint _dxgiSurface;
    private nint _bitmap;
    private nint _bitmapRenderTarget;
    private int _bitmapRenderTargetGeneration = -1;
    private bool _disposed;
    private readonly BitmapAlphaMode _alphaMode;

    public int PixelWidth { get; }
    public int PixelHeight { get; }

    public Direct2DDxgiSurfaceImage(nint dxgiSurface, int pixelWidth, int pixelHeight, BitmapAlphaMode alphaMode)
    {
        if (dxgiSurface == 0) throw new ArgumentException("DXGI surface pointer is 0.", nameof(dxgiSurface));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelHeight, 0);

        ComHelpers.AddRef(dxgiSurface);
        _dxgiSurface = dxgiSurface;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        _alphaMode = alphaMode;
    }

    public nint GetOrCreateBitmap(nint renderTarget, int renderTargetGeneration)
    {
        if (_disposed || _dxgiSurface == 0 || renderTarget == 0)
        {
            return 0;
        }

        if (_bitmap != 0 && _bitmapRenderTarget == renderTarget && _bitmapRenderTargetGeneration == renderTargetGeneration)
        {
            return _bitmap;
        }

        ReleaseBitmap();

        var props = new D2D1_BITMAP_PROPERTIES(
            new D2D1_PIXEL_FORMAT(D2D1.DXGI_FORMAT_B8G8R8A8_UNORM, ToD2DAlphaMode(_alphaMode)),
            dpiX: 96,
            dpiY: 96);

        int hr = D2D1VTable.CreateSharedBitmap(
            (ID2D1RenderTarget*)renderTarget,
            D2D1.IID_IDXGISurface,
            _dxgiSurface,
            props,
            out _bitmap);
        if (hr < 0 || _bitmap == 0)
        {
            _bitmap = 0;
            _bitmapRenderTarget = 0;
            _bitmapRenderTargetGeneration = -1;
            return 0;
        }

        _bitmapRenderTarget = renderTarget;
        _bitmapRenderTargetGeneration = renderTargetGeneration;
        return _bitmap;
    }

    ~Direct2DDxgiSurfaceImage() => ReleaseNativeHandles();

    public void Dispose()
    {
        ReleaseNativeHandles();
        GC.SuppressFinalize(this);
    }

    private void ReleaseNativeHandles()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseBitmap();
        if (_dxgiSurface != 0)
        {
            ComHelpers.Release(_dxgiSurface);
            _dxgiSurface = 0;
        }
    }

    private void ReleaseBitmap()
    {
        if (_bitmap != 0)
        {
            ComHelpers.Release(_bitmap);
            _bitmap = 0;
        }

        _bitmapRenderTarget = 0;
        _bitmapRenderTargetGeneration = -1;
    }

    private static D2D1_ALPHA_MODE ToD2DAlphaMode(BitmapAlphaMode alphaMode)
        => alphaMode switch
        {
            BitmapAlphaMode.Ignore => D2D1_ALPHA_MODE.IGNORE,
            BitmapAlphaMode.Straight => D2D1_ALPHA_MODE.STRAIGHT,
            _ => D2D1_ALPHA_MODE.PREMULTIPLIED,
        };
}