using Aprillz.MewUI.Rendering;

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
}

/// <summary>
/// Indicates that an image source can notify when its pixels change.
/// </summary>
public interface INotifyImageChanged
{
    /// <summary>
    /// Raised when the image contents have changed.
    /// </summary>
    event Action? Changed;
}

/// <summary>
/// An image source that draws itself directly into a graphics context, staying crisp at any
/// size (vector). The owning <see cref="Controls.Image"/> renders it at the laid-out size via
/// <see cref="Render"/> instead of rasterizing once through <see cref="IImageSource.CreateImage"/>,
/// so it re-renders when the control resizes. <see cref="IImageSource.CreateImage"/> remains the
/// raster fallback for consumers that need pixels.
/// </summary>
public interface IVectorImageSource : IImageSource
{
    /// <summary>
    /// Intrinsic size in DIPs (e.g. the SVG viewBox), used for measuring. Empty if unknown.
    /// </summary>
    Size IntrinsicSize { get; }

    /// <summary>
    /// Renders the vector content into <paramref name="destRect"/> (control-local coordinates).
    /// </summary>
    void Render(IGraphicsContext context, Rect destRect);
}
