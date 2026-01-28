namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Represents a target surface for rendering operations.
/// Abstracts away platform-specific details like HWND/HDC on Windows.
/// </summary>
public interface IRenderTarget
{
    /// <summary>
    /// Gets the pixel width of the render target.
    /// </summary>
    int PixelWidth { get; }

    /// <summary>
    /// Gets the pixel height of the render target.
    /// </summary>
    int PixelHeight { get; }

    /// <summary>
    /// Gets the DPI scale factor (e.g., 1.0 for 96 DPI, 1.5 for 144 DPI).
    /// </summary>
    double DpiScale { get; }
}
