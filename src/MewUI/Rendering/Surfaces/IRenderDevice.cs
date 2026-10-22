using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// A backend device that creates offscreen surfaces, draws into them, and exposes them as images.
/// </summary>
/// <remarks>
/// Surfaces and images belong to the device that created them. Backends with thread-affine state
/// require <see cref="IGraphicsFactory.AcquireBackgroundRenderScope"/> around calls made off the UI thread.
/// </remarks>
public interface IRenderDevice
{
    /// <summary>Gets the identity that partitions cached resources by device and device generation; the default is empty.</summary>
    RenderDeviceIdentity RenderIdentity => default;

    /// <summary>Creates a surface described by <paramref name="descriptor"/>; the caller owns and disposes it.</summary>
    IRenderSurface CreateSurface(RenderSurfaceDescriptor descriptor);

    /// <summary>
    /// Creates a context that draws into <paramref name="surface"/>; throws <see cref="NotSupportedException"/>
    /// when the surface is not <see cref="SurfaceCapabilities.Renderable"/>.
    /// </summary>
    IGraphicsContext CreateContext(IRenderSurface surface);

    /// <summary>
    /// Creates an image that aliases the pixels of <paramref name="surface"/>; the surface is released
    /// only after its last view has been disposed.
    /// </summary>
    IImage CreateImageView(IRenderSurface surface);

    /// <summary>Creates an image over a CPU pixel source.</summary>
    IImage CreateImageView(IPixelBufferSource source);

    /// <summary>
    /// Creates an image that samples an externally owned raster resource; throws
    /// <see cref="NotSupportedException"/> when the backend cannot import it.
    /// </summary>
    IImage CreateImageView(IExternalRasterSource source);

    /// <summary>
    /// Copies the pixels of <paramref name="source"/> as BGRA32 rows spaced
    /// <paramref name="destinationStrideBytes"/> apart into <paramref name="destination"/>; returns
    /// <see langword="false"/> when the surface is not CPU-readable or the destination is too small.
    /// </summary>
    bool TryReadPixels(IRenderSurface source, Span<byte> destination, int destinationStrideBytes);

    /// <summary>
    /// Starts the readback that makes the pixels of a <see cref="SurfaceCapabilities.DeferredReadback"/>
    /// surface CPU-readable; the returned operation is already completed for other surfaces.
    /// </summary>
    IRenderOperation RequestReadback(IRenderSurface source);

    /// <summary>Returns an operation that completes once the backend's queued asynchronous work has finished.</summary>
    IRenderOperation FlushAsyncWork();

    /// <summary>
    /// Gets the cache that holds this device's resident resources and scratch surfaces, or
    /// <see langword="null"/> when the device has none.
    /// </summary>
    IRenderResourceCache? ResourceCache { get; }

    /// <summary>Gets the shader effect device, or <see langword="null"/> when the backend runs no custom shaders.</summary>
    IRenderEffectDevice? Effects { get; }
}

/// <summary>Identifies a render device for cache partitioning.</summary>
/// <param name="DeviceId">Process-unique id of the device.</param>
/// <param name="Generation">Incremented when the backend recreates its GPU device, so resources of the previous generation are not reused.</param>
/// <param name="ContextId">Id of the context within the device; 0 when the device has a single context.</param>
public readonly record struct RenderDeviceIdentity(ulong DeviceId, uint Generation, ulong ContextId = 0)
{
    private static long _nextDeviceId;

    internal static ulong AllocateDeviceId() => (ulong)Interlocked.Increment(ref _nextDeviceId);
}
