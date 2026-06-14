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
    private bool _bitmapCachesReleasedWhileCulled;

    // While > 0 on the current thread, the viewport-bounds cull in Render is bypassed: a cache
    // snapshot renders the whole subtree into an offscreen surface, so culling against the window
    // client rect would wrongly drop parts that fall outside the visible viewport.
    [ThreadStatic]
    private static int _cacheSnapshotDepth;

    internal static bool IsRenderingToCache => _cacheSnapshotDepth > 0;

    private void ReleaseBitmapCachesInSubtree()
    {
        if (_bitmapCachesReleasedWhileCulled)
        {
            return;
        }

        VisualTree.Visit(this, static element =>
        {
            if (element is UIElement uiElement)
            {
                uiElement.DisposeCacheEntry();
                uiElement._bitmapCachesReleasedWhileCulled = true;
            }
        });
    }

    private void MarkBitmapCacheVisible()
    {
        _bitmapCachesReleasedWhileCulled = false;
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
        if (_hasBitmapCache)
        {
            _contentVersion++;
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
            context.DrawImage(entry.Image, Bounds);
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

        if (_cache is { } current
            && current.PixelWidth == pixelWidth
            && current.PixelHeight == pixelHeight
            && current.DpiScale == effectiveDpiScale
            && current.DeviceGeneration == deviceGeneration
            && current.Version == version)
        {
            return false;
        }

        DisposeCacheEntry();

        IRenderSurface surface = factory.CreateSurface(
            RenderSurfaceDescriptor.CachedImage(pixelWidth, pixelHeight, effectiveDpiScale));

        using (IGraphicsContext cacheContext = factory.CreateContext(surface))
        {
            cacheContext.BeginFrame(surface);
            cacheContext.Clear(Color.Transparent);
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

        _cache = new CacheEntry
        {
            Surface = surface,
            Image = factory.CreateImageView(surface),
            PixelWidth = pixelWidth,
            PixelHeight = pixelHeight,
            DpiScale = effectiveDpiScale,
            DeviceGeneration = deviceGeneration,
            Version = version,
        };

        return true;
    }

    private void DisposeCacheEntry()
    {
        _cache?.Dispose();
        _cache = null;
    }

    private sealed class CacheEntry : IDisposable
    {
        public required IRenderSurface Surface { get; init; }
        public required IImage Image { get; init; }
        public required int PixelWidth { get; init; }
        public required int PixelHeight { get; init; }
        public required double DpiScale { get; init; }
        public required int DeviceGeneration { get; init; }
        public required long Version { get; init; }
        public Color InvalidationOverlayColor { get; set; }

        public void Dispose()
        {
            Image.Dispose();
            Surface.Dispose();
        }
    }
}
