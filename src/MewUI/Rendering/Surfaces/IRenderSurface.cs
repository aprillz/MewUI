namespace Aprillz.MewUI.Rendering;

public interface IRenderSurface : IRenderTarget, IDisposable
{
    RenderPixelFormat Format { get; }

    SurfaceUsage Usage { get; }

    SurfaceCapabilities Capabilities { get; }

    ulong Version { get; }

    bool IsDisposed { get; }
}
