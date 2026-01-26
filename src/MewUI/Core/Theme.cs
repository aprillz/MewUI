namespace Aprillz.MewUI;

public record class Theme
{
    private static Accent _defaultAccent = Accent.Blue;
    private static ThemeVariant _default = ThemeVariant.System;
    private static Theme? _light;
    private static Theme? _dark;

    public static Accent DefaultAccent
    {
        get => _defaultAccent;
        set
        {
            if (Application.IsRunning)
            {
                throw new InvalidOperationException("Theme.DefaultAccent cannot be changed after Application is running.");
            }

            if (_defaultAccent == value)
            {
                return;
            }

            _defaultAccent = value;
            _light = null;
            _dark = null;
        }
    }

    public static ThemeVariant Default
    {
        get => _default;
        set
        {
            if (Application.IsRunning)
            {
                throw new InvalidOperationException("Theme.Default cannot be changed after Application is running.");
            }

            _default = value;
        }
    }

    public static Theme Light => _light ??= CreateLight();

    public static Theme Dark => _dark ??= CreateDark();

    internal static ThemeVariant ResolveVariant(ThemeVariant variant)
    {
        if (variant != ThemeVariant.System)
        {
            return variant;
        }

        if (Application.IsRunning)
        {
            return Application.Current.PlatformHost.GetSystemThemeVariant();
        }

        // Application is not initialized yet, but we still want System to reflect the OS theme
        // so windows can be created with the correct initial colors.
        try
        {
            return Application.DefaultPlatformHost.GetSystemThemeVariant();
        }
        catch
        {
            return ThemeVariant.Light;
        }
    }

    internal static Theme DefaultMericTheme { get; } = new Theme
    {
        Name = null!,
        Palette = null!,
        BaseControlHeight = 28,
        ControlCornerRadius = 4,
        ListItemPadding = new Thickness(8, 2, 8, 2),
        FontFamily = "Segoe UI",
        FontSize = 12,
        FontWeight = FontWeight.Normal,
        ScrollBarThickness = 4,
        ScrollBarHitThickness = 10,
        ScrollBarMinThumbLength = 14,
        ScrollWheelStep = 32,
        ScrollBarSmallChange = 24,
        ScrollBarLargeChange = 120
    };

    internal static ThemeSeed LightSeed { get; } = new ThemeSeed
    {
        WindowBackground = Color.FromRgb(250, 250, 250),
        WindowText = Color.FromRgb(30, 30, 30),
        ControlBackground = Color.White,
        ButtonFace = Color.FromRgb(232, 232, 232),
        ButtonDisabledBackground = Color.FromRgb(204, 204, 204)
    };

    internal static ThemeSeed DarkSeed { get; } = new ThemeSeed
    {
        WindowBackground = Color.FromRgb(30, 30, 30),
        WindowText = Color.FromRgb(230, 230, 232),
        ControlBackground = Color.FromRgb(26, 26, 27),
        ButtonFace = Color.FromRgb(48, 48, 50),
        ButtonDisabledBackground = Color.FromRgb(60, 60, 64)
    };

    public required string Name { get; init; }

    public required Palette Palette { get; init; }

    public required double BaseControlHeight { get; init; }

    public required double ControlCornerRadius { get; init; }

    public required Thickness ListItemPadding { get; init; }

    public required string FontFamily { get; init; }

    public required double FontSize { get; init; }

    public required FontWeight FontWeight { get; init; }

    public required double ScrollBarThickness { get; init; }

    public required double ScrollBarHitThickness { get; init; }

    public required double ScrollBarMinThumbLength { get; init; }

    public required double ScrollWheelStep { get; init; }

    public required double ScrollBarSmallChange { get; init; }

    public required double ScrollBarLargeChange { get; init; }

    public bool IsDark => Palette.IsDarkBackground(Palette.WindowBackground);

    public Theme WithPalette(Palette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);

        return this with
        {
            Palette = palette
        };
    }

    private static Theme CreateLight()
    {
        var palette = new Palette(LightSeed, DefaultAccent.GetColor(false));

        return DefaultMericTheme with
        {
            Name = "Light",
            Palette = palette,
        };
    }

    private static Theme CreateDark()
    {
        var palette = new Palette(DarkSeed, DefaultAccent.GetColor(true));

        return DefaultMericTheme with
        {
            Name = "Dark",
            Palette = palette,
        };
    }
}

public enum ThemeVariant
{
    System,
    Light,
    Dark
}
