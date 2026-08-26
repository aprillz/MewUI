using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public abstract partial class UIElement
{
    /// <summary>
    /// Render-cache policy for this element. When set to a <see cref="BitmapCache"/>, the element's
    /// rendered output is captured into an offscreen bitmap and blitted each frame until its content,
    /// size, or DPI changes; the visual tree stays live (layout/hit-test/focus unaffected).
    /// Default <see langword="null"/> = normal live rendering.
    /// </summary>
    public static readonly MewProperty<CacheMode?> CacheModeProperty =
        MewProperty<CacheMode?>.Register<UIElement>(nameof(CacheMode), null,
            MewPropertyOptions.AffectsRender,
            static (self, oldValue, newValue) =>
            {
                self._hasBitmapCache = newValue is BitmapCache;
                // The previous cache (if any) was built for the old policy; drop it so the next
                // render rebuilds under the new one.
                self.DisposeCacheEntry();
            });

    public CacheMode? CacheMode
    {
        get => GetValue(CacheModeProperty);
        set => SetValue(CacheModeProperty, value);
    }

    // Mirror of (CacheMode is BitmapCache) so the hot InvalidateVisual / Render paths avoid a
    // property-store lookup on every call.
    private bool _hasBitmapCache;

    // Monotonic content version, bumped whenever this element invalidates its visual (directly or
    // via a descendant bubbling up through here). The cache stores the version it captured; a
    // mismatch triggers a re-snapshot. Using a version instead of a bool avoids losing an
    // invalidation that arrives while the snapshot itself is rendering.
    private long _contentVersion;

    private CacheEntry? _cache;
    private static long s_nextBitmapCacheOwnerId;
    private readonly string _bitmapCacheScope =
        $"BitmapCache:{Interlocked.Increment(ref s_nextBitmapCacheOwnerId)}";

    // What the last render pass observed: true while this element sat outside the cull viewport, and
    // false again once it is actually drawn. A repaint asked for from here reaches no pixels, so it
    // does not need to wake the window.
    private bool _culledSinceLastRender;

    // While > 0 on the current thread, the viewport-bounds cull in Render is bypassed: a cache
    // snapshot renders the whole subtree into an offscreen surface, so culling against the window
    // client rect would wrongly drop parts that fall outside the visible viewport.
    [ThreadStatic]
    private static int _cacheSnapshotDepth;

    internal static bool IsRenderingToCache => _cacheSnapshotDepth > 0;

    private void MarkSubtreeCulled()
    {
        if (_culledSinceLastRender)
        {
            return;
        }

        VisualTree.Visit(this, static element =>
        {
            if (element is UIElement uiElement)
            {
                uiElement.DisposeCacheEntry();
                uiElement._culledSinceLastRender = true;
            }
        });
    }

    private void MarkRendered()
    {
        _culledSinceLastRender = false;
    }

    /// <summary>
    /// Marks this element as not reaching the screen while it is still drawn into an ancestor's cache.
    /// Its own cache is left alone: the snapshot in progress is writing through it.
    /// </summary>
    private void MarkCulledWhileCaching()
    {
        _culledSinceLastRender = true;
    }

    /// <summary>
    /// Ages the cached bitmaps above this element without waking the window. A repaint that is dropped
    /// still happened: an ancestor holding a snapshot of it must re-take that snapshot before showing it
    /// again, which the version mismatch makes it do the next time it renders.
    /// </summary>
    private void StaleCachedAncestors()
    {
        for (Element? element = Parent; element != null; element = element.Parent)
        {
            if (element is UIElement uiElement && uiElement._hasBitmapCache)
            {
                uiElement._contentVersion++;
            }
        }
    }

    /// <summary>
    /// Renders this element (and its subtree) when it is not part of any window's visual tree, e.g. a
    /// detached drag-preview element drawn into another surface. Bypasses the viewport cull, which would
    /// otherwise drop the whole subtree because it has no Window root.
    /// </summary>
    internal void RenderDetached(IGraphicsContext context)
    {
        _cacheSnapshotDepth++;
        try
        {
            Render(context);
        }
        finally
        {
            _cacheSnapshotDepth--;
        }
    }

    public override void InvalidateVisual()
    {
        // The version is bumped even when the walk stops here, so a cache rebuilt after this element
        // comes back into view carries the content this repaint asked for.
        if (_hasBitmapCache)
        {
            _contentVersion++;
        }

        // A repaint from an element the last render pass culled reaches no pixels, so it must not wake
        // the window. SkipViewportCull is read live: an element that set it after being marked is
        // drawn again and has to be able to ask for it.
        if (_culledSinceLastRender && !SkipViewportCull && !IsRenderingToCache)
        {
            StaleCachedAncestors();
            return;
        }

        base.InvalidateVisual();
    }

    /// <summary>
    /// Renders this element by serving its cached bitmap, (re)building the cache first if missing
    /// or stale. Falls back to live rendering when the cache cannot be produced (e.g. zero size).
    /// </summary>
    private void RenderCached(IGraphicsContext context)
    {
        var window = FindVisualRoot() as Window;
        var factory = window?.GraphicsFactory ?? Application.DefaultGraphicsFactory;
        int deviceGeneration = window?.DeviceGeneration ?? 0;
        var bitmapCache = (BitmapCache)CacheMode!;

        bool cacheRebuilt = EnsureCache(factory, context.DpiScale, deviceGeneration, bitmapCache);

        if (_cache is { } entry)
        {
            if (cacheRebuilt && window != null)
            {
                entry.InvalidationOverlayColor = window.NextBitmapCacheInvalidationOverlayColor();
            }
            context.DrawImage(entry.Image, Bounds, new Rect(0, 0, entry.PixelWidth, entry.PixelHeight));
            if (!IsRenderingToCache &&
                window?.DevToolsBitmapCacheInvalidationOverlayEnabled == true)
            {
                context.FillRectangle(Bounds, entry.InvalidationOverlayColor);
            }
        }
        else
        {
            OnRender(context);
            RenderSubtree(context);
        }
    }

    /// <summary>
    /// The colour an opaque cache can be filled with, or <see langword="null"/> to cache with alpha.
    /// </summary>
    private Color? ResolveOpaqueCacheFill()
    {
        // Backends can only keep subpixel text antialiasing on a surface with no per-pixel alpha,
        // because subpixel coverage cannot be resolved against unknown backdrop pixels. The nearest
        // opaque background in the ancestry is what the cache would have been composited onto, so
        // priming it with that colour leaves the same pixels and keeps the text subpixel-rendered.
        var bounds = Bounds;

        for (Element? element = this; element != null; element = element.Parent)
        {
            if (element is Controls.Control control && control.Background.A == 255)
            {
                // This element's own background only covers the whole box when it is not rounded; a
                // rounded one leaves its corners to whatever is behind, which an ancestor colour supplies.
                if (!ReferenceEquals(element, this) || control.CornerRadius <= 0)
                {
                    return control.Background;
                }
            }

            // A sibling overlapping this element paints between it and any ancestor background, so that
            // background is not what the cache composites onto: filling with it would hide the sibling.
            if (element.Parent is IVisualTreeHost host && HasOverlappingSibling(host, element, bounds))
            {
                return null;
            }
        }

        return null;
    }

    // Scratch for the sibling-overlap visitor; the visual tree is walked only on the UI thread.
    private static Element? _overlapChild;
    private static Rect _overlapBounds;
    private static bool _overlapFound;

    // Single static delegate - no per-call closure allocation on the render path.
    private static readonly Func<Element, bool> _overlapVisitor = static sibling =>
    {
        if (ReferenceEquals(sibling, _overlapChild)
            || sibling is not UIElement uiElement
            || !uiElement.Bounds.IntersectsWith(_overlapBounds))
        {
            return true;
        }

        _overlapFound = true;
        return false;
    };

    private static bool HasOverlappingSibling(IVisualTreeHost parent, Element child, Rect bounds)
    {
        _overlapChild = child;
        _overlapBounds = bounds;
        _overlapFound = false;

        parent.VisitChildren(_overlapVisitor);

        _overlapChild = null;
        return _overlapFound;
    }

    private bool EnsureCache(IGraphicsFactory factory, double dpiScale, int deviceGeneration, BitmapCache bitmapCache)
    {
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            DisposeCacheEntry();
            return false;
        }

        double effectiveDpiScale = dpiScale * Math.Max(0.01, bitmapCache.RenderAtScale);
        int pixelWidth = Math.Max(1, (int)Math.Ceiling(bounds.Width * effectiveDpiScale));
        int pixelHeight = Math.Max(1, (int)Math.Ceiling(bounds.Height * effectiveDpiScale));
        long version = _contentVersion;
        Color? opaqueFill = ResolveOpaqueCacheFill();

        var entry = _cache;
        bool canReuse = entry != null
            && entry.PixelWidth == pixelWidth
            && entry.PixelHeight == pixelHeight
            && entry.DpiScale == effectiveDpiScale
            && entry.DeviceGeneration == deviceGeneration
            && entry.OpaqueFill == opaqueFill
            && (entry.PersistentLease == null || entry.Version == version);

        if (canReuse && entry!.Version == version)
        {
            return false;
        }

        if (!canReuse)
        {
            DisposeCacheEntry();

            var cacheKey = new RenderCacheKey(
                RenderCacheEntryKind.ViewportSnapshot,
                pixelWidth,
                pixelHeight,
                effectiveDpiScale,
                opaqueFill is null ? RenderPixelFormat.Bgra8888Premultiplied : RenderPixelFormat.Bgra8888,
                unchecked((ulong)version),
                DeviceId: 0,
                Scope: _bitmapCacheScope).ForDevice(factory);
            if (factory.ResourceCache?.TryGet(cacheKey, out var cached) == true)
            {
                entry = new CacheEntry
                {
                    Surface = cached.Surface,
                    Image = cached.Image,
                    PersistentLease = cached,
                    PersistentKey = cacheKey,
                    PixelWidth = pixelWidth,
                    PixelHeight = pixelHeight,
                    DpiScale = effectiveDpiScale,
                    DeviceGeneration = deviceGeneration,
                    OpaqueFill = opaqueFill,
                    AccountedBytes = RenderResourceMetrics.ScratchBytes(cached.Surface.PixelWidth, cached.Surface.PixelHeight),
                    Version = version,
                };
                _cache = entry;
                RenderResourceMetrics.BitmapCacheEntryAdded(entry.AccountedBytes);
                return false;
            }

            // Pool-sized (approx-fitted) surface; PixelWidth/Height keep the painted content size
            // and the blit reads just that region.
            IRenderSurface surface = factory.AcquireScratchSurface(
                pixelWidth, pixelHeight, effectiveDpiScale, hasAlpha: opaqueFill is null, debugName: "BitmapCache");
            try
            {
                entry = new CacheEntry
                {
                    Surface = surface,
                    Image = factory.CreateImageView(surface),
                    PersistentKey = cacheKey,
                    PixelWidth = pixelWidth,
                    PixelHeight = pixelHeight,
                    DpiScale = effectiveDpiScale,
                    DeviceGeneration = deviceGeneration,
                    OpaqueFill = opaqueFill,
                    AccountedBytes = RenderResourceMetrics.ScratchBytes(surface.PixelWidth, surface.PixelHeight),
                };
                if (factory.ResourceCache is { } resourceCache)
                {
                    entry.PersistentLease = resourceCache.Add(cacheKey, entry.Surface, entry.Image);
                    entry.Surface = entry.PersistentLease.Surface;
                    entry.Image = entry.PersistentLease.Image;
                }
                _cache = entry;
                RenderResourceMetrics.BitmapCacheEntryAdded(entry.AccountedBytes);
            }
            catch
            {
                factory.ReleaseScratchSurface(surface);
                throw;
            }
        }

        using (IGraphicsContext cacheContext = factory.CreateContext(entry!.Surface))
        {
            cacheContext.BeginFrame(entry.Surface);
            cacheContext.Clear(opaqueFill ?? Color.Transparent);
            cacheContext.Translate(-bounds.Left, -bounds.Top);

            _cacheSnapshotDepth++;
            try
            {
                // The element's full visual = its own OnRender plus its subtree; both must be
                // captured because the cache blit replaces both.
                OnRender(cacheContext);
                RenderSubtree(cacheContext);
            }
            finally
            {
                _cacheSnapshotDepth--;
            }

            cacheContext.EndFrame();
        }

        entry.Version = version;

        return true;
    }

    private void DisposeCacheEntry()
    {
        if (_cache is not { } entry)
        {
            return;
        }
        _cache = null;
        RenderResourceMetrics.BitmapCacheEntryRemoved(entry.AccountedBytes);

        if (entry.PersistentLease != null)
        {
            entry.PersistentLease.Dispose();
            return;
        }

        entry.Image.Dispose();
        // The surface itself goes back to the device scratch pool so the next cache (any element,
        // any window) can repaint into it instead of allocating.
        var device = Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;
        if (device != null)
        {
            device.ReleaseScratchSurface(entry.Surface);
        }
        else
        {
            entry.Surface.Dispose();
        }
    }

    private sealed class CacheEntry
    {
        public required IRenderSurface Surface { get; set; }
        public required IImage Image { get; set; }
        public IRenderCacheEntry? PersistentLease { get; set; }
        public required RenderCacheKey PersistentKey { get; init; }
        public required int PixelWidth { get; init; }
        public required int PixelHeight { get; init; }
        public required double DpiScale { get; init; }
        public required int DeviceGeneration { get; init; }
        public required Color? OpaqueFill { get; init; }
        public required long AccountedBytes { get; init; }
        public long Version { get; set; }
        public Color InvalidationOverlayColor { get; set; }
    }
}
