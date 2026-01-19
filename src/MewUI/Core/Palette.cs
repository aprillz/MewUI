namespace Aprillz.MewUI;

public sealed class Palette
{
    private readonly ThemeSeed _seed;

    public string Name { get; }

    public Color WindowBackground { get; }

    public Color WindowText { get; }

    public Color ControlBackground { get; }

    public Color ControlBorder { get; }

    public Color ContainerBackground { get; }

    public Color ButtonFace { get; }

    public Color ButtonHoverBackground { get; }

    public Color ButtonPressedBackground { get; }

    public Color ButtonDisabledBackground { get; }

    public Color Accent { get; }

    public Color AccentText { get; }

    public Color SelectionBackground { get; }

    public Color SelectionText { get; }

    public Color DisabledText { get; }

    public Color PlaceholderText { get; }

    public Color DisabledControlBackground { get; }

    public Color Focus { get; }

    public Color ScrollBarThumb { get; }

    public Color ScrollBarThumbHover { get; }

    public Color ScrollBarThumbActive { get; }

    public ThemeSeed Seed => _seed;

    public Palette(
        string name,
        ThemeSeed baseColors,
        Color accent,
        Color? accentText = null)
    {
        Name = name;
        _seed = baseColors;

        var windowBackground = baseColors.WindowBackground;
        var windowText = baseColors.WindowText;
        var controlBackground = baseColors.ControlBackground;
        var buttonFace = baseColors.ButtonFace;
        var buttonDisabledBackground = baseColors.ButtonDisabledBackground;

        WindowBackground = ComputeBackground(windowBackground, accent);
        WindowText = windowText;
        ControlBackground = ComputeBackground(controlBackground, accent);
        ButtonFace = buttonFace;
        ButtonDisabledBackground = buttonDisabledBackground;

        ContainerBackground = ComputeContainerBackground(windowBackground, buttonFace);

        Accent = accent;
        AccentText = accentText ?? GetDefaultAccentText(accent);

        var isDark = IsDarkBackground(windowBackground);
        var hoverT = isDark ? 0.22 : 0.14;
        var pressedT = isDark ? 0.32 : 0.24;

        SelectionBackground = ComputeSelectionBackground(controlBackground, accent);
        SelectionText = GetDefaultAccentText(SelectionBackground);

        ControlBorder = ComputeControlBorder(windowBackground, windowText, accent);
        DisabledText = ComputeDisabledText(windowBackground, windowText);
        PlaceholderText = DisabledText.WithAlpha(127);
        DisabledControlBackground = ComputeDisabledControlBackground(windowBackground, controlBackground);
        ButtonHoverBackground = buttonFace.Lerp(accent, hoverT);
        ButtonPressedBackground = buttonFace.Lerp(accent, pressedT);
        Focus = accent;

        (ScrollBarThumb, ScrollBarThumbHover, ScrollBarThumbActive) = ComputeScrollBarThumbs(windowBackground);
    }

    public Palette WithAccent(Color accent, Color? accentText = null)
    {
        return new Palette(Name, _seed, accent, accentText);
    }

    public static bool IsDarkBackground(Color color) => (color.R + color.G + color.B) < 128 * 3;

    private static Color ComputeControlBorder(Color windowBackground, Color windowText, Color accent)
    {
        var isDark = IsDarkBackground(windowBackground);
        var baseBorder = windowBackground.Lerp(windowText, isDark ? 0.18 : 0.21);
        return baseBorder.Lerp(accent, isDark ? 0.04 : 0.05);
    }

    private static Color ComputeContainerBackground(Color windowBackground, Color buttonFace)
    {
        var isDark = IsDarkBackground(windowBackground);
        return windowBackground.Lerp(buttonFace, isDark ? 0.5 : 0.25);
    }

    private static Color ComputeDisabledText(Color windowBackground, Color windowText)
    {
        var isDark = IsDarkBackground(windowBackground);
        return windowText.Lerp(windowBackground, isDark ? 0.72 : 0.58);
    }

    private static Color ComputeDisabledControlBackground(Color windowBackground, Color controlBackground)
    {
        var isDark = IsDarkBackground(windowBackground);
        return controlBackground.Lerp(windowBackground, isDark ? 0.45 : 0.55);
    }

    private static Color ComputeBackground(Color background, Color accent)
    {
        var isDark = IsDarkBackground(background);
        var t = isDark ? 0.012 : 0.0125;
        return background.Lerp(accent, t);
    }

    private static Color ComputeSelectionBackground(Color controlBackground, Color accent)
    {
        var isDark = IsDarkBackground(controlBackground);
        var t = isDark ? 0.45 : 0.35;
        return controlBackground.Lerp(accent, t);
    }

    private static Color GetDefaultAccentText(Color accent)
    {
        var luma = (0.2126 * accent.R + 0.7152 * accent.G + 0.0722 * accent.B) / 255.0;
        return luma >= 0.6 ? Color.FromRgb(28, 28, 32) : Color.FromRgb(248, 246, 255);
    }

    private static (Color thumb, Color hover, Color active) ComputeScrollBarThumbs(Color windowBackground)
    {
        var isDark = IsDarkBackground(windowBackground);
        byte c = isDark ? (byte)255 : (byte)0;
        return (
            Color.FromArgb(0x44, c, c, c),
            Color.FromArgb(0x66, c, c, c),
            Color.FromArgb(0x88, c, c, c)
        );
    }
}
