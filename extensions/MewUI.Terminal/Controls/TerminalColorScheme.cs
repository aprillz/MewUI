namespace Aprillz.MewUI.Terminal;

/// <summary>The colors of a terminal: the 16 ANSI colors and the default text, background, cursor and selection colors.</summary>
public sealed class TerminalColorScheme
{
    /// <summary>Creates a scheme; <paramref name="ansiColors"/> must hold the 16 colors black, red, green, yellow, blue, magenta, cyan, white and their bright forms.</summary>
    public TerminalColorScheme(Color foreground, Color background, Color cursor, Color selectionBackground, IReadOnlyList<Color> ansiColors)
    {
        if (ansiColors.Count != 16)
        {
            throw new ArgumentException("A color scheme has 16 ANSI colors.", nameof(ansiColors));
        }

        Foreground = foreground;
        Background = background;
        Cursor = cursor;
        SelectionBackground = selectionBackground;
        AnsiColors = ansiColors.ToArray();
    }

    /// <summary>Gets the default text color.</summary>
    public Color Foreground { get; }

    /// <summary>Gets the default background color.</summary>
    public Color Background { get; }

    /// <summary>Gets the cursor color.</summary>
    public Color Cursor { get; }

    /// <summary>Gets the color selected text is highlighted with.</summary>
    public Color SelectionBackground { get; }

    /// <summary>Gets the 16 ANSI colors.</summary>
    public IReadOnlyList<Color> AnsiColors { get; }

    /// <summary>Gets the Campbell scheme, the default of Windows Terminal.</summary>
    public static TerminalColorScheme Campbell { get; } = new(
        Color.FromRgb(0xCC, 0xCC, 0xCC),
        Color.FromRgb(0x0C, 0x0C, 0x0C),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        Color.FromRgb(0xFF, 0xFF, 0xFF),
        [
            Color.FromRgb(0x0C, 0x0C, 0x0C), Color.FromRgb(0xC5, 0x0F, 0x1F), Color.FromRgb(0x13, 0xA1, 0x0E), Color.FromRgb(0xC1, 0x9C, 0x00),
            Color.FromRgb(0x00, 0x37, 0xDA), Color.FromRgb(0x88, 0x17, 0x98), Color.FromRgb(0x3A, 0x96, 0xDD), Color.FromRgb(0xCC, 0xCC, 0xCC),
            Color.FromRgb(0x76, 0x76, 0x76), Color.FromRgb(0xE7, 0x48, 0x56), Color.FromRgb(0x16, 0xC6, 0x0C), Color.FromRgb(0xF9, 0xF1, 0xA5),
            Color.FromRgb(0x3B, 0x78, 0xFF), Color.FromRgb(0xB4, 0x00, 0x9E), Color.FromRgb(0x61, 0xD6, 0xD6), Color.FromRgb(0xF2, 0xF2, 0xF2),
        ]);

    /// <summary>Gets the One Half Light scheme, a light scheme of Windows Terminal.</summary>
    public static TerminalColorScheme OneHalfLight { get; } = new(
        Color.FromRgb(0x38, 0x3A, 0x42),
        Color.FromRgb(0xFA, 0xFA, 0xFA),
        Color.FromRgb(0x4F, 0x52, 0x5D),
        Color.FromRgb(0x38, 0x3A, 0x42),
        [
            Color.FromRgb(0x38, 0x3A, 0x42), Color.FromRgb(0xE4, 0x56, 0x49), Color.FromRgb(0x50, 0xA1, 0x4F), Color.FromRgb(0xC1, 0x84, 0x01),
            Color.FromRgb(0x01, 0x84, 0xBC), Color.FromRgb(0xA6, 0x26, 0xA4), Color.FromRgb(0x09, 0x97, 0xB3), Color.FromRgb(0xFA, 0xFA, 0xFA),
            Color.FromRgb(0x4F, 0x52, 0x5D), Color.FromRgb(0xDF, 0x6C, 0x75), Color.FromRgb(0x98, 0xC3, 0x79), Color.FromRgb(0xE4, 0xC0, 0x7A),
            Color.FromRgb(0x61, 0xAF, 0xEF), Color.FromRgb(0xC5, 0x77, 0xDD), Color.FromRgb(0x56, 0xB5, 0xC1), Color.FromRgb(0xFF, 0xFF, 0xFF),
        ]);

    internal static uint ToColorRef(Color color) => (uint)(color.R | (color.G << 8) | (color.B << 16));
}
