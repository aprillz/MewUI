using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Direct2D;

/// <summary>
/// Backend marker interface for pixel sources whose underlying GPU resource is a native
/// Direct2D bitmap (<c>ID2D1Bitmap1*</c>) on a specific device context. Direct2D
/// consumers (e.g. <c>Direct2DImage.GetOrCreateBitmap</c>) cast to this to enable the
/// zero-copy fast path when source and consumer share a device context.
/// </summary>
/// <remarks>
/// Sources outside the D2D backend (GL FBO, Metal texture, etc.) do NOT implement this
/// interface; the cross-backend cast naturally fails and falls through to the CPU
/// <see cref="IPixelBufferSource.Lock"/> readback path.
/// </remarks>
public interface ID2DTextureSource : IGpuTextureSource
{
    /// <summary>
    /// Native <c>ID2D1Bitmap1*</c>. Lifetime is owned by the source - consumers MUST NOT
    /// release this pointer. Returns 0 when the bitmap hasn't been realised yet (e.g.
    /// CPU-only consumer never triggered GPU init).
    /// </summary>
    nint NativeBitmap { get; }

    /// <summary>
    /// The <c>ID2D1DeviceContext*</c> that produced <see cref="NativeBitmap"/>. D2D
    /// bitmaps are device-bound - consumers compare this against their own context's
    /// device (or shared filter device context) by reference equality to decide whether
    /// they can sample the bitmap directly without a copy.
    /// </summary>
    nint OwningDeviceContext { get; }

    /// <summary>
    /// Alpha mode the underlying bitmap was created with. Exposed via the backend-agnostic
    /// <see cref="BitmapAlphaMode"/> rather than the native <c>D2D1_ALPHA_MODE</c> so
    /// cross-backend consumers can read it without depending on D2D headers.
    /// </summary>
    BitmapAlphaMode AlphaMode { get; }
}
