using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.Direct2D;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Direct2D;

/// <summary>
/// IImage that wraps an externally-created native <c>ID2D1Bitmap*</c> (typically obtained
/// via <c>ID2D1DeviceContext::CreateBitmapFromDxgiSurface</c> or similar interop). Used by
/// callers (video sample, custom interop helpers) that need to hand a D3D11/DXGI-derived
/// bitmap to the MewUI rendering pipeline without going through a CPU pixel buffer.
/// </summary>
/// <remarks>
/// <para>
/// Ownership: the constructor calls <c>AddRef</c> on the supplied native bitmap so the
/// caller can release its own reference independently. <see cref="Dispose"/> releases the
/// reference held by this wrapper.
/// </para>
/// <para>
/// Same-device requirement: D2D bitmaps are device-bound. The wrapped bitmap must live on
/// the same <c>ID2D1Device</c> as the factory that produced this image — otherwise
/// <c>DrawBitmap</c> on the consuming render target will fail. Use
/// <see cref="Direct2DGraphicsFactory.NativeD2DDevice"/> when constructing the bitmap to
/// guarantee this.
/// </para>
/// </remarks>
internal sealed unsafe class Direct2DNativeBitmapImage : IImage
{
    private nint _bitmap;
    private bool _disposed;

    public int PixelWidth { get; }
    public int PixelHeight { get; }

    public Direct2DNativeBitmapImage(nint nativeBitmap, int pixelWidth, int pixelHeight)
    {
        if (nativeBitmap == 0) throw new ArgumentException("Native bitmap pointer is 0.", nameof(nativeBitmap));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelHeight, 0);

        // AddRef so the caller can Release their own reference independently. We hold the
        // bitmap alive as long as this IImage exists.
        ComHelpers.AddRef(nativeBitmap);
        _bitmap = nativeBitmap;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }

    /// <summary>
    /// Returns the wrapped native bitmap pointer for the consuming render target. The
    /// <paramref name="renderTarget"/> argument is ignored: D2D bitmaps work across any
    /// <c>ID2D1DeviceContext</c> sharing the same <c>ID2D1Device</c>, so no
    /// per-RT recreate is needed.
    /// </summary>
    public nint GetOrCreateBitmap(nint renderTarget, int renderTargetGeneration)
    {
        _ = renderTarget;
        _ = renderTargetGeneration;
        return _disposed ? 0 : _bitmap;
    }

    ~Direct2DNativeBitmapImage() => ReleaseNativeHandles();

    public void Dispose()
    {
        ReleaseNativeHandles();
        GC.SuppressFinalize(this);
    }

    private void ReleaseNativeHandles()
    {
        if (_disposed) return;
        _disposed = true;
        if (_bitmap != 0)
        {
            ComHelpers.Release(_bitmap);
            _bitmap = 0;
        }
    }
}
