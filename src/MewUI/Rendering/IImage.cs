namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Abstract interface for image resources.
/// </summary>
public interface IImage : IDisposable
{
    /// <summary>
    /// Gets the width of the image in pixels.
    /// </summary>
    int PixelWidth { get; }

    /// <summary>
    /// Gets the height of the image in pixels.
    /// </summary>
    int PixelHeight { get; }

    /// <summary>
    /// Gets the size of the image.
    /// </summary>
    Size Size => new(PixelWidth, PixelHeight);
}
