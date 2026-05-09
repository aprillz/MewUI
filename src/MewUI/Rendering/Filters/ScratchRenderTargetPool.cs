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
    private readonly Dictionary<(int Width, int Height), Stack<IBitmapRenderTarget>> _buckets = new();
    private readonly Dictionary<IBitmapRenderTarget, IRenderSurface> _surfaces = new();
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
    {
        if (_disposed) throw new ObjectDisposedException(nameof(ScratchRenderTargetPool));

        int w = Math.Max(1, pixelWidth);
        int h = Math.Max(1, pixelHeight);
        var key = (w, h);

        if (_buckets.TryGetValue(key, out var stack))
        {
            while (stack.Count > 0)
            {
                var target = stack.Pop();
                if (target is IReusableScratchRenderTarget reusable && !reusable.CanReturnToPool)
                {
                    DisposeTarget(target);
                    continue;
                }

                return target;
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
            _surfaces[bitmapSurface.Target] = surface;
            return bitmapSurface.Target;
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
        if (_disposed)
        {
            DisposeTarget(target);
            return;
        }

        if (target is IReusableScratchRenderTarget reusable && !reusable.CanReturnToPool)
        {
            DisposeTarget(target);
            return;
        }

        var key = (target.PixelWidth, target.PixelHeight);
        if (!_buckets.TryGetValue(key, out var stack))
        {
            stack = new Stack<IBitmapRenderTarget>();
            _buckets[key] = stack;
        }

        if (stack.Count >= MaxPerBucket)
        {
            DisposeTarget(target);
            return;
        }

        stack.Push(target);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var stack in _buckets.Values)
        {
            while (stack.Count > 0)
            {
                DisposeTarget(stack.Pop());
            }
        }
        _buckets.Clear();
    }

    private void DisposeTarget(IBitmapRenderTarget target)
    {
        if (_surfaces.Remove(target, out var surface))
        {
            surface.Dispose();
            return;
        }

        target.Dispose();
    }
}
