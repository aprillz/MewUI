namespace Aprillz.MewUI.Rendering;

/// <summary>
/// An offscreen surface created by an <see cref="IRenderDevice"/>; disposing it releases the backend allocation.
/// </summary>
public interface IRenderSurface : IRenderTarget, IDisposable
{
    /// <summary>Gets the pixel format.</summary>
    RenderPixelFormat Format { get; }

    /// <summary>Gets the usages the surface was created for.</summary>
    SurfaceUsage Usage { get; }

    /// <summary>Gets what the surface can do, such as being drawn into, read on the CPU, or sampled on the GPU.</summary>
    SurfaceCapabilities Capabilities { get; }

    /// <summary>Gets a counter that increases each time the contents change, so a cached derivation can detect a stale copy.</summary>
    ulong Version { get; }

    /// <summary>Gets whether the surface has been disposed.</summary>
    bool IsDisposed { get; }
}

internal interface IBackendSurfaceProvider
{
    IRenderSurface BackendSurface { get; }
}

internal static class RenderSurfaceResource
{
    public static IRenderSurface ResolveBackendSurface(IRenderSurface surface)
    {
        while (surface is IBackendSurfaceProvider provider)
        {
            surface = provider.BackendSurface;
        }
        return surface;
    }
}
