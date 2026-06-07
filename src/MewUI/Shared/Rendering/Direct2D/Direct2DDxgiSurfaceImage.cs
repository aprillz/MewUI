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
internal sealed unsafe class Direct2DDxgiSurfaceImage : IImage, IGpuResourceAffinityProvider
{
    private nint _dxgiSurface;
    private nint _bitmap;
    private nint _bitmapRenderTarget;
    private int _bitmapRenderTargetGeneration = -1;
    private bool _disposed;
    private readonly BitmapAlphaMode _alphaMode;
    private readonly IDisposable? _lease;
    private readonly bool _preferDeviceContextBitmap;
    private readonly float _bitmapDpi;
    private bool _mismatchNotified;

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public GpuResourceAffinity? Affinity { get; }

    public Direct2DDxgiSurfaceImage(
        nint dxgiSurface,
        int pixelWidth,
        int pixelHeight,
        BitmapAlphaMode alphaMode,
        GpuResourceAffinity? affinity = null,
        IDisposable? lease = null,
        bool preferDeviceContextBitmap = true,
        double dpiScale = 1.0)
    {
        if (dxgiSurface == 0) throw new ArgumentException("DXGI surface pointer is 0.", nameof(dxgiSurface));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelHeight, 0);
        if (!(dpiScale > 0)) dpiScale = 1.0;

        ComHelpers.AddRef(dxgiSurface);
        _dxgiSurface = dxgiSurface;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        _alphaMode = alphaMode;
        Affinity = affinity;
        _lease = lease;
        _preferDeviceContextBitmap = preferDeviceContextBitmap;
        _bitmapDpi = (float)(96.0 * dpiScale);
    }

    public bool MarkMismatchNotified()
    {
        if (_mismatchNotified)
        {
            return false;
        }

        _mismatchNotified = true;
        return true;
    }

    public nint GetOrCreateBitmap(nint renderTarget, int renderTargetGeneration, nint deviceContext = 0)
    {
        if (_disposed || _dxgiSurface == 0 || renderTarget == 0)
        {
            return 0;
        }

        if (_bitmap != 0 && _bitmapRenderTarget == renderTarget && _bitmapRenderTargetGeneration == renderTargetGeneration)
        {
            return _bitmap;
        }

        int hr;
        nint newBitmap;
        if (_preferDeviceContextBitmap && deviceContext != 0)
        {
            var props = new D2D1_BITMAP_PROPERTIES1(
                new D2D1_PIXEL_FORMAT(D2D1.DXGI_FORMAT_B8G8R8A8_UNORM, ToD2DAlphaMode(_alphaMode)),
                dpiX: _bitmapDpi,
                dpiY: _bitmapDpi,
                D2D1_BITMAP_OPTIONS.NONE,
                colorContext: 0);

            hr = D2D1VTable.CreateBitmapFromDxgiSurface(
                (ID2D1DeviceContext*)deviceContext,
                _dxgiSurface,
                props,
                out newBitmap);
        }
        else
        {
            var props = new D2D1_BITMAP_PROPERTIES(
                new D2D1_PIXEL_FORMAT(D2D1.DXGI_FORMAT_B8G8R8A8_UNORM, ToD2DAlphaMode(_alphaMode)),
                dpiX: _bitmapDpi,
                dpiY: _bitmapDpi);

            hr = D2D1VTable.CreateSharedBitmap(
                (ID2D1RenderTarget*)renderTarget,
                D2D1.IID_IDXGISurface,
                _dxgiSurface,
                props,
                out newBitmap);
        }
        if (hr < 0 || newBitmap == 0)
        {
            return 0;
        }

        ReleaseBitmap();
        _bitmap = newBitmap;
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

        _lease?.Dispose();
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
