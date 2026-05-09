namespace Aprillz.MewUI.Rendering.Filters;

/// <summary>
/// Per-context scratch <see cref="IBitmapRenderTarget"/> pool. Filter graphs allocate intermediate
/// RTs per node (Blur, ColorMatrix, etc.); without pooling, a 5-node DAG on a 1024² source
/// allocates 20 MB just for scratches every frame. The pool keeps a small set of recently-used
/// targets per (width, height, dpi) bucket and hands them back on rent.
/// </summary>
/// <remarks>
/// Sizing policy: rents return a target whose dimensions are at least the requested ones,
/// drawn from the largest bucket that satisfies the request. Over-sized rents are acceptable —
/// the consumer renders into the requested viewport and ignores the extra pixels. This keeps
/// the bucket count bounded (one per power-of-two extent) instead of one-per-exact-dimension.
/// <para/>
/// Lifetime: a pool is owned by an <see cref="IImageFilterContext"/> instance. When the context
/// is disposed the pool releases all retained targets. No cross-context sharing — different
/// graph evaluations use independent pools to avoid synchronization on the rent path.
/// </remarks>
public sealed class ScratchRenderTargetPool : IDisposable
{
    private readonly IRenderDevice _device;
    private readonly double _dpiScale;
    private readonly Dictionary<(int Width, int Height), Stack<ScratchRenderTargetLease>> _buckets = new();
    private readonly Dictionary<IBitmapRenderTarget, ScratchRenderTargetLease> _leases = new();
    private bool _disposed;

    /// <summary>
    /// Maximum retained targets per bucket. Beyond this, returned targets are disposed
    /// immediately. Keeps memory bounded under "many one-shot" workloads.
    /// </summary>
    public int MaxPerBucket { get; init; } = 4;

    public ScratchRenderTargetPool(IGraphicsFactory factory, double dpiScale)
        : this(factory?.AsRenderDevice() ?? throw new ArgumentNullException(nameof(factory)), dpiScale)
    {
    }

    public ScratchRenderTargetPool(IRenderDevice device, double dpiScale)
    {
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _dpiScale = dpiScale > 0 ? dpiScale : 1.0;
    }

    /// <summary>
    /// Rents a target with the exact requested pixel dimensions. Same-size requests reuse
    /// the same bucket; differently-sized requests miss the cache and allocate fresh.
    /// </summary>
    /// <remarks>
    /// Earlier revision rounded up to power-of-2 to bound bucket count, but that broke
    /// pixel layout in callers: <see cref="IBitmapRenderTarget.GetPixelSpan"/> reports
    /// stride for the actual width, so a 100-wide source written into a 128-wide scratch
    /// buffer via flat <see cref="System.Span{T}.CopyTo"/> smears the source rows into
    /// arbitrary scratch rows. Exact-size buckets eliminate the impedance mismatch at the
    /// cost of more cache entries — acceptable, as filter graphs typically reuse a single
    /// size for the duration of the source layer.
    /// </remarks>
    public IBitmapRenderTarget Rent(int pixelWidth, int pixelHeight)
        => RentLease(pixelWidth, pixelHeight).Target;

    public ScratchRenderTargetLease RentLease(int pixelWidth, int pixelHeight)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ScratchRenderTargetPool));

        int w = Math.Max(1, pixelWidth);
        int h = Math.Max(1, pixelHeight);
        var key = (w, h);

        if (_buckets.TryGetValue(key, out var stack))
        {
            while (stack.Count > 0)
            {
                var lease = stack.Pop();
                var target = lease.Target;
                if (target is IReusableScratchRenderTarget reusable && !reusable.CanReturnToPool)
                {
                    DisposeLease(lease);
                    continue;
                }

                return lease;
            }
        }

        // Filter scratch buffers benefit from the GPU pipeline when the backend supports
        // it (Direct2D's shared device, MewVG's FBO). The compatibility device routes to
        // the existing factory methods today, while keeping allocation policy centralized.
        var surface = _device.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(
            w,
            h,
            _dpiScale,
            debugName: nameof(ScratchRenderTargetPool)));

        if (surface is BitmapRenderTargetSurfaceAdapter bitmapSurface)
        {
            var lease = new ScratchRenderTargetLease(surface, bitmapSurface.Target);
            _leases[lease.Target] = lease;
            return lease;
        }

        surface.Dispose();
        throw new NotSupportedException(
            $"{nameof(ScratchRenderTargetPool)} currently requires bitmap-backed render surfaces.");
    }

    /// <summary>
    /// Returns a target to the pool for reuse. If the bucket is at capacity, the target
    /// is disposed immediately.
    /// </summary>
    public void Return(IBitmapRenderTarget target)
    {
        if (target is null) return;
        if (!_leases.TryGetValue(target, out var lease))
        {
            target.Dispose();
            return;
        }

        Return(lease);
    }

    public void Return(ScratchRenderTargetLease lease)
    {
        if (lease is null) return;
        var target = lease.Target;
        if (_disposed)
        {
            DisposeLease(lease);
            return;
        }

        if (target is IReusableScratchRenderTarget reusable && !reusable.CanReturnToPool)
        {
            DisposeLease(lease);
            return;
        }

        var key = (target.PixelWidth, target.PixelHeight);
        if (!_buckets.TryGetValue(key, out var stack))
        {
            stack = new Stack<ScratchRenderTargetLease>();
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

    private void DisposeLease(ScratchRenderTargetLease lease)
    {
        _leases.Remove(lease.Target);
        lease.Dispose();
    }
}

public sealed class ScratchRenderTargetLease : IDisposable
{
    private bool _disposed;

    internal ScratchRenderTargetLease(IRenderSurface surface, IBitmapRenderTarget target)
    {
        Surface = surface ?? throw new ArgumentNullException(nameof(surface));
        Target = target ?? throw new ArgumentNullException(nameof(target));
    }

    public IRenderSurface Surface { get; }

    public IBitmapRenderTarget Target { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Surface.Dispose();
    }
}
