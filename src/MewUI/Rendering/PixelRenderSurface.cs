using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Render target for offscreen bitmap rendering.
/// Implementations manage platform-specific resources.
/// Use <see cref="IRenderDevice.CreateSurface"/> to create instances.
/// </summary>
public interface IPixelRenderSurface : IPixelBufferSource, ICpuPixelSurface, IDeferredCpuReadableSurface, IDisposable
{
    /// <summary>
    /// Gets the pixel width. Resolves ambiguity between IRenderTarget and IPixelBufferSource.
    /// </summary>
    new int PixelWidth { get; }

    /// <summary>
    /// Gets the pixel height. Resolves ambiguity between IRenderTarget and IPixelBufferSource.
    /// </summary>
    new int PixelHeight { get; }

    /// <summary>
    /// Gets the DPI scale factor. Resolves ambiguity between IRenderTarget and IRenderSurface.
    /// </summary>
    new double DpiScale { get; }

    /// <summary>
    /// Gets the stride in bytes per row. Resolves ambiguity between IPixelBufferSource and ICpuPixelSurface.
    /// </summary>
    new int StrideBytes { get; }

    /// <summary>
    /// Gets the pixel version. Resolves ambiguity between IPixelBufferSource and IRenderSurface.
    /// </summary>
    new int Version { get; }

    /// <summary>
    /// Gets the pixel format of the bitmap (always BGRA32).
    /// </summary>
    new BitmapPixelFormat PixelFormat { get; }

    /// <summary>
    /// Copies the rendered pixels to a new array.
    /// </summary>
    /// <returns>A copy of the pixel buffer in BGRA32 format, or empty array if disposed.</returns>
    new byte[] CopyPixels();

    /// <summary>
    /// Gets a span over the pixel buffer for direct access.
    /// </summary>
    /// <returns>A span over the pixels, or empty span if disposed.</returns>
    Span<byte> GetPixelSpan();

    /// <summary>
    /// Clears the pixel buffer to the specified color.
    /// </summary>
    void Clear(Color color);

    /// <summary>
    /// Increments the version to signal that pixels have changed.
    /// Call this after modifying pixels via GetPixelSpan() or IGraphicsContext.
    /// </summary>
    new void IncrementVersion();

    /// <summary>
    /// <see langword="true"/> if pixels in this target are stored with
    /// premultiplied alpha (the channel values are pre-scaled by alpha so
    /// that a half-opacity white reads back as <c>(128, 128, 128, 128)</c>);
    /// <see langword="false"/> when the target stores straight alpha
    /// (half-opacity white reads back as <c>(255, 255, 255, 128)</c>).
    /// <para/>
    /// Consumers that mix or convolve pixels (e.g. a Gaussian blur for SVG
    /// filters) must know which space the data lives in: premultiplied pixels
    /// are linear-blendable as-is, while straight pixels need to be
    /// premultiplied first or edges become darker / brighter depending on the
    /// direction of the format mismatch.
    /// </summary>
    new bool IsPremultiplied => false;

    RenderPixelFormat IRenderSurface.Format => IsPremultiplied
        ? RenderPixelFormat.Bgra8888Premultiplied
        : RenderPixelFormat.Bgra8888;

    double IRenderTarget.DpiScale => DpiScale;

    SurfaceUsage IRenderSurface.Usage =>
        SurfaceUsage.Offscreen | SurfaceUsage.ImageSource | SurfaceUsage.ReadbackSource;

    SurfaceCapabilities IRenderSurface.Capabilities
    {
        get
        {
            var capabilities =
                SurfaceCapabilities.Renderable |
                SurfaceCapabilities.CpuReadable |
                SurfaceCapabilities.CpuWritable |
                SurfaceCapabilities.Alpha;

            if (IsPremultiplied)
            {
                capabilities |= SurfaceCapabilities.Premultiplied;
            }

            if (LockMode == LockMode.Readback)
            {
                capabilities |= SurfaceCapabilities.DeferredReadback;
            }

            if (this is IGpuTextureSource)
            {
                capabilities |= SurfaceCapabilities.GpuSampleable;
            }

            return capabilities;
        }
    }

    ulong IRenderSurface.Version => (ulong)Math.Max(0, Version);

    int ICpuPixelSurface.StrideBytes => StrideBytes;

    byte[] ICpuPixelSurface.CopyPixels() => CopyPixels();

    void ICpuPixelSurface.IncrementVersion() => IncrementVersion();

    bool IRenderSurface.IsDisposed => false;

    ReadOnlySpan<byte> ICpuPixelSurface.GetReadOnlyPixelSpan() => GetPixelSpan();

    Span<byte> ICpuPixelSurface.GetWritablePixelSpan() => GetPixelSpan();

    bool IDeferredCpuReadableSurface.HasPendingReadback => LockMode == LockMode.Readback;

    IRenderOperation IDeferredCpuReadableSurface.RequestReadback()
    {
        if (LockMode == LockMode.Readback)
        {
            _ = CopyPixels();
        }

        return RenderOperation.Completed;
    }

    bool IDeferredCpuReadableSurface.TryFlushReadback()
    {
        if (LockMode == LockMode.Readback)
        {
            _ = CopyPixels();
        }

        return true;
    }
}
