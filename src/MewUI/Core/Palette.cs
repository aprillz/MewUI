namespace Aprillz.MewUI;

/// <summary>
/// Computed theme colors derived from a <see cref="ThemeSeed"/> and an accent.
/// </summary>
public sealed class Palette
{
    /// <summary>Window background color.</summary>
    public Color WindowBackground { get; }

    /// <summary>Default window foreground/text color.</summary>
    public Color WindowText { get; }

    /// <summary>Default control background color.</summary>
    public Color ControlBackground { get; }

    /// <summary>Default control border color.</summary>
    public Color ControlBorder { get; }

    public Color ContainerBackground { get; }

    public Color ButtonFace { get; }

    public Color ButtonHoverBackground { get; }

    public Color ButtonPressedBackground { get; }

    public Color ButtonDisabledBackground { get; }

    public Color AccentHoverOverlay { get; }

    public Color AccentPressedOverlay { get; }

    public Color AccentBorderHotOverlay { get; }

    public Color AccentBorderActiveOverlay { get; }

    public Color Accent { get; }

    public Color AccentText { get; }

    public Color DisabledAccent { get; }

    public Color SelectionBackground { get; }

    public Color SelectionText { get; }

    public Color DisabledText { get; }

    public Color PlaceholderText { get; }

    public Color DisabledControlBackground { get; }

    public Color Focus { get; }

    public Color ScrollBarThumb { get; }

    public Color ScrollBarThumbHover { get; }

    public Color ScrollBarThumbActive { get; }

    public ThemeSeed Seed { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Palette"/> class.
    /// </summary>
    public Palette(
        ThemeSeed baseColors,
        Color accent,
        Color? accentText = null)
    {
        Seed = baseColors;

        var windowBackground = baseColors.WindowBackground;
        var windowText = baseColors.WindowText;
        var controlBackground = baseColors.ControlBackground;
        var buttonFace = baseColors.ButtonFace;
        var buttonDisabledBackground = baseColors.ButtonDisabledBackground;

        WindowText = windowText;
        ButtonFace = buttonFace;
        ButtonDisabledBackground = buttonDisabledBackground;

        WindowBackground = ComputeBackground(windowBackground, accent);
        ControlBackground = ComputeBackground(controlBackground, accent);

        ContainerBackground = ComputeContainerBackground(windowBackground, buttonFace, accent);

        Accent = accent;
        AccentText = accentText ?? GetDefaultAccentText(accent);

        var isDark = IsDarkBackground(windowBackground);
        var hoverT = isDark ? 0.22 : 0.14;
        var pressedT = isDark ? 0.32 : 0.24;

        AccentHoverOverlay = accent.WithAlpha((byte)Math.Clamp(Math.Round(hoverT * 255.0), 0, 255));
        AccentPressedOverlay = accent.WithAlpha((byte)Math.Clamp(Math.Round(pressedT * 255.0), 0, 255));

        // Border overlays are typically stronger than background overlays.
        AccentBorderHotOverlay = accent.WithAlpha((byte)Math.Clamp(Math.Round(0.6 * 255.0), 0, 255));
        // Used when a control supplies a custom BorderBrush; keep some of the original hue instead of hard-replacing.
        AccentBorderActiveOverlay = accent.WithAlpha((byte)Math.Clamp(Math.Round(0.85 * 255.0), 0, 255));

        SelectionBackground = ComputeSelectionBackground(controlBackground, accent);
        SelectionText = GetDefaultAccentText(SelectionBackground);

        ControlBorder = ComputeControlBorder(windowBackground, windowText, accent);
        DisabledText = ComputeDisabledText(windowBackground, windowText);
        DisabledAccent = ComputeDisabledAccent(windowBackground, accent, DisabledText);
        PlaceholderText = ComputePlaceholderText(windowBackground, DisabledText);
        DisabledControlBackground = ComputeDisabledControlBackground(windowBackground, controlBackground, windowText);

        // Back-compat result colors derived from overlays (keep ButtonFace-based defaults consistent
        // while allowing controls with custom backgrounds to composite overlays themselves).
        ButtonHoverBackground = Color.Composite(buttonFace, AccentHoverOverlay);
        ButtonPressedBackground = Color.Composite(buttonFace, AccentPressedOverlay);
        Focus = accent;

        (ScrollBarThumb, ScrollBarThumbHover, ScrollBarThumbActive) = ComputeScrollBarThumbs(windowBackground);
    }

    internal static bool UseAlphaPalette { get; set; } = false;

    /// <summary>
    /// Creates a copy of this palette with a different accent.
    /// </summary>
    public Palette WithAccent(Color accent, Color? accentText = null)
    {
        return new Palette(Seed, accent, accentText);
    }

    /// <summary>
    /// Returns true if a background should be treated as dark.
    /// </summary>
    public static bool IsDarkBackground(Color color) => (color.R + color.G + color.B) < 128 * 3;

    private static Color ComputeControlBorder(Color baseColor, Color windowText, Color accent)
    {
        var isDark = IsDarkBackground(baseColor);
        var baseBorder = baseColor.Lerp(windowText, isDark ? 0.23 : 0.21);
        return baseBorder.Lerp(accent, isDark ? 0.04 : 0.05);
    }

    private static Color ComputeContainerBackground(Color baseColor, Color buttonFace, Color accent)
    {
        var isDark = IsDarkBackground(baseColor);
        if (UseAlphaPalette)
        {
            return buttonFace.WithAlpha((byte)((isDark ? 0.25 : 0.15) * 255)).Lerp(accent, isDark ? 0.01 : 0.014);
        }
        else
        {
            return baseColor.Lerp(buttonFace, isDark ? 0.25 : 0.15).Lerp(accent, isDark ? 0.01 : 0.014);
        }
    }

    private static Color ComputeDisabledText(Color baseColor, Color windowText)
    {
        var isDark = IsDarkBackground(baseColor);
        return windowText.Lerp(baseColor, isDark ? 0.52 : 0.58);
    }

    private static Color ComputeDisabledAccent(Color baseColor, Color accent, Color disabledText)
    {
        var isDark = IsDarkBackground(baseColor);
        // Keep some hue from the accent, but move it toward the disabled text tone so "checked"
        // states remain recognizable while clearly disabled.
        return accent.Lerp(disabledText, isDark ? 0.95 : 0.9);
    }

    private Color ComputePlaceholderText(Color baseColor, Color textColor)
    {
        var isDark = IsDarkBackground(baseColor);
        return textColor.WithAlpha(isDark ? (byte)192 : (byte)160);
    }

    private static Color ComputeDisabledControlBackground(Color baseColor, Color controlBackground, Color foreground)
    {
        var isDark = IsDarkBackground(baseColor);
        return controlBackground.Lerp(foreground, isDark ? 0.05 : 0.055);
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
        return luma >= 0.61 ? Color.FromRgb(28, 28, 32) : Color.FromRgb(248, 246, 255);
    }

    private static (Color thumb, Color hover, Color active) ComputeScrollBarThumbs(Color baseColor)
    {
        var isDark = IsDarkBackground(baseColor);
        byte c = isDark ? (byte)255 : (byte)0;
        return (
            Color.FromArgb(0x44, c, c, c),
            Color.FromArgb(0x66, c, c, c),
            Color.FromArgb(0x88, c, c, c)
        );
    }
}
