namespace Aprillz.MewUI.Rendering;

internal sealed class LeasedRenderSurfaceView : IRenderSurface, IBackendSurfaceProvider
{
    private readonly IRenderDevice _device;
    private IRenderSurface? _allocation;

    public LeasedRenderSurfaceView(
        IRenderDevice device,
        IRenderSurface allocation,
        int logicalPixelWidth,
        int logicalPixelHeight)
    {
        _device = device;
        _allocation = allocation;
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

        long bytes = RenderMemoryLedger.ScratchBytes(allocation.PixelWidth, allocation.PixelHeight);
        RenderMemoryLedger.ScratchReleased(bytes);
        var cache = _device.ResourceCache;
        if (cache == null || allocation.IsDisposed)
        {
            allocation.Dispose();
            RenderMemoryLedger.ScratchDisposedOutsidePool();
            return;
        }

        var key = new ScratchSurfaceKey(
            allocation.PixelWidth,
            allocation.PixelHeight,
            allocation.DpiScale,
            allocation.Capabilities.HasFlag(SurfaceCapabilities.Alpha));
        cache.ReturnScratchSurface(key, allocation);
    }

    private IRenderSurface GetAllocation() => Volatile.Read(ref _allocation)
        ?? throw new ObjectDisposedException(nameof(LeasedRenderSurfaceView));
}
