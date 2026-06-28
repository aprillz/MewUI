using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

public partial class Window
{
    private VectorSurfaceReclaimer? _vectorSurfaceReclaimer;

    // Retains offscreen vector-cache surfaces parked by detached Image controls (e.g. virtualized
    // tiles) so re-realization reuses them instead of rebuilding the offscreen surface. Created on
    // demand and disposed when this window's graphics resources are released.
    internal VectorSurfaceReclaimer VectorSurfaceReclaimer => _vectorSurfaceReclaimer ??= new VectorSurfaceReclaimer();

    private void DisposeVectorSurfaceReclaimer()
    {
        _vectorSurfaceReclaimer?.Dispose();
        _vectorSurfaceReclaimer = null;
    }
}
