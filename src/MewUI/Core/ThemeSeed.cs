namespace Aprillz.MewUI;

/// <summary>
/// Defines a small set of base colors used to generate a <see cref="Theme"/> palette.
/// </summary>
public record ThemeSeed
{
    /// <summary>
    /// Gets the default light theme seed.
    /// </summary>
    public static ThemeSeed DefaultLight { get; } = new ThemeSeed
    {
        WindowBackground = Color.FromRgb(250, 250, 250),
        WindowText = Color.FromRgb(30, 30, 30),
        ControlBackground = Color.White,
        ButtonFace = Color.FromRgb(232, 232, 232),
        ButtonDisabledBackground = Color.FromRgb(204, 204, 204)
    };

    /// <summary>
    /// Gets the default dark theme seed.
    /// </summary>
    public static ThemeSeed DefaultDark { get; } = new ThemeSeed
    {
        WindowBackground = Color.FromRgb(30, 30, 30),
        WindowText = Color.FromRgb(230, 230, 232),
        ControlBackground = Color.FromRgb(26, 26, 27),
        ButtonFace = Color.FromRgb(48, 48, 50),
        ButtonDisabledBackground = Color.FromRgb(60, 60, 64)
    };

    /// <summary>
    /// Gets the window background color.
    /// </summary>
    public required Color WindowBackground { get; init; }

    /// <summary>
    /// Gets the window foreground/text color.
    /// </summary>
    public required Color WindowText { get; init; }

    /// <summary>
    /// Gets the default control background color.
    /// </summary>
    public required Color ControlBackground { get; init; }

    /// <summary>
    /// Gets the face color for button-like controls.
    /// </summary>
    public required Color ButtonFace { get; init; }

    /// <summary>
    /// Gets the background color used for disabled buttons.
    /// </summary>
    public required Color ButtonDisabledBackground { get; init; }
}
