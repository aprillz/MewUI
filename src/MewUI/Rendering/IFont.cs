namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Abstract interface for font resources.
/// </summary>
public interface IFont : IDisposable
{
    /// <summary>
    /// Gets the requested font family name: the family this font was created for, as the caller asked.
    /// </summary>
    /// <remarks>
    /// Implementations report the requested name, not a resolved or substituted face name, so consumers
    /// can compare against their request; name resolution stays inside the implementation. A
    /// comma-separated request resolves to its first installed candidate, which is reported here; the
    /// installed candidates after it supply the characters it lacks.
    /// </remarks>
    string Family { get; }

    /// <summary>
    /// Gets the font size in DIPs.
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
    /// Gets the font ascent in DIPs (distance from baseline to top of character cell).
    /// </summary>
    double Ascent { get; }

    /// <summary>
    /// Gets the font descent in DIPs (distance from baseline to bottom of character cell).
    /// </summary>
    double Descent { get; }

    /// <summary>
    /// Gets the internal leading in DIPs (extra space above the ascent within the line height).
    /// </summary>
    double InternalLeading { get; }

    /// <summary>
    /// Gets the cap height in DIPs (height of flat capital letters such as 'H'); falls back to
    /// <c>Ascent * 0.7</c> when the font does not report it.
    /// </summary>
    double CapHeight { get; }

    /// <summary>
    /// Gets the x-height in DIPs (distance from the baseline to the top of a lowercase letter such as 'x').
    /// </summary>
    /// <remarks>
    /// Implementations should return the font's actual metric; the default derives it from
    /// <see cref="CapHeight"/> so custom font implementations keep working.
    /// </remarks>
    double XHeight => CapHeight * 0.72;
}
