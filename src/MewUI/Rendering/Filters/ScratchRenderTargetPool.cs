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
    private readonly IGraphicsFactory _factory;
    private readonly double _dpiScale;
    private readonly Dictionary<(int Width, int Height), Stack<IBitmapRenderTarget>> _buckets = new();
    private bool _disposed;

    /// <summary>
    /// Maximum retained targets per bucket. Beyond this, returned targets are disposed
    /// immediately. Keeps memory bounded under "many one-shot" workloads.
    /// </summary>
    public int MaxPerBucket { get; init; } = 4;

    public ScratchRenderTargetPool(IGraphicsFactory factory, double dpiScale)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
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
                    target.Dispose();
                    continue;
                }

                return target;
            }
        }

        // Filter scratch buffers benefit from the GPU pipeline when the backend supports
        // it (Direct2D's shared device, MewVG's FBO). CreateOffscreenRenderTarget routes
        // to the optimal RT type per backend; default impl falls back to the regular bitmap.
        return _factory.CreateOffscreenRenderTarget(w, h, _dpiScale);
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
            target.Dispose();
            return;
        }

        if (target is IReusableScratchRenderTarget reusable && !reusable.CanReturnToPool)
        {
            target.Dispose();
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
            target.Dispose();
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
                stack.Pop().Dispose();
            }
        }
        _buckets.Clear();
    }

    private static int NextPow2(int value)
    {
        if (value <= 1) return 1;
        value--;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        return value + 1;
    }
}
