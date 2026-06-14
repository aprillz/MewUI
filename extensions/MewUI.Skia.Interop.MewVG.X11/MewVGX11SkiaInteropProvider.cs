using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Skia.Rendering;

namespace Aprillz.MewUI.Skia.Interop.MewVG.X11;

internal sealed class MewVGX11SkiaInteropProvider : ISkiaInteropProvider
{
    public string BackendIdentifier => "MewVG.X11";

    public ISkiaSurfaceHost? TryCreateSurfaceHost(IGraphicsFactory factory)
        => new SkiaGLSurfaceHost(factory);
}
