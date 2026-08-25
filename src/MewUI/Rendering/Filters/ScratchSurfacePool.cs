namespace Aprillz.MewUI.Rendering.Filters;

/// <summary>
/// Device-backed scratch <see cref="IRenderSurface"/> pool view. Filter graphs allocate intermediate
/// surfaces per node (Blur, ColorMatrix, etc.); without pooling, a 5-node DAG on a 1024px source
/// allocates 20 MB just for scratches every frame. The pool keeps a small set of recently-used
/// surfaces per (width, height, dpi) bucket and hands them back on rent.
/// </summary>
/// <remarks>
/// Sizing policy: filter intermediates require exact dimensions because their CPU spans expose
/// allocation stride. Other resource classes may opt into bounded oversize reuse through the
/// same device cache without exposing allocation dimensions to consumers.
/// <para/>
/// Lifetime: this wrapper is owned by an <see cref="IImageFilterContext"/> instance, while idle
/// surfaces are owned by the device cache and can be reused by another compatible context.
/// </remarks>
public sealed class ScratchSurfacePool : IDisposable
{
    private readonly IRenderDevice _device;
    private readonly double _dpiScale;
    private readonly Dictionary<IRenderSurface, ScratchSurfaceLease> _leases = new();
    private bool _disposed;

    /// <summary>
    /// Retained for source compatibility. Device-level byte/count budgets now control retention.
    /// </summary>
    public int MaxPerBucket { get; init; } = 4;

    public ScratchSurfacePool(IGraphicsFactory factory, double dpiScale)
        : this((IRenderDevice)(factory ?? throw new ArgumentNullException(nameof(factory))), dpiScale)
    {
    }

