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
