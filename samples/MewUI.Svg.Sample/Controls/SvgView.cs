using System.Diagnostics;
using System.Numerics;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

using Svg;

namespace Aprillz.MewUI.Svg.Sample.Controls;

public sealed class SvgView : FrameworkElement
{
    private const double MaxPreviewExtent = 2048;
    private const int MaxCachePixelExtent = 4096;

    // Diagnostic toggle. When false, every frame re-renders the SVG into a fresh bitmap
    // — no pan reuse, no cache hit. Used to A/B test whether per-frame perf or memory
    // issues stem from the cache itself or from the render pipeline underneath.
    public bool CacheEnabled { get; set; } = true;

    private SvgDocument? _cachedDocument;
    private Rect _cachedLocalRect;
    private int _cachedPixelWidth;
    private int _cachedPixelHeight;
    private double _cachedDpiScale;
    private IRenderSurface? _cachedSurface;
    private IImage? _cachedImage;

    // Background-rebuild state. _rebuildInProgress is read/written from both UI and
    // worker threads — volatile is sufficient (no compound state, just gate the kickoff).
    private volatile bool _rebuildInProgress;
    // Snapshot of the request that triggered the in-progress rebuild. UI thread compares
    // current view params against this on subsequent OnRender calls so it doesn't queue
    // duplicate rebuilds while one is already running.
    private double _pendingDpiScale;
    private Rect _pendingLocalRect;

    private static readonly MewPropertyKey<TimeSpan> LastDrawTimePropertyKey =
        MewProperty<TimeSpan>.RegisterReadOnly<SvgView>(nameof(LastDrawTime), TimeSpan.Zero, MewPropertyOptions.None);

    /// <summary>
    /// Time taken by the most recent cached-bitmap rebuild (i.e. the last full SVG render).
    /// <see cref="TimeSpan.Zero"/> when no rebuild has occurred yet, or when the last render
    /// hit the cache. Bindable — host UI can observe via the standard binding pipeline.
    /// </summary>
    public static readonly MewProperty<TimeSpan> LastDrawTimeProperty = LastDrawTimePropertyKey.Property;

    private SvgDocument? _document;

    /// <summary>
    /// SVG source. Assigning a different document drops the bitmap cache so the next
    /// render reflects the new content immediately (otherwise the stale bitmap would
    /// linger until a zoom/pan triggered the cache check).
    /// </summary>
    public SvgDocument? Document
    {
        get => _document;
        set
        {
            if (ReferenceEquals(_document, value))
            {
                return;
            }
            _document = value;
            InvalidateCache();
        }
    }

    public double ContentPadding { get; set; } = 0;

    public TimeSpan LastDrawTime => GetValue(LastDrawTimeProperty);

