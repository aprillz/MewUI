using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Skia.Interop.MewVG.MacOS;

internal sealed class MewVGMacOSSkiaInteropProvider : ISkiaInteropProvider
{
    public string BackendIdentifier => "MewVG.MacOS";

    public ISkiaSurfaceHost? TryCreateSurfaceHost(IGraphicsFactory factory)
        => new SkiaMetalSurfaceHost(factory);
}
