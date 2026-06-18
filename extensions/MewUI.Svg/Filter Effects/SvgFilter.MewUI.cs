using System.Numerics;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Filters;

namespace Svg.FilterEffects;

public partial class SvgFilter
{
    // Per-(filter, visual-element) result cache. Each entry owns a backend RT + IImage
    // that survive across frames; on hit we skip the entire pipeline and just DrawImage.
    //
    // The earlier "ReadPixels + CPU memcpy into DIB cache RT" version blew memory under
    // zoom because every cache miss allocated a fresh managed byte[] for GPU readback,
    // creating GC pressure as the working set grew. This version uses GPU→GPU copy via
    // IGraphicsContext.DrawImage(result.AsImage()) into a GPU-backed cache RT — no CPU
    // readback, no managed buffer allocation, just a single GPU draw op per snapshot.
    private const bool CacheEnabled = true;

    // Toggle for the [SvgFilter*] / [SvgFilterCache] / [SvgFilterTime] diagnostic spam.
    private readonly Dictionary<SvgVisualElement, FilterCacheEntry> _resultCache = new();
    // Guards _resultCache and _pendingDisposal — same SvgFilter instance is shared by the
    // SVG document and accessed concurrently from UI and worker render threads (SvgView
    // background cache build). Without a lock, Dictionary mutation races with TryGetValue,
    // and entry disposal can race with another thread's DrawFilterResult recording.
    private readonly object _cacheLock = new();
    // Deferred-disposal queue for cache entries replaced or evicted mid-render. Modeled on
    // the MewVG per-NVG image disposal queue: D2D's DrawImage records a reference to the
    // IImage that the device context retains internally until EndDraw. Disposing the entry
    // mid-frame (cache cap flush, replacement on miss) frees its underlying ID2D1Bitmap
    // wrapper while the recorded DrawImage call still expects to commit it — visible as
    // specific filtered elements going invisible at certain zooms (the missing element is
    // whichever's cache entry happened to be evicted between its DrawFilterResult and the
    // outer EndDraw). Drain at the start of the next ApplyFilter call: by that point the
    // previous render's EndDraw has flushed and the entries are safe to release.
    private readonly List<FilterCacheEntry> _pendingDisposal = new();

    private sealed class FilterCacheEntry : IDisposable
    {
        // Cache key. Uses POST-CAP effective scale (executor MaxInputScale + maxOffscreenExtent
        // shrink) rather than the raw request scale. Two requests at different zoom levels that
        // both clamp to the same effective resolution produce bit-identical output and must hit
        // the same cache entry.
        public double EffectiveScaleX, EffectiveScaleY;
        public double FilterRegionWidth, FilterRegionHeight;
        public IRenderSurface? Surface;
        public IImage? OutputImage;
        public int OutputPixelWidth, OutputPixelHeight;
        public int SourcePixelWidth, SourcePixelHeight;
        public Rect ResultBounds;

        public bool Matches(double effectiveScaleX, double effectiveScaleY, double regionWidth, double regionHeight)
        {
            const double eps = 0.5;
            return Math.Abs(EffectiveScaleX - effectiveScaleX) < 0.01
                && Math.Abs(EffectiveScaleY - effectiveScaleY) < 0.01
                && Math.Abs(FilterRegionWidth - regionWidth) < eps
                && Math.Abs(FilterRegionHeight - regionHeight) < eps
                && OutputImage is not null
                && Surface is not null;
        }

        public void Dispose()
        {
            OutputImage?.Dispose();
            Surface?.Dispose();
            OutputImage = null;
            Surface = null;
        }
    }

    protected override void Render(ISvgRenderer renderer)
    {
        RenderChildren(renderer);
    }