    public ScratchSurfacePool(IRenderDevice device, double dpiScale)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _dpiScale = dpiScale > 0 ? dpiScale : 1.0;
    }

    /// <summary>
    /// Rents a scratch surface lease with the exact requested pixel dimensions. Same-size
    /// requests reuse the same bucket; differently-sized requests miss the cache and allocate fresh.
    /// </summary>
    /// <remarks>
    /// Earlier revision rounded up to power-of-2 to bound bucket count, but that broke
    /// pixel layout in callers: <see cref="ICpuPixelSurface.GetWritablePixelSpan"/> reports
    /// stride for the actual width, so a 100-wide source written into a 128-wide scratch
    /// buffer via flat <see cref="System.Span{T}.CopyTo"/> smears the source rows into
    /// arbitrary scratch rows. Exact-size buckets eliminate the impedance mismatch at the
    /// cost of more cache entries; acceptable, as filter graphs typically reuse a single
    /// size for the duration of the source layer.
    /// </remarks>
    public ScratchSurfaceLease RentLease(int pixelWidth, int pixelHeight)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ScratchSurfacePool));

        int w = Math.Max(1, pixelWidth);
        int h = Math.Max(1, pixelHeight);
        var key = new ScratchSurfaceKey(
            w,
            h,
            _dpiScale,
            HasAlpha: true,
            ScratchResourceClass.FilterIntermediate);
        var surface = _device.ResourceCache?.RentScratchSurface(key, exactSizeOnly: true);
        if (surface != null)
        {
            if (surface is IReusableScratchSurface reusable && !reusable.CanReturnToPool)
            {
                surface.Dispose();
                RenderResourceMetrics.ScratchDisposedOutsidePool();
                surface = null;
            }
            else
            {
                long bytes = RenderResourceMetrics.ScratchBytes(surface.PixelWidth, surface.PixelHeight);
                RenderResourceMetrics.ScratchAcquired(bytes, created: false);
                if (surface is not ICpuPixelSurface cpuSurface)
                {
                    surface.Dispose();
                    RenderResourceMetrics.ScratchDisposedOutsidePool();
                    throw new NotSupportedException(
                        $"{nameof(ScratchSurfacePool)} requires CPU-readable render surfaces.");
                }
                if (!_leases.TryGetValue(surface, out var reusedLease))
                {
                    reusedLease = new ScratchSurfaceLease(this, surface, cpuSurface);
                    _leases[surface] = reusedLease;
                }
                reusedLease.State = ScratchSurfaceLeaseState.Active;
                return reusedLease;
            }
        }

        // Filter scratch buffers benefit from the GPU pipeline when the backend supports
        // it. The compatibility device routes to the existing factory methods today,
        // while keeping allocation policy centralized.
        surface = _device.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(
            w,
            h,
            _dpiScale,
            debugName: nameof(ScratchSurfacePool)));

        if (surface is ICpuPixelSurface pixels)
        {
            var lease = new ScratchSurfaceLease(this, surface, pixels);
            _leases[lease.Surface] = lease;
            RenderResourceMetrics.ScratchAcquired(lease.AccountedBytes, created: true);
            return lease;
        }

        surface.Dispose();
        throw new NotSupportedException(
            $"{nameof(ScratchSurfacePool)} currently requires CPU-readable render surfaces.");
    }

    public void Return(IRenderSurface surface)
    {
        if (surface is null) return;
        if (!_leases.TryGetValue(surface, out var lease))
        {
            surface.Dispose();
            return;
        }

        Return(lease);
    }

    public void Return(ScratchSurfaceLease lease)
    {
        if (lease is null || lease.State != ScratchSurfaceLeaseState.Active) return;
        if (_disposed)
        {
            DisposeActiveLease(lease);
            return;
        }

        if (lease.Surface is IReusableScratchSurface reusable && !reusable.CanReturnToPool)
        {
            DisposeActiveLease(lease);
            return;
        }

        RenderResourceMetrics.ScratchReleased(lease.AccountedBytes);
        var cache = _device.ResourceCache;
        if (cache == null)
        {
            RenderResourceMetrics.ScratchDisposedOutsidePool();
            DisposeSurface(lease);
            return;
        }

        var key = new ScratchSurfaceKey(
            lease.Surface.PixelWidth,
            lease.Surface.PixelHeight,
            lease.Surface.DpiScale,
            lease.Surface.Capabilities.HasFlag(SurfaceCapabilities.Alpha),
            ScratchResourceClass.FilterIntermediate);
        lease.State = ScratchSurfaceLeaseState.Pooled;
        cache.ReturnScratchSurface(key, lease.Surface);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _leases.Clear();
    }

    internal void DisposeLease(ScratchSurfaceLease lease)
    {
        if (lease.State == ScratchSurfaceLeaseState.Active)
        {
            DisposeActiveLease(lease);
        }
        else if (lease.State == ScratchSurfaceLeaseState.Pooled)
        {
            lease.State = ScratchSurfaceLeaseState.Disposed;
            _leases.Remove(lease.Surface);
        }
    }

    private void DisposeActiveLease(ScratchSurfaceLease lease)
    {
        RenderResourceMetrics.ScratchReleased(lease.AccountedBytes);
        RenderResourceMetrics.ScratchDisposedOutsidePool();
        DisposeSurface(lease);
    }

    private void DisposeSurface(ScratchSurfaceLease lease)
    {
        _leases.Remove(lease.Surface);
        lease.State = ScratchSurfaceLeaseState.Disposed;
        lease.DisposeSurface();
    }

}

public sealed class ScratchSurfaceLease : IDisposable
{
    private readonly ScratchSurfacePool _owner;
    private bool _disposed;

    internal ScratchSurfaceLease(ScratchSurfacePool owner, IRenderSurface surface, ICpuPixelSurface pixels)
    {
        _owner = owner;
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
        Pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
        AccountedBytes = RenderResourceMetrics.ScratchBytes(surface.PixelWidth, surface.PixelHeight);
    }

    public IRenderSurface Surface { get; }

    public ICpuPixelSurface Pixels { get; }

    internal long AccountedBytes { get; }

    internal ScratchSurfaceLeaseState State { get; set; } = ScratchSurfaceLeaseState.Active;

    public void Dispose() => _owner.DisposeLease(this);

    internal void DisposeSurface()
    {
        if (_disposed) return;
        _disposed = true;
        Surface.Dispose();
    }
}

internal enum ScratchSurfaceLeaseState
{
    Active,
    Pooled,
    Disposed,
}
