using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;

namespace Aprillz.MewUI.Skia.Interop.Direct2D;

internal sealed class Direct2DSkiaInteropProvider : ISkiaInteropProvider
{
    public string BackendIdentifier => "Direct2D";

    public ISkiaSurfaceHost? TryCreateSurfaceHost(IGraphicsFactory factory)
        => factory is Direct2DGraphicsFactory d2dFactory ? new SkiaWglInteropHost(d2dFactory) : null;
}
