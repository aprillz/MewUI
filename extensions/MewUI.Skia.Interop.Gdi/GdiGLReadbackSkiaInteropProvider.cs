using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;

namespace Aprillz.MewUI.Skia.Interop.Gdi;

internal sealed class GdiGLReadbackSkiaInteropProvider : ISkiaInteropProvider
{
    public string BackendIdentifier => "Gdi";

    public ISkiaSurfaceHost? TryCreateSurfaceHost(IGraphicsFactory factory)
        => factory is GdiGraphicsFactory gdiFactory ? new GdiGLReadbackSkiaSurfaceHost(gdiFactory) : null;
}
