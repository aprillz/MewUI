using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI;

/// <summary>
/// Provides an <see cref="IImage"/> for a given rendering backend.
/// </summary>
public interface IImageSource
{
    /// <summary>
    /// Creates a backend image from this source.
    /// </summary>
    /// <param name="factory">The graphics factory used to create backend resources.</param>
    IImage CreateImage(IGraphicsFactory factory);

    /// <summary>
    /// Creates a backend image for a known device-pixel footprint, which implementations may use to bound
    /// decode and upload work.
    /// </summary>
    /// <remarks>
    /// Ignoring the hint must preserve the same visual result.
    /// </remarks>
    IImage CreateImage(IGraphicsFactory factory, int targetPixelWidth, int targetPixelHeight)
        => CreateImage(factory);
}

/// <summary>
/// An image source that carries the orientation parsed from its metadata (for example JPEG EXIF).
/// </summary>
/// <remarks>
/// The raw pixels stay in their as-decoded order; consumers apply <see cref="Orientation"/> at layout and
/// draw time. Sources without orientation metadata (raw pixels, vectors) report
/// <see cref="ImageOrientation.Identity"/>.
/// </remarks>
public interface IOrientedImageSource : IImageSource
{
    /// <summary>Gets the orientation parsed from the source, or <see cref="ImageOrientation.Identity"/> when none.</summary>
    ImageOrientation Orientation { get; }
}

/// <summary>
/// Indicates that an image source can notify when its pixels change.
/// </summary>
public interface INotifyImageChanged
{
    /// <summary>
    /// Occurs when the image contents have changed.
    /// </summary>
    event Action? Changed;
}

/// <summary>
/// An image source that draws itself directly into a graphics context, staying crisp at any size.
/// </summary>
/// <remarks>
/// The owning <see cref="Controls.Image"/> renders it at the laid-out size through <see cref="Render"/>
/// instead of rasterizing once through <see cref="IImageSource.CreateImage(IGraphicsFactory)"/>, so it
/// re-renders when the control resizes. <see cref="IImageSource.CreateImage(IGraphicsFactory)"/> remains the
/// raster fallback for consumers that need pixels.
/// </remarks>
public interface IVectorImageSource : IImageSource
{
    /// <summary>
    /// Gets the intrinsic size in DIPs (for example the SVG viewBox) used for measuring; empty when unknown.
    /// </summary>
    Size IntrinsicSize { get; }

    /// <summary>
    /// Renders the vector content into <paramref name="destRect"/> (control-local coordinates).
    /// </summary>
    void Render(IGraphicsContext context, Rect destRect);
}
