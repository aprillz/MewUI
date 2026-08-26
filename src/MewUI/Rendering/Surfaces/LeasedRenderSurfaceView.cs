namespace Aprillz.MewUI.Rendering;

internal sealed class LeasedRenderSurfaceView : IRenderSurface, IBackendSurfaceProvider
{
    private readonly IRenderDevice _device;
    private readonly ScratchResourceClass _resourceClass;
    // Pool key attribute of the acquire request; surface capabilities always report Alpha for
    // BGRA formats, so deriving it from the allocation would return under a key no opaque
    // acquire ever asks for.
    private readonly bool _hasAlphaKey;
    private IRenderSurface? _allocation;

    public LeasedRenderSurfaceView(
        IRenderDevice device,
        IRenderSurface allocation,
        int logicalPixelWidth,
        int logicalPixelHeight,
        ScratchResourceClass resourceClass = ScratchResourceClass.General,
        bool hasAlphaKey = true)
    {
        _device = device;
        _allocation = allocation;
        _resourceClass = resourceClass;
        _hasAlphaKey = hasAlphaKey;
        PixelWidth = logicalPixelWidth;
        PixelHeight = logicalPixelHeight;
    }

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public double DpiScale => GetAllocation().DpiScale;
    public RenderPixelFormat Format => GetAllocation().Format;
    public SurfaceUsage Usage => GetAllocation().Usage;
    public SurfaceCapabilities Capabilities => GetAllocation().Capabilities;
    public ulong Version => GetAllocation().Version;
    public bool IsDisposed => Volatile.Read(ref _allocation) is null;

    IRenderSurface IBackendSurfaceProvider.BackendSurface => GetAllocation();

    public void Dispose()
    {
        var allocation = Interlocked.Exchange(ref _allocation, null);
        if (allocation == null)
        {
            return;
        }

        long bytes = RenderResourceMetrics.ScratchBytes(allocation.PixelWidth, allocation.PixelHeight);
        RenderResourceMetrics.ScratchReleased(bytes);
        var cache = _device.ResourceCache;
        if (cache == null || allocation.IsDisposed)
        {
            allocation.Dispose();
            RenderResourceMetrics.ScratchDisposedOutsidePool();
            return;
        }

        var key = new ScratchSurfaceKey(
            allocation.PixelWidth,
            allocation.PixelHeight,
            allocation.DpiScale,
            _hasAlphaKey);
        key = key with { ResourceClass = _resourceClass };
        cache.ReturnScratchSurface(key, allocation);
    }

    private IRenderSurface GetAllocation() => Volatile.Read(ref _allocation)
        ?? throw new ObjectDisposedException(nameof(LeasedRenderSurfaceView));
}
