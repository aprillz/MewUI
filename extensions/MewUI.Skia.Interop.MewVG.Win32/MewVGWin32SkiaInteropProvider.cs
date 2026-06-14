using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Skia.Rendering;

namespace Aprillz.MewUI.Skia.Interop.MewVG.Win32;

internal sealed class MewVGWin32SkiaInteropProvider : ISkiaInteropProvider
{
    public string BackendIdentifier => "MewVG.Win32";

    public ISkiaSurfaceHost? TryCreateSurfaceHost(IGraphicsFactory factory)
        => new SkiaGLSurfaceHost(factory);
}