    /// <summary>
    /// Forces the next render to rebuild the cached bitmap. Call this after mutating the
    /// <see cref="Document"/> in place — assigning a different document is detected
    /// automatically, but in-place edits keep the same reference and bypass the check.
    /// </summary>
    public void InvalidateCache()
    {
        ReleaseCache();
        InvalidateVisual();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (Document is null)
        {
            return new Size(320, 320);
        }

        double width = Math.Max(1, Document.ViewBoxWidth);
        double height = Math.Max(1, Document.ViewBoxHeight);

        double longestSide = Math.Max(width, height);
        if (longestSide > MaxPreviewExtent)
        {
            double scale = MaxPreviewExtent / longestSide;
            width *= scale;
            height *= scale;
        }

        return new Size(width, height);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        if (Document is null)
        {
            return;
        }

        double padding = Math.Max(0, ContentPadding);
        var insetBounds = new Rect(padding, padding, Math.Max(0, ActualWidth - padding * 2), Math.Max(0, ActualHeight - padding * 2));
        if (insetBounds.Width <= 0 || insetBounds.Height <= 0)
        {
            return;
        }

        var renderBounds = FitViewBox(insetBounds, Document.ViewBoxWidth, Document.ViewBoxHeight);
        if (renderBounds.Width <= 0 || renderBounds.Height <= 0)
        {
            return;
        }

        // Reduce the cached region to whatever portion of renderBounds is actually visible
        // on screen. When the SvgView lives inside a ZoomPanCanvas zoomed in 4×, the visible
        // strip is ~25% of renderBounds — caching the whole renderBounds at 4× resolution
        // would allocate 16× the necessary bitmap.
        var visibleLocalRect = ComputeVisibleLocalRect(context, renderBounds);
        if (visibleLocalRect.Width <= 0 || visibleLocalRect.Height <= 0)
        {
            return;
        }

        try
        {
            // Background rebuild: instead of synchronously rendering the SVG into the
            // offscreen bitmap on the UI thread (multi-hundred-ms for complex SVGs), kick
            // off Task.Run for the heavy work and draw whatever we have cached in the
            // meantime. The first frame after a zoom change shows stale content (or
            // nothing if no cache yet); next OnRender after the worker commits sees the
            // fresh image. D2D MULTI_THREADED + the GDI per-HDC concurrency model
            // both support worker-thread render-target creation.
            MaybeStartBackgroundRebuild(context, visibleLocalRect, renderBounds);
            if (_cachedImage is not null)
            {
                context.DrawImage(_cachedImage, _cachedLocalRect);
            }
        }
        catch { }
    }

