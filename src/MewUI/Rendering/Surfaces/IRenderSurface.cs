namespace Aprillz.MewUI.Rendering;

public interface IRenderSurface : IRenderTarget, IDisposable
{
    RenderPixelFormat Format { get; }

    SurfaceUsage Usage { get; }

    SurfaceCapabilities Capabilities { get; }

    ulong Version { get; }

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
