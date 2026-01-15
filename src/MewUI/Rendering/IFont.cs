namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Abstract interface for font resources.
/// </summary>
public interface IFont : IDisposable
{
    /// <summary>
    /// Gets the font family name.
    /// </summary>
    string Family { get; }

    /// <summary>
    /// Gets the font size in device-independent units.
    /// </summary>
    double Size { get; }

    /// <summary>
    /// Gets the font weight.
    /// </summary>
    FontWeight Weight { get; }

    /// <summary>
    /// Gets whether the font is italic.
    /// </summary>
    bool IsItalic { get; }

    /// <summary>
    /// Gets whether the font has underline.
    /// </summary>
    bool IsUnderline { get; }

    /// <summary>
    /// Gets whether the font has strikethrough.
    /// </summary>
    bool IsStrikethrough { get; }
}