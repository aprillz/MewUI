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
/// on the live element - only painting is served from the cache. Clearing <c>CacheMode</c> resumes
/// live rendering immediately. Backing bitmaps are released while their subtree is hidden,
/// outside the window viewport, or detached, and are recreated when rendered again.
/// </summary>
/// <remarks>
/// Settings are immutable. To change them, assign a new <see cref="BitmapCache"/> instance to
/// <see cref="Controls.UIElement.CacheMode"/> so the property change is observed and the cache is
/// rebuilt. Mutating an already-assigned instance would not invalidate the cache.
/// </remarks>
public sealed class BitmapCache : CacheMode
{
    /// <summary>
    /// Gets or sets the scale at which the cache bitmap is rendered relative to device pixels; the default
    /// is 1.0, above 1 is sharper at a higher memory cost and below 1 is softer and cheaper.
    /// </summary>
    public double RenderAtScale { get; init; } = 1.0;

    /// <summary>
    /// Gets or sets whether the cache bitmap dimensions are snapped to whole device pixels to avoid blur;
    /// the default is <see langword="true"/>.
    /// </summary>
    public bool SnapsToDevicePixels { get; init; } = true;
}
