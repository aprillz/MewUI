namespace Aprillz.MewUI;

/// <summary>
/// Base class for render-cache policies assigned to <see cref="Controls.UIElement.CacheMode"/>.
/// </summary>
public abstract class CacheMode
{
}

/// <summary>
/// Caches an element's rendered output (its own <c>OnRender</c> plus its subtree) into an
/// offscreen bitmap and blits that bitmap each frame until the element's content, size, or DPI
/// changes. The visual tree is kept intact: layout, hit testing, focus, and state continue to run
/// on the live element — only painting is served from the cache. Clearing <c>CacheMode</c> resumes
/// live rendering immediately. Backing bitmaps are released while their subtree is hidden,
/// outside the window viewport, or detached, and are recreated when rendered again.
/// <para/>
/// Settings are immutable. To change them, assign a new <see cref="BitmapCache"/> instance to
/// <see cref="Controls.UIElement.CacheMode"/> so the property change is observed and the cache is
/// rebuilt. Mutating an already-assigned instance would not invalidate the cache.
/// </summary>
public sealed class BitmapCache : CacheMode
{
    /// <summary>
    /// Scale at which the cache bitmap is rendered relative to device pixels (1.0 = device pixels).
    /// Above 1 produces a sharper cache at higher memory cost; below 1, softer and cheaper.
    /// </summary>
    public double RenderAtScale { get; init; } = 1.0;

    /// <summary>
    /// Whether the cache bitmap dimensions are snapped to whole device pixels to avoid blur.
    /// </summary>
    public bool SnapsToDevicePixels { get; init; } = true;
}
