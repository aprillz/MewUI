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

    /// <summary>
    /// Gets the font ascent in device-independent units (distance from baseline to top of character cell).
    /// </summary>
    double Ascent { get; }

    /// <summary>
    /// Gets the font descent in device-independent units (distance from baseline to bottom of character cell).
    /// </summary>
    double Descent { get; }

    /// <summary>
    /// Gets the internal leading in device-independent units (extra space above the ascent within the line height).
    /// </summary>
    double InternalLeading { get; }
}