    public void ApplyFilter(SvgVisualElement element, ISvgRenderer renderer, Action<ISvgRenderer> renderMethod)
    {
        // TEMP DIAGNOSTIC: bypass the entire filter pipeline (no source layer alloc, no
        // offscreen ctx, no scratch surface, no filter graph eval) and just render the element
        // straight into the outer renderer. If the cross-frame corruption disappears with
        // this on, the bug lives somewhere in the filter pipeline (texture wrap lifetime,
        // pool reuse, NVG-state interleave) — not in generic SVG concurrent rendering.
        if (Environment.GetEnvironmentVariable("MEWUI_SVG_DISABLE_FILTER") == "1")
        {
            renderMethod(renderer);
            return;
        }

        // Drain prior frames' deferred cache evictions before starting this filter pass.
        // Anything queued during a previous render has had its renderer's EndDraw flushed
        // by now, so its underlying D2D bitmap is no longer held by any command list and
        // can safely be released. This is the MewVG-equivalent "drain on safe boundary".
        DrainPendingCacheDisposal();

        var bounds = GetElementBounds(element, renderer);
        if (bounds.IsEmpty || bounds.Width <= 0d || bounds.Height <= 0d)
        {
            return;
        }

        var filterRegion = GetFilterRegion(bounds, renderer, element);
        if (filterRegion.IsEmpty || filterRegion.Width <= 0d || filterRegion.Height <= 0d)
        {
            return;
        }

        var currentTransform = renderer.GraphicsContext.GetTransform();
        var scaleX = Math.Max(1e-6d, Math.Sqrt((currentTransform.M11 * currentTransform.M11) + (currentTransform.M12 * currentTransform.M12)));
        var scaleY = Math.Max(1e-6d, Math.Sqrt((currentTransform.M21 * currentTransform.M21) + (currentTransform.M22 * currentTransform.M22)));
        var dpiScale = Math.Max(1d, renderer.GraphicsContext.DpiScale);

        // Element's own transform scale. SVG primitiveUnits="userSpaceOnUse" (the default)
        // is interpreted by browsers as units in the user space *after* the filtered
        // element's transforms are applied — so for a heavily-shrunk element like
        // <g transform="matrix(0.069,…)"> the σ=14 in feGaussianBlur means 14 *local*
        // units which display as 14 × 0.069 ≈ 1 parent-unit (visually ~1px blur). Using
        // ancestor-only scale here treats σ=14 as 14 parent-units and produces the
        // characteristic "thick black band" where browsers render almost no shadow
        // (e.g. issue-084-02 beaker filter5994). Multiply ancestor scale by the
        // element's self-transform scale so per-primitive σ/dx/dy convert to source-
        // layer pixels at the same rate the geometry shrinks.
        double selfScaleX = 1, selfScaleY = 1;
        if (element.Transforms is { Count: > 0 } selfXforms)
        {
            var selfMatrix = selfXforms.GetMatrix();
            double sx = Math.Sqrt(selfMatrix.M11 * selfMatrix.M11 + selfMatrix.M12 * selfMatrix.M12);
            double sy = Math.Sqrt(selfMatrix.M21 * selfMatrix.M21 + selfMatrix.M22 * selfMatrix.M22);
            if (sx > 0) selfScaleX = sx;
            if (sy > 0) selfScaleY = sy;
        }

        // Find max σ across this filter's primitives — needed both for halo region
        // inflation (so the SVG-specified region isn't tighter than 3σ) AND for the
        // visibility-clip below (so the halo can fade naturally past the visible cull).
        // σ is scaled by selfScale so the inflation is in the same parent-space units
        // filterRegion uses.
        double maxSigma = 0;
        foreach (var primChild in Children.OfType<SvgFilterPrimitive>())
        {
            if (primChild is SvgGaussianBlur gb)
            {
                double sx = gb.StdDeviation.Count > 0 ? gb.StdDeviation[0] : 0;
                double sy = gb.StdDeviation.Count > 1 ? gb.StdDeviation[1] : sx;
                if (sx > maxSigma) maxSigma = sx;
                if (sy > maxSigma) maxSigma = sy;
            }
        }
        double sigmaPad = maxSigma * 3.0 * Math.Max(selfScaleX, selfScaleY);

        // σ-aware region inflation. SVG spec clips filter output at the filter region,
        // so a region narrower than 3σ produces a visibly hard rectangular boundary where
        // the blur halo cuts off (e.g. path3716 + filter4264 with σ=27.77 user units but
        // only ~10% bbox padding). Browsers extend the working region by ~3σ so the halo
        // fades naturally past the author's region — match that here.
        if (sigmaPad > 0)
        {
            filterRegion = new Rect(
                filterRegion.X - sigmaPad,
                filterRegion.Y - sigmaPad,
                filterRegion.Width + 2 * sigmaPad,
                filterRegion.Height + 2 * sigmaPad);
        }

        // Clamp filter region to element-visibility-bounds. Outside (element bounds + 3σ
        // halo), the filter has zero contribution — the element doesn't render there and
        // the blur halo can't extend past 3σ from any rendered pixel. SVG-spec filter
        // regions wildly exceeding this (e.g. unitless `width="200"` in objectBoundingBox
        // = 200× bbox = 18000 user-units for a 90-unit element) waste source bitmap on
        // empty space — multi-hundred-MB allocations for nothing visible. Clamping
        // matches author intent and keeps memory in check.
        var elementHaloBounds = sigmaPad > 0
            ? new Rect(bounds.X - sigmaPad, bounds.Y - sigmaPad,
                       bounds.Width + 2 * sigmaPad, bounds.Height + 2 * sigmaPad)
            : bounds;
        var visibilityClip = elementHaloBounds;

        // If the active context exposes a finite cull (visible viewport in user-space),
        // tighten the visibility clip further — at high zoom only a sub-rect of the
        // element + halo may actually be on-screen.
        var clipLocal = renderer.GraphicsContext.GetClipBoundsLocal();
        if (clipLocal is { Width: > 0, Height: > 0 } cl)
        {
            var paddedClip = sigmaPad > 0
                ? new Rect(cl.X - sigmaPad, cl.Y - sigmaPad,
                           cl.Width + 2 * sigmaPad, cl.Height + 2 * sigmaPad)
                : cl;
            visibilityClip = visibilityClip.Intersect(paddedClip);
        }

        var clipped = filterRegion.Intersect(visibilityClip);
        // Guarantee the element bounds (without halo) are always inside the surface — when
        // GetElementBounds reports the union of children, those children must be drawable.
        // Without this, an element's own contents can fall outside `clipped` (which is a
        // visibility approximation, not a strict bounding box) and get cropped on the source
        // raster, producing partially-rendered text/paths inside the filter result.
        if (!clipped.IsEmpty && !bounds.IsEmpty)
        {
            double left = Math.Min(clipped.X, bounds.X);
            double top = Math.Min(clipped.Y, bounds.Y);
            double right = Math.Max(clipped.Right, bounds.Right);
            double bottom = Math.Max(clipped.Bottom, bounds.Bottom);
            clipped = new Rect(left, top, right - left, bottom - top);
        }
        if (clipped.IsEmpty || clipped.Width <= 0 || clipped.Height <= 0)
        {
            return; // The element + halo is fully off-screen / outside the SVG-specified region.
        }
        // Don't shrink filterRegion to `clipped` — the SVG.NET reference uses a simple
        // bounds × 1.5 inflate as the source rasterization rectangle; cropping it down to
        // the visibility intersection truncates content the moment the visible viewport
        // doesn't cover the full element + halo. Keep the SVG-spec filterRegion as-is so
        // the source layer always covers all element children.

        // Compute the effective rendering scale and source pixel dimensions BEFORE the cache
        // lookup. Two requests with different raw scales can clamp to the same effective scale
        // (executor MaxInputScale cap, then maxOffscreenExtent shrink). Keying the cache by
        // post-cap values lets those collide on the same entry — without this, CPU executor
        // calls (MaxInputScale=1.0) miss the cache on every zoom step despite the rasterized
        // output being identical.
        var offscreenFactory = renderer.GraphicsFactory;
        var executor = offscreenFactory.CreateImageFilterExecutor();
        double maxScale = executor.MaxInputScale;
        double effectiveLogicalToPixelX = Math.Min(scaleX * dpiScale, maxScale);
        double effectiveLogicalToPixelY = Math.Min(scaleY * dpiScale, maxScale);

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(filterRegion.Width * effectiveLogicalToPixelX));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(filterRegion.Height * effectiveLogicalToPixelY));

        const int maxOffscreenExtent = 4096;
        if (pixelWidth > maxOffscreenExtent || pixelHeight > maxOffscreenExtent)
        {
            double shrink = (double)maxOffscreenExtent / Math.Max(pixelWidth, pixelHeight);
            pixelWidth = Math.Max(1, (int)(pixelWidth * shrink));
            pixelHeight = Math.Max(1, (int)(pixelHeight * shrink));
            effectiveLogicalToPixelX *= shrink;
            effectiveLogicalToPixelY *= shrink;
        }

        // Cache lookup. For static SVG documents under pan/zoom, the filter output is
        // invariant given (effective scale, post-clip filterRegion size). On hit we skip
        // the entire source rasterization + filter execution path and just blit the cached
        // image to the new on-canvas position.
        if (CacheEnabled)
        {
            FilterCacheEntry? hit = null;
            lock (_cacheLock)
            {
                if (_resultCache.TryGetValue(element, out var cached) &&
                    cached.Matches(effectiveLogicalToPixelX, effectiveLogicalToPixelY, filterRegion.Width, filterRegion.Height))
                {
                    hit = cached;
                }
            }
            if (hit is not null)
            {
                // Draw outside the lock — DrawFilterResult records into the renderer's
                // device context; doing it under _cacheLock would serialize all SvgFilter
                // ApplyFilter calls across threads. The local 'hit' reference keeps the
                // entry observable until DrawFilterResult returns; eviction by another
                // thread between the read and the draw would queue the entry into
                // _pendingDisposal but not dispose it until next ApplyFilter call.
                DrawFilterResult(renderer, hit.OutputImage!,
                    GetResultDestination(filterRegion, hit.ResultBounds, hit.SourcePixelWidth, hit.SourcePixelHeight),
                    new Rect(0, 0, hit.OutputPixelWidth, hit.OutputPixelHeight));
                return;
            }
        }

        // Source layer rendering: 1 user unit → effectiveLogicalToPixel pixels. Set the
        // bitmap's reported DpiScale to the same value so internally-DPI-aware rendering
        // (e.g. backend stroke snapping) sees a consistent device-pixel-per-DIP ratio.
        var renderDevice = offscreenFactory;
        using var sourceSurface = renderDevice.CreateSurface(RenderSurfaceDescriptor.FilterIntermediate(
            pixelWidth,
            pixelHeight,
            Math.Max(1.0, Math.Min(effectiveLogicalToPixelX, effectiveLogicalToPixelY)),
            debugName: "SvgFilterSourceLayer"));
        if (sourceSurface is not ICpuPixelSurface sourcePixels)
        {
            throw new NotSupportedException($"{nameof(SvgFilter)} currently requires CPU-writable filter source layers.");
        }
        sourcePixels.Clear(Aprillz.MewUI.Color.Transparent);

        // 1. Render the element into the source layer.
        using (var context = offscreenFactory.CreateContext(sourceSurface))
        {
            context.BeginFrame(sourceSurface);
            try
            {
                using var offscreenRenderer = new MewSvgRenderer(offscreenFactory, context);
                offscreenRenderer.SetBoundable(renderer.GetBoundable());
                // SetTransform's scale × context.DpiScale = total logical-to-pixel ratio.
                // We want that to equal effectiveLogicalToPixel, so divide by the bitmap's
                // DpiScale (which we set to min(effectiveX, effectiveY) above).
                double sourceDpi = context.DpiScale;
                context.SetTransform(
                    Matrix3x2.CreateTranslation((float)-filterRegion.X, (float)-filterRegion.Y) *
                    Matrix3x2.CreateScale((float)(effectiveLogicalToPixelX / sourceDpi), (float)(effectiveLogicalToPixelY / sourceDpi)));
                renderMethod(offscreenRenderer);
            }
            finally
            {
                context.EndFrame();
            }
        }

        // 2. Build the filter DAG from this filter element's primitive children, then evaluate.
        // The graph builder honors SVG `in` / `result` chaining (so feMerge, feComposite, etc.
        // produce correct output where the legacy per-primitive ApplyInPlace path could not).
        var primitives = Children.OfType<SvgFilterPrimitive>().ToList();
        ImageFilter? graph = SvgFilterGraphBuilder.Build(primitives, renderer);

        IImage outputImage;
        Rect outputSourceRect;

        if (graph is null)
        {
            // No primitives — draw the source layer as-is.
            outputImage = renderDevice.CreateImageView(sourceSurface);
            outputSourceRect = new Rect(0, 0, sourceSurface.PixelWidth, sourceSurface.PixelHeight);
            try
            {
                DrawFilterResult(renderer, outputImage, filterRegion, outputSourceRect);
            }
            finally { outputImage.Dispose(); }
            return;
        }

        // Factory returns the backend's best executor (GPU shader path on OpenGL, CPU on
        // backends without a dedicated implementation). Each GPU executor internally chains
        // the CPU executor as fallback for nodes it doesn't accelerate.
        using var sourceImage = renderDevice.CreateImageView(sourceSurface);
        // The source layer was rasterized at filterRegion × effectiveLogicalToPixel pixels
        // (clamped by the executor's MaxInputScale hint above). Filter parameters in user/DIP
        // units must be multiplied by the same scale to land in source-pixel space.
        // Self-transform scale is folded in here (only) so per-primitive σ/dx/dy convert
        // to source-pixel units at the same rate the geometry was shrunk by the element's
        // own transform — matches browser behavior on heavily-scaled filtered elements.
        // The source-layer pixelWidth/Height above intentionally exclude selfScale: the
        // bitmap covers filterRegion (in parent units) at the parent display rate, so the
        // shrunk geometry rasterizes correctly inside it via renderMethod's PushTransforms.
        using var ctx = new DefaultFilterContext(
            sourceSurface,
            sourceImage,
            sourceBounds: new Rect(0, 0, sourceSurface.PixelWidth, sourceSurface.PixelHeight),
            offscreenFactory,
            logicalToPixelScaleX: effectiveLogicalToPixelX * selfScaleX,
            logicalToPixelScaleY: effectiveLogicalToPixelY * selfScaleY);

        using var result = executor.Execute(graph, ctx);

        DrawFilterResult(renderer, result.AsImage(),
            GetResultDestination(filterRegion, result.Bounds, sourceSurface.PixelWidth, sourceSurface.PixelHeight),
            new Rect(0, 0, result.PixelWidth, result.PixelHeight));

        // Snapshot the result into a cache-owned RT so subsequent frames at the same
        // (scale, region) hit the early DrawImage path. The result FilterResult holds a
        // pooled scratch surface that's about to be released; we copy its pixels (and create
        // a fresh IImage backed by our own RT) so the cached entry remains valid after
        // result.Dispose returns the scratch.
        if (CacheEnabled)
        {
            TrySnapshotIntoCache(element, offscreenFactory, result,
                effectiveLogicalToPixelX, effectiveLogicalToPixelY,
                filterRegion.Width, filterRegion.Height,
                sourceSurface.PixelWidth, sourceSurface.PixelHeight);
        }
    }

    /// <summary>Draws the filter output onto the main render context using the fast
    /// hardware stretch path (NEAREST / GDI COLORONCOLOR / D2D NEAREST_NEIGHBOR). Filter
    /// outputs are blur/composite results — low-frequency content where the linear/cubic
    /// resamplers' extra quality is invisible but their cost (per-frame cached scaled
    /// bitmaps on GDI, mip generation on D2D HighQuality) is significant.</summary>
    private static void DrawFilterResult(ISvgRenderer renderer, IImage image, Rect destRect, Rect sourceRect)
    {
        var ctx = renderer.GraphicsContext;
        var prevQuality = ctx.ImageScaleQuality;
        ctx.ImageScaleQuality = ImageScaleQuality.Fast;
        try
        {
            renderer.DrawImage(image, destRect, sourceRect);
        }
        finally
        {
            ctx.ImageScaleQuality = prevQuality;
        }
    }

    private static Rect GetResultDestination(Rect filterRegion, Rect resultBounds, int sourcePixelWidth, int sourcePixelHeight)
    {
        var logicalPerPixelX = filterRegion.Width / Math.Max(1, sourcePixelWidth);
        var logicalPerPixelY = filterRegion.Height / Math.Max(1, sourcePixelHeight);
        return new Rect(
            filterRegion.X + resultBounds.X * logicalPerPixelX,
            filterRegion.Y + resultBounds.Y * logicalPerPixelY,
            resultBounds.Width * logicalPerPixelX,
            resultBounds.Height * logicalPerPixelY);
    }

    // Hard caps so the cache can't grow unbounded under bad workloads. Earlier values
    // (256 × 1M = 1GB ceiling) blew working set on GDI/CPU paths where the cache holds
    // pinned DIB sections. Tightened: 64 × 256K = 64MB ceiling, comfortable for typical
    // 70-element SVGs and bounds the worst case.
    private const int MaxCacheEntries = 16;
    private const int MaxCacheEntryPixelArea = 32 * 1024 * 1024; // 256K pixels = ~1 MB per entry

    private void TrySnapshotIntoCache(
        SvgVisualElement element, IGraphicsFactory factory, FilterResult result,
        double effectiveScaleX, double effectiveScaleY,
        double regionWidth, double regionHeight,
        int sourcePixelWidth, int sourcePixelHeight)
    {
        int pw = result.PixelWidth;
        int ph = result.PixelHeight;
        if (pw <= 0 || ph <= 0) return;

        // Skip cache for huge outputs — caching a 4096×4096 result costs 64 MB; under
        // continuous-zoom miss patterns that turns into hundreds of MB across elements.
        // Big filters re-render full-pipeline; the cache helps small/medium elements
        // where the win is largest (per-call setup overhead dominates).
        if ((long)pw * ph > MaxCacheEntryPixelArea) return;

        // Zero-copy: detach the result's underlying scratch surface (and matching IImage) and
        // hand them to the cache. The pool release is suppressed inside Detach so the RT
        // stays alive across frames; cache eviction disposes the RT explicitly. Pixels are
        // not copied — cache hits re-use the exact buffer the filter wrote into. Only
        // ScratchFilterResult supports detach (it's the only shape that owns a fresh RT).
        if (result is not ScratchFilterResult scratch)
        {
            return;
        }
        var detached = scratch.Detach();
        if (detached is null)
        {
            return;
        }
        var (surface, image) = detached.Value;

        var entry = new FilterCacheEntry
        {
            EffectiveScaleX = effectiveScaleX,
            EffectiveScaleY = effectiveScaleY,
            FilterRegionWidth = regionWidth,
            FilterRegionHeight = regionHeight,
            Surface = surface,
            OutputImage = image,
            OutputPixelWidth = pw,
            OutputPixelHeight = ph,
            SourcePixelWidth = sourcePixelWidth,
            SourcePixelHeight = sourcePixelHeight,
            ResultBounds = result.Bounds,
        };

        lock (_cacheLock)
        {
            // If the dict is at cap, queue everything for deferred disposal — DO NOT
            // dispose mid-render because the current renderer's BeginDraw...EndDraw
            // bracket may still hold recorded DrawImage references to the entries that
            // earlier cache HITs in this same frame issued. Their underlying ID2D1Bitmap
            // wrappers must survive until that EndDraw flushes. Drain at the next
            // ApplyFilter call (DrainPendingCacheDisposal) — by then the prior render's
            // EndDraw has fired.
            if (_resultCache.Count >= MaxCacheEntries)
            {
                foreach (var existing in _resultCache.Values)
                {
                    _pendingDisposal.Add(existing);
                }
                _resultCache.Clear();
            }

            // Same rationale for replacement: queue the previous entry for the same
            // element rather than disposing immediately. The previous entry may have been
            // returned by a cache HIT earlier in this same frame, with its DrawImage
            // pending in the renderer's command list.
            if (_resultCache.TryGetValue(element, out var prev))
            {
                _pendingDisposal.Add(prev);
            }

            _resultCache[element] = entry;
        }
    }

    /// <summary>
    /// Releases cache entries queued during a previous render. Called at the start of
    /// every <see cref="ApplyFilter"/> — by that point any prior renderer's EndDraw has
    /// flushed, so the entries' ID2D1Bitmap wrappers are no longer held by any D2D
    /// command list and can safely be released.
    /// </summary>
    private void DrainPendingCacheDisposal()
    {
        FilterCacheEntry[]? toDispose = null;
        lock (_cacheLock)
        {
            if (_pendingDisposal.Count > 0)
            {
                toDispose = _pendingDisposal.ToArray();
                _pendingDisposal.Clear();
            }
        }
        if (toDispose is null) return;
        foreach (var entry in toDispose)
        {
            entry.Dispose();
        }
    }

    private static Rect GetElementBounds(SvgVisualElement element, ISvgRenderer renderer)
    {
        // Bounds (Group/Text/etc.) already applies the element's own Transforms via
        // TransformedBounds, yielding the parent-space AABB the filter pipeline needs:
        // ApplyFilter is invoked before PushTransforms, so the source layer is rasterized
        // with the element's transform applied inside renderMethod, and filterRegion must
        // be in that same parent space. The earlier SvgGroup-only branch returned
        // Path(renderer).GetBounds() which is in element-local space (GetPaths does not
        // bake child transforms either) — that mismatch let glyphs fall outside the
        // source bitmap on the right/bottom (e.g. issue-084-02 "CHEMISTRY" → "CHEMISTR").
        return element.Bounds;
    }

    private Rect GetFilterRegion(Rect bounds, ISvgRenderer renderer, SvgVisualElement element)
    {
        // Match SVG.NET's Drawing.cs path: ignore the SVG-spec filter x/y/width/height
        // entirely and just inflate the element bounds by 50% on each side. The spec-
        // derived region exposes ambiguity in unitless attributes (`width="200"` could
        // mean 200% or 200× bbox depending on reader convention), and capping a 200×
        // region at maxOffscreenExtent shrinks the source raster to a tile, blurring
        // everything to mush. The 50%-inflate is what the GDI+ backend has shipped
        // with for years, and matches well-formed PNG references for filters with
        // typical sigma values.
        const double inflateFactor = 0.5;
        return new Rect(
            bounds.X - bounds.Width * inflateFactor,
            bounds.Y - bounds.Height * inflateFactor,
            bounds.Width * (1 + 2 * inflateFactor),
            bounds.Height * (1 + 2 * inflateFactor));
    }

    private static double BboxFraction(SvgUnit unit) =>
        unit.Type == SvgUnitType.Percentage ? unit.Value / 100d : unit.Value;
}
