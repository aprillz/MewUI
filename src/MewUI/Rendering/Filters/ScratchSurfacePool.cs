namespace Aprillz.MewUI.Rendering.Filters;

/// <summary>
/// Per-context scratch <see cref="IRenderSurface"/> pool. Filter graphs allocate intermediate
/// surfaces per node (Blur, ColorMatrix, etc.); without pooling, a 5-node DAG on a 1024px source
/// allocates 20 MB just for scratches every frame. The pool keeps a small set of recently-used
/// surfaces per (width, height, dpi) bucket and hands them back on rent.
/// </summary>
/// <remarks>
/// Sizing policy: rents return a surface whose dimensions are at least the requested ones,
/// drawn from the largest bucket that satisfies the request. Over-sized rents are acceptable:
/// the consumer renders into the requested viewport and ignores the extra pixels. This keeps
/// the bucket count bounded (one per power-of-two extent) instead of one-per-exact-dimension.
/// <para/>
/// Lifetime: a pool is owned by an <see cref="IImageFilterContext"/> instance. When the context
/// is disposed the pool releases all retained surfaces. No cross-context sharing: different
/// graph evaluations use independent pools to avoid synchronization on the rent path.
/// </remarks>
public sealed class ScratchSurfacePool : IDisposable
{
    private readonly IRenderDevice _device;
    private readonly double _dpiScale;
    private readonly Dictionary<(int Width, int Height), Stack<ScratchSurfaceLease>> _buckets = new();
    private readonly Dictionary<IRenderSurface, ScratchSurfaceLease> _leases = new();
    private bool _disposed;

    /// <summary>
    /// Maximum retained surfaces per bucket. Beyond this, returned surfaces are disposed
    /// immediately. Keeps memory bounded under "many one-shot" workloads.
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
        var key = (w, h);

        if (_buckets.TryGetValue(key, out var stack))
        {
            while (stack.Count > 0)
            {
                var lease = stack.Pop();
                if (lease.Surface is IReusableScratchSurface reusable && !reusable.CanReturnToPool)
                {
                    DisposeLease(lease);
                    continue;
                }

                return lease;
            }
        }

        // Filter scratch buffers benefit from the GPU pipeline when the backend supports
        // it. The compatibility device routes to the existing factory methods today,
        // while keeping allocation policy centralized.
        var surface = _device.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(
            w,
            h,
            _dpiScale,
            debugName: nameof(ScratchSurfacePool)));

        if (surface is ICpuPixelSurface pixels)
        {
            var lease = new ScratchSurfaceLease(surface, pixels);
            _leases[lease.Surface] = lease;
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
        if (lease is null) return;
        if (_disposed)
        {
            DisposeLease(lease);
            return;
        }

        if (lease.Surface is IReusableScratchSurface reusable && !reusable.CanReturnToPool)
        {
            DisposeLease(lease);
            return;
        }

        var key = (lease.Surface.PixelWidth, lease.Surface.PixelHeight);
        if (!_buckets.TryGetValue(key, out var stack))
        {
            stack = new Stack<ScratchSurfaceLease>();
            _buckets[key] = stack;
        }

        if (stack.Count >= MaxPerBucket)
        {
            DisposeLease(lease);
            return;
        }

        stack.Push(lease);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var stack in _buckets.Values)
        {
            while (stack.Count > 0)
            {
                DisposeLease(stack.Pop());
            }
        }
        _buckets.Clear();
    }

    private void DisposeLease(ScratchSurfaceLease lease)
    {
        _leases.Remove(lease.Surface);
        lease.Dispose();
    }
}

public sealed class ScratchSurfaceLease : IDisposable
{
    private bool _disposed;

    internal ScratchSurfaceLease(IRenderSurface surface, ICpuPixelSurface pixels)
    {
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
        Pixels = pixels ?? throw new ArgumentNullException(nameof(pixels));
    }

    public IRenderSurface Surface { get; }

    public ICpuPixelSurface Pixels { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Surface.Dispose();
    }
}
