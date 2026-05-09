namespace Aprillz.MewUI.Rendering;

public interface IRenderSurface : IDisposable
{
    int PixelWidth { get; }

    int PixelHeight { get; }

    double DpiScale { get; }

    RenderPixelFormat Format { get; }

    SurfaceUsage Usage { get; }

    SurfaceCapabilities Capabilities { get; }

    ulong Version { get; }

    bool IsDisposed { get; }
}
