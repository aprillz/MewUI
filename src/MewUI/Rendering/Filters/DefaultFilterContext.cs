namespace Aprillz.MewUI.Rendering.Filters;

/// <summary>
/// Default <see cref="IImageFilterContext"/> for graph evaluation. Owns the scratch surface pool
/// and tracks the current source layer (replaceable via <see cref="WithSource"/> for
/// <see cref="ComposeFilter"/> sub-evaluations).
/// </summary>
/// <remarks>
/// Construction takes the source <see cref="IRenderSurface"/> directly so the context can wrap
/// it as a <see cref="BorrowedFilterResult"/> with both <see cref="IImage"/> access and optional
/// CPU pixel access when the surface implements <see cref="ICpuPixelSurface"/>.
/// </remarks>
public sealed class DefaultFilterContext : IImageFilterContext, IDisposable
{
    private readonly ScratchRenderTargetPool _pool;
    private readonly bool _ownsPool;
    private readonly FilterResult _source;
    private bool _disposed;

    public DefaultFilterContext(IRenderSurface sourceLayer, IImage sourceImage, Rect sourceBounds,
        IGraphicsFactory factory, double logicalToPixelScaleX = 1.0, double logicalToPixelScaleY = 1.0)
        : this(new BorrowedFilterResult(sourceImage, sourceBounds, sourceLayer, sourceLayer as ICpuPixelSurface), sourceBounds, factory,
               new ScratchRenderTargetPool(factory, sourceLayer.DpiScale), ownsPool: true,
               logicalToPixelScaleX, logicalToPixelScaleY)
    {
    }

    private DefaultFilterContext(FilterResult source, Rect sourceBounds, IGraphicsFactory factory,
        ScratchRenderTargetPool pool, bool ownsPool,
        double logicalToPixelScaleX, double logicalToPixelScaleY)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        SourceBounds = sourceBounds;
        Factory = factory ?? throw new ArgumentNullException(nameof(factory));
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _ownsPool = ownsPool;
        LogicalToPixelScaleX = logicalToPixelScaleX;
        LogicalToPixelScaleY = logicalToPixelScaleY;
    }

    public FilterResult Source => _source;

    public Rect SourceBounds { get; }

    public IGraphicsFactory Factory { get; }

    public double LogicalToPixelScaleX { get; }

    public double LogicalToPixelScaleY { get; }

    public ScratchFilterResult AcquireScratch(int pixelWidth, int pixelHeight, Rect bounds)
    {
        var lease = _pool.RentLease(pixelWidth, pixelHeight);
        var image = Factory.CreateImageView(lease.Surface);
        return new ScratchFilterResult(lease, image, bounds, l =>
        {
            // Defer the pool return until the image's backend-side GPU/NVG handles are
            // actually released. Backends that wrap a scratch surface zero-copy with NVG (MewVG
            // Metal / GL) have an in-flight draw queue that must flush before the RT can be
            // recycled — otherwise the next AcquireScratch in the same eval hands back the
            // same RT and the next filter node's GPU write overwrites the texture while a
            // queued draw still references it (cross-filter content bleed when UI invalidate
            // races a render). When the backend accepts the deferred callback, image.Dispose
            // queues for the post-flush drain that fires it; otherwise (CPU bitmap, D2D copy)
            // we fall back to the original immediate-return path.
            if (image.TrySetPostReleaseCallback(() => _pool.Return(l)))
            {
                image.Dispose();
            }
            else
            {
                image.Dispose();
                _pool.Return(l);
            }
        });
    }

    public IImageFilterContext WithSource(FilterResult newSource)
    {
        // Sub-context shares the pool but replaces the source. We don't own or dispose the
        // source; the upstream graph result stays responsible for its lifetime.
        return new DefaultFilterContext(newSource, newSource.Bounds, Factory, _pool, ownsPool: false,
            LogicalToPixelScaleX, LogicalToPixelScaleY);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_ownsPool)
        {
            _pool.Dispose();
        }
    }
}
