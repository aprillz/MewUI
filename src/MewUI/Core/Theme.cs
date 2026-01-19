using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

public record class Theme
{
    public static Theme Light { get; } = CreateLight();
    public static Theme Dark { get; } = CreateDark();

    public static Theme Current
    {
        get => _current;
        set
        {
            ArgumentNullException.ThrowIfNull(value);

            if (_current == value)
            {
                return;
            }

            var old = _current;
            _current = value;
            CurrentChanged?.Invoke(old, value);
        }
    }

    private static Theme _current = Light;

    public static Action<Theme, Theme>? CurrentChanged { get; set; }

    public string Name { get; init; }

    public Palette Palette { get; }

    public double BaseControlHeight { get; init; }

    public double ControlCornerRadius { get; init; }

    public Thickness ListItemPadding { get; init; }

    public string FontFamily { get; init; }
    public double FontSize { get; init; }
    public FontWeight FontWeight { get; init; }

    // Scroll (thin style defaults)
    public double ScrollBarThickness { get; init; }
    public double ScrollBarHitThickness { get; init; }
    public double ScrollBarMinThumbLength { get; init; }
    public double ScrollWheelStep { get; init; }
    public double ScrollBarSmallChange { get; init; }
    public double ScrollBarLargeChange { get; init; }

    public Theme WithAccent(Color accent, Color? accentText = null)
    {
        return WithPalette(Palette.WithAccent(accent, accentText));
    }

    private Theme(
        string name,
        Palette palette,
        double baseControlHeight,
        double controlCornerRadius,
        Thickness listItemPadding,
        string fontFamily,
        double fontSize,
        FontWeight fontWeight,
        double scrollBarThickness,
        double scrollBarHitThickness,
        double scrollBarMinThumbLength,
        double scrollWheelStep,
        double scrollBarSmallChange,
        double scrollBarLargeChange)
    {
        Name = name;
        Palette = palette;
        BaseControlHeight = baseControlHeight;
        ControlCornerRadius = controlCornerRadius;
        ListItemPadding = listItemPadding;
        FontFamily = fontFamily;
        FontSize = fontSize;
        FontWeight = fontWeight;

        ScrollBarThickness = scrollBarThickness;
        ScrollBarHitThickness = scrollBarHitThickness;
        ScrollBarMinThumbLength = scrollBarMinThumbLength;
        ScrollWheelStep = scrollWheelStep;
        ScrollBarSmallChange = scrollBarSmallChange;
        ScrollBarLargeChange = scrollBarLargeChange;
    }

    public Theme WithPalette(Palette palette)
    {
        ArgumentNullException.ThrowIfNull(palette);

        return new Theme(
            Name,
            palette,
            BaseControlHeight,
            ControlCornerRadius,
            ListItemPadding,
            FontFamily,
            FontSize,
            FontWeight,
            ScrollBarThickness,
            ScrollBarHitThickness,
            ScrollBarMinThumbLength,
            ScrollWheelStep,
            ScrollBarSmallChange,
            ScrollBarLargeChange);
    }

    private static Theme CreateLight()
    {
        var palette = new Palette(
            name: "Light",
            baseColors: new ThemeSeed(
                WindowBackground: Color.FromRgb(244, 244, 244),
                WindowText: Color.FromRgb(30, 30, 30),
                ControlBackground: Color.White,
                ButtonFace: Color.FromRgb(232, 232, 232),
                ButtonDisabledBackground: Color.FromRgb(204, 204, 204)),
            accent: Color.FromRgb(214, 176, 82));

        return new Theme(
            name: "Light",
            palette: palette,
            baseControlHeight: 28,
            controlCornerRadius: 4,
            listItemPadding: new Thickness(8, 2, 8, 2),
            fontFamily: "Segoe UI",
            fontSize: 12,
            fontWeight: FontWeight.Normal,
            scrollBarThickness: 4,
            scrollBarHitThickness: 10,
            scrollBarMinThumbLength: 14,
            scrollWheelStep: 32,
            scrollBarSmallChange: 24,
            scrollBarLargeChange: 120);
    }

    private static Theme CreateDark()
    {
        var palette = new Palette(
            name: "Dark",
            baseColors: new ThemeSeed(
                WindowBackground: Color.FromRgb(28, 28, 28),
                WindowText: Color.FromRgb(230, 230, 232),
                ControlBackground: Color.FromRgb(26, 26, 27),
                ButtonFace: Color.FromRgb(48, 48, 50),
                ButtonDisabledBackground: Color.FromRgb(60, 60, 64)),
            accent: Color.FromRgb(214, 165, 94));

        return new Theme(
            name: "Dark",
            palette: palette,
            baseControlHeight: 28,
            controlCornerRadius: 4,
            listItemPadding: new Thickness(8, 2, 8, 2),
            fontFamily: "Segoe UI",
            fontSize: 12,
            fontWeight: FontWeight.Normal,
            scrollBarThickness: 4,
            scrollBarHitThickness: 10,
            scrollBarMinThumbLength: 14,
            scrollWheelStep: 32,
            scrollBarSmallChange: 24,
            scrollBarLargeChange: 120);
    }
}
