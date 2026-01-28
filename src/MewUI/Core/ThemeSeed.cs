namespace Aprillz.MewUI;

public record ThemeSeed
{
    public static ThemeSeed DefaultLight { get; } = new ThemeSeed
    {
        WindowBackground = Color.FromRgb(250, 250, 250),
        WindowText = Color.FromRgb(30, 30, 30),
        ControlBackground = Color.White,
        ButtonFace = Color.FromRgb(232, 232, 232),
        ButtonDisabledBackground = Color.FromRgb(204, 204, 204)
    };

    public static ThemeSeed DefaultDark { get; } = new ThemeSeed
    {
        WindowBackground = Color.FromRgb(30, 30, 30),
        WindowText = Color.FromRgb(230, 230, 232),
        ControlBackground = Color.FromRgb(26, 26, 27),
        ButtonFace = Color.FromRgb(48, 48, 50),
        ButtonDisabledBackground = Color.FromRgb(60, 60, 64)
    };

    public required Color WindowBackground { get; init; }

    public required Color WindowText { get; init; }

    public required Color ControlBackground { get; init; }

    public required Color ButtonFace { get; init; }

    public required Color ButtonDisabledBackground { get; init; }
}