    /// <summary>
    /// Maps the visual root's client viewport into this element's local coordinate space and
    /// intersects it with the element's render bounds. Returns the portion of renderBounds
    /// that is actually on screen, accounting for any ancestor transform (e.g. ZoomPanCanvas).
    /// </summary>
    private Rect ComputeVisibleLocalRect(IGraphicsContext context, Rect renderBounds)
    {
        if (FindVisualRoot() is not Window window)
        {
            return renderBounds;
        }

        var transform = context.GetTransform();
        if (!Matrix3x2.Invert(transform, out var inverse))
        {
            return renderBounds;
        }

        var clientSize = window.ClientSize;
        var p0 = Vector2.Transform(new Vector2(0, 0), inverse);
        var p1 = Vector2.Transform(new Vector2((float)clientSize.Width, 0), inverse);
        var p2 = Vector2.Transform(new Vector2(0, (float)clientSize.Height), inverse);
        var p3 = Vector2.Transform(new Vector2((float)clientSize.Width, (float)clientSize.Height), inverse);

        double minX = Math.Min(Math.Min(p0.X, p1.X), Math.Min(p2.X, p3.X));
        double minY = Math.Min(Math.Min(p0.Y, p1.Y), Math.Min(p2.Y, p3.Y));
        double maxX = Math.Max(Math.Max(p0.X, p1.X), Math.Max(p2.X, p3.X));
        double maxY = Math.Max(Math.Max(p0.Y, p1.Y), Math.Max(p2.Y, p3.Y));

        double left = Math.Max(renderBounds.X, minX);
        double top = Math.Max(renderBounds.Y, minY);
        double right = Math.Min(renderBounds.Right, maxX);
        double bottom = Math.Min(renderBounds.Bottom, maxY);
        if (right <= left || bottom <= top)
        {
            return Rect.Empty;
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    private readonly record struct RebuildRequest(
        SvgDocument Document,
        Rect VisibleLocalRect,
        Rect RenderBounds,
        double EffectiveScale,
        int PixelWidth,
        int PixelHeight);

    /// <summary>UI-thread entry point: decide whether the cache satisfies the current
    /// view, and if not, start a background rebuild. Compares the requested params to
    /// any in-progress rebuild's snapshot to avoid queuing duplicate work.</summary>
    private void MaybeStartBackgroundRebuild(IGraphicsContext context, Rect visibleLocalRect, Rect renderBounds)
    {
        var doc = Document;
        if (doc is null) return;

        if (!TryComputeRebuildRequest(context, visibleLocalRect, renderBounds, doc, out var request))
        {
            return; // Cache valid for this view — nothing to do.
        }

        if (_rebuildInProgress
            && _pendingDpiScale == request.EffectiveScale
            && _pendingLocalRect == request.VisibleLocalRect)
        {
            return; // Same rebuild already in flight; let it finish.
        }

        // Different request OR no rebuild active — queue one. (If a stale rebuild is
        // still running for an outdated zoom, we let it complete and discard its result
        // on commit — see CommitRebuild below.)
        if (_rebuildInProgress) return;
        _rebuildInProgress = true;
        _pendingDpiScale = request.EffectiveScale;
        _pendingLocalRect = request.VisibleLocalRect;
        _ = RebuildAsync(request);
    }

    /// <summary>Reproduces EnsureCachedBitmap's UI-thread phase: compute effectiveScale,
    /// inflate the visible rect, clamp pixel size — but DON'T touch the factory or build
    /// the bitmap. Returns false when the existing cache covers the request, true with
    /// the rebuild parameters otherwise.</summary>
    private bool TryComputeRebuildRequest(IGraphicsContext context, Rect visibleLocalRect, Rect renderBounds, SvgDocument doc, out RebuildRequest request)
    {
        request = default;
        double dpiScale = context.DpiScale > 0 ? context.DpiScale : 1.0;

        // Account for any ancestor transform (e.g. ZoomPanCanvas zoom). Decompose to per-axis
        // scale and take the larger axis as the effective resolution multiplier; without this,
        // the cached bitmap is rendered at 1× and scaled up by the on-screen transform → blurry.
        var transform = context.GetTransform();
        float scaleX = MathF.Sqrt(transform.M11 * transform.M11 + transform.M12 * transform.M12);
        float scaleY = MathF.Sqrt(transform.M21 * transform.M21 + transform.M22 * transform.M22);
        float transformScale = MathF.Max(scaleX, scaleY);
        if (!float.IsFinite(transformScale) || transformScale <= 0f)
        {
            transformScale = 1f;
        }

        // Quantize to power-of-2 steps so continuous zoom doesn't rebuild every frame; resolves
        // to next pow2 ≥ current zoom (over-resolving by ≤2× is fine; under-resolving blurs).
        double quantizedScale = transformScale <= 1f
            ? 1.0
            : Math.Min(16.0, Math.Pow(2, Math.Ceiling(Math.Log2(transformScale))));
        double effectiveScale = dpiScale * quantizedScale;

        // Pan reuse: if the existing cache covers the current visible area (and zoom hasn't
        // changed), keep using it. SvgView previously rebuilt on every visibleLocalRect
        // change → every pan invalidated the (expensive) full SVG render. Now: cache stays
        // valid as long as the new visible is within the previously cached bounds.
        if (CacheEnabled
            && _cachedImage is not null
            && ReferenceEquals(_cachedDocument, doc)
            && _cachedDpiScale == effectiveScale
            && _cachedLocalRect.Contains(visibleLocalRect))
        {
            return false; // Cache hit — current rendering already covers the visible.
        }

        // Inflate the cache region beyond the visible viewport so subsequent small pans
        // stay inside the cached extent (cache hit, no re-render). Half-viewport on each
        // side = 2× the visible area cached. Clamp inside renderBounds so we never request
        // pixels for SVG content that doesn't exist.
        var inflated = InflateAndClamp(visibleLocalRect, renderBounds, paddingFactor: 0.5);

        int pixelWidth = Math.Max(1, (int)Math.Ceiling(inflated.Width * effectiveScale));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(inflated.Height * effectiveScale));

        if (pixelWidth > MaxCachePixelExtent || pixelHeight > MaxCachePixelExtent)
        {
            double shrink = (double)MaxCachePixelExtent / Math.Max(pixelWidth, pixelHeight);
            pixelWidth = Math.Max(1, (int)(pixelWidth * shrink));
            pixelHeight = Math.Max(1, (int)(pixelHeight * shrink));
            // Shrink the inflated rect proportionally so the bitmap covers the same user-space
            // area at a slightly lower resolution. The visible portion still fits.
            double newWidth = pixelWidth / effectiveScale;
            double newHeight = pixelHeight / effectiveScale;
            inflated = ClampToRect(
                new Rect(inflated.X, inflated.Y, newWidth, newHeight),
                renderBounds);
            // Fallback if clamping pushed the new rect off the visible — use visible only.
            if (!inflated.Contains(visibleLocalRect))
            {
                inflated = visibleLocalRect;
                pixelWidth = Math.Max(1, (int)Math.Ceiling(inflated.Width * effectiveScale));
                pixelHeight = Math.Max(1, (int)Math.Ceiling(inflated.Height * effectiveScale));
            }
        }
        visibleLocalRect = inflated;

        if (CacheEnabled
            && _cachedImage is not null
            && ReferenceEquals(_cachedDocument, doc)
            && _cachedPixelWidth == pixelWidth
            && _cachedPixelHeight == pixelHeight
            && _cachedDpiScale == effectiveScale
            && _cachedLocalRect == visibleLocalRect)
        {
            return false; // Cache hit on exact match.
        }

        request = new RebuildRequest(doc, visibleLocalRect, renderBounds, effectiveScale, pixelWidth, pixelHeight);
        return true;
    }

    /// <summary>Worker-thread entry: builds the offscreen bitmap, then marshals back to
    /// the UI thread to swap fields and InvalidateVisual. Exceptions during build are
    /// swallowed (existing cache stays in place).</summary>
    private async Task RebuildAsync(RebuildRequest request)
    {
        IRenderSurface? newSurface = null;
        IImage? newImage = null;
        TimeSpan elapsed = TimeSpan.Zero;
        try
        {
            await Task.Run(() =>
            {
                var factory = Application.IsRunning
                    ? Application.Current.GraphicsFactory
                    : Application.DefaultGraphicsFactory;
                if (factory is null) return;

                // Backend-specific worker thread setup: GL backends activate a hidden-
                // window worker HGLRC whose namespace is share-listed with all window
                // contexts. D2D MULTI_THREADED / Metal / GDI return a no-op disposable.
                using var workerScope = factory.AcquireBackgroundRenderScope();

                var sw = Stopwatch.StartNew();
                // GPU-preferring offscreen target so SvgView's render pass lives on the
                // shared filter device. D2D MULTI_THREADED factory permits this from a
                // worker thread; GDI's HDC operations are HDC-local so concurrent
                // workers with their own targets don't collide either.
                var renderDevice = factory;
                var surface = renderDevice.CreateSurface(RenderSurfaceDescriptor.CachedImage(
                    request.PixelWidth,
                    request.PixelHeight,
                    request.EffectiveScale,
                    debugName: "SvgViewCache"));
                if (surface is not ICpuPixelSurface pixels)
                {
                    surface.Dispose();
                    throw new NotSupportedException($"{nameof(SvgView)} cache rebuild requires a CPU-writable render surface.");
                }
                try
                {
                    pixels.Clear(Color.Transparent);
                    using (var bmpContext = renderDevice.CreateContext(surface))
                    {
                        bmpContext.BeginFrame(surface);
                        try
                        {
                            bmpContext.Translate(-request.VisibleLocalRect.X, -request.VisibleLocalRect.Y);
                            bmpContext.IntersectClip(request.VisibleLocalRect);
                            request.Document.Render(bmpContext, request.RenderBounds);
                        }
                        finally
                        {
                            bmpContext.EndFrame();
                        }
                    }
                    newImage = renderDevice.CreateImageView(surface);
                    newSurface = surface;
                    surface = null!;
                }
                finally
                {
                    surface?.Dispose();
                }
                elapsed = sw.Elapsed;
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SvgView] RebuildAsync EXCEPTION: {ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            // Build failed — drop any partial state, leave existing cache untouched.
            newImage?.Dispose();
            newSurface?.Dispose();
            newImage = null;
            newSurface = null;
        }

        // Marshal to UI thread for the field swap. BeginInvoke (async) is fine —
        // OnRender will pick up the new fields on its next pass after InvalidateVisual.
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        Action commit = () => CommitRebuild(request, newSurface, newImage, elapsed);
        if (dispatcher != null && !dispatcher.IsOnUIThread)
        {
            dispatcher.BeginInvoke(commit);
        }
        else
        {
            commit();
        }
    }

    /// <summary>UI-thread commit: replaces cache fields with the worker-built bitmap.
    /// Disposes the previous bitmap. If the build was cancelled / failed (newSurface=null)
    /// just clears the in-progress flag without touching the cache.</summary>
    private void CommitRebuild(RebuildRequest request, IRenderSurface? newSurface, IImage? newImage, TimeSpan elapsed)
    {
        try
        {
            if (newSurface is null || newImage is null)
            {
                return;
            }

            // Discard previous cache, install the worker-built bitmap.
            ReleaseCache();
            _cachedImage = newImage;
            _cachedSurface = newSurface;
            _cachedDocument = request.Document;
            _cachedLocalRect = request.VisibleLocalRect;
            _cachedPixelWidth = request.PixelWidth;
            _cachedPixelHeight = request.PixelHeight;
            _cachedDpiScale = request.EffectiveScale;
            SetValue(LastDrawTimePropertyKey, elapsed);
        }
        finally
        {
            _rebuildInProgress = false;
            // Re-paint with the new cache. If the user has zoomed/panned during the
            // background build, the next OnRender will see params don't match and queue
            // another rebuild — that's the natural way to handle stale rebuilds.
            InvalidateVisual();
        }
    }

    private void ReleaseCache()
    {
        _cachedImage?.Dispose();
        _cachedImage = null;
        _cachedSurface?.Dispose();
        _cachedSurface = null;
        _cachedDocument = null;
        _cachedLocalRect = Rect.Empty;
        _cachedPixelWidth = 0;
        _cachedPixelHeight = 0;
        _cachedDpiScale = 0;
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        if (newRoot is null)
        {
            ReleaseCache();
        }
    }

    /// <summary>
    /// Inflates <paramref name="visibleRect"/> by <paramref name="paddingFactor"/> on each
    /// side and clamps the result inside <paramref name="container"/>. Used to give the
    /// SvgView's cached bitmap headroom for pan: small pans stay within the inflated cache
    /// without re-rendering.
    /// </summary>
    private static Rect InflateAndClamp(Rect visibleRect, Rect container, double paddingFactor)
    {
        double padX = visibleRect.Width * paddingFactor;
        double padY = visibleRect.Height * paddingFactor;
        var inflated = new Rect(
            visibleRect.X - padX,
            visibleRect.Y - padY,
            visibleRect.Width + 2 * padX,
            visibleRect.Height + 2 * padY);
        return ClampToRect(inflated, container);
    }

    private static Rect ClampToRect(Rect rect, Rect container)
    {
        double left = Math.Max(rect.X, container.X);
        double top = Math.Max(rect.Y, container.Y);
        double right = Math.Min(rect.X + rect.Width, container.X + container.Width);
        double bottom = Math.Min(rect.Y + rect.Height, container.Y + container.Height);
        if (right <= left || bottom <= top)
        {
            return Rect.Empty;
        }
        return new Rect(left, top, right - left, bottom - top);
    }

    private static Rect FitViewBox(Rect bounds, double viewBoxWidth, double viewBoxHeight)
    {
        double sourceWidth = Math.Max(1, viewBoxWidth);
        double sourceHeight = Math.Max(1, viewBoxHeight);
        double scale = Math.Min(bounds.Width / sourceWidth, bounds.Height / sourceHeight);
        if (scale <= 0 || double.IsNaN(scale) || double.IsInfinity(scale))
        {
            return Rect.Empty;
        }

        double width = sourceWidth * scale;
        double height = sourceHeight * scale;
        double x = bounds.X + (bounds.Width - width) * 0.5;
        double y = bounds.Y + (bounds.Height - height) * 0.5;
        return new Rect(x, y, width, height);
    }
}
