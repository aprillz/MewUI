using Aprillz.MewUI.Primitives;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Core;

public sealed class Theme
{
    public static Theme Light { get; } = CreateLight();
    public static Theme Dark { get; } = CreateDark();

    public static Theme Current
    {
        get => _current;
        set
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (_current == value) return;
            var old = _current;
            _current = value;
            CurrentChanged?.Invoke(old, value);
        }
    }

    private static Theme _current = Light;

    public static Action<Theme, Theme>? CurrentChanged { get; set; }

    public string Name { get; }

    public Palette Palette { get; }

    public Color WindowBackground => Palette.WindowBackground;
    public Color WindowText => Palette.WindowText;
    public Color ControlBackground => Palette.ControlBackground;
    public Color ControlBorder => Palette.ControlBorder;

    public Color ButtonFace => Palette.ButtonFace;
    public Color ButtonHoverBackground => Palette.ButtonHoverBackground;
    public Color ButtonPressedBackground => Palette.ButtonPressedBackground;
    public Color ButtonDisabledBackground => Palette.ButtonDisabledBackground;

    public Color Accent => Palette.Accent;
    public Color AccentText => Palette.AccentText;
    public Color SelectionBackground => Palette.SelectionBackground;
    public Color SelectionText => Palette.SelectionText;

    public Color DisabledText => Palette.DisabledText;
    public Color PlaceholderText => Palette.PlaceholderText;
    public Color TextBoxDisabledBackground => Palette.TextBoxDisabledBackground;
    public Color FocusRect => Palette.FocusRect;

    public double ControlCornerRadius { get; }

    public string FontFamily { get; }
    public double FontSize { get; }
    public FontWeight FontWeight { get; }

    // Scroll (thin style defaults)
    public double ScrollBarThickness { get; }
    public double ScrollBarHitThickness { get; }
    public double ScrollBarMinThumbLength { get; }
    public double ScrollWheelStep { get; }
    public double ScrollBarSmallChange { get; }
    public double ScrollBarLargeChange { get; }
    public Color ScrollBarThumb { get; }
    public Color ScrollBarThumbHover { get; }
    public Color ScrollBarThumbActive { get; }

    public Theme WithAccent(Color accent, Color? accentText = null)
    {
        return WithPalette(Palette.WithAccent(accent, accentText));
    }

    private Theme(
        string name,
        Palette palette,
        double controlCornerRadius,
        string fontFamily,
        double fontSize,
        FontWeight fontWeight,
        double scrollBarThickness,
        double scrollBarHitThickness,
        double scrollBarMinThumbLength,
        double scrollWheelStep,
        double scrollBarSmallChange,
        double scrollBarLargeChange,
        Color scrollBarThumb,
        Color scrollBarThumbHover,
        Color scrollBarThumbActive)
    {
        Name = name;
        Palette = palette;
        ControlCornerRadius = controlCornerRadius;
        FontFamily = fontFamily;
        FontSize = fontSize;
        FontWeight = fontWeight;

        ScrollBarThickness = scrollBarThickness;
        ScrollBarHitThickness = scrollBarHitThickness;
        ScrollBarMinThumbLength = scrollBarMinThumbLength;
        ScrollWheelStep = scrollWheelStep;
        ScrollBarSmallChange = scrollBarSmallChange;
        ScrollBarLargeChange = scrollBarLargeChange;
        ScrollBarThumb = scrollBarThumb;
        ScrollBarThumbHover = scrollBarThumbHover;
        ScrollBarThumbActive = scrollBarThumbActive;
    }

    public Theme WithPalette(Palette palette)
    {
        if (palette == null) throw new ArgumentNullException(nameof(palette));
        return new Theme(
            name: Name,
            palette: palette,
            controlCornerRadius: ControlCornerRadius,
            fontFamily: FontFamily,
            fontSize: FontSize,
            fontWeight: FontWeight,
            scrollBarThickness: ScrollBarThickness,
            scrollBarHitThickness: ScrollBarHitThickness,
            scrollBarMinThumbLength: ScrollBarMinThumbLength,
            scrollWheelStep: ScrollWheelStep,
            scrollBarSmallChange: ScrollBarSmallChange,
            scrollBarLargeChange: ScrollBarLargeChange,
            scrollBarThumb: ScrollBarThumb,
            scrollBarThumbHover: ScrollBarThumbHover,
            scrollBarThumbActive: ScrollBarThumbActive);
    }

    private static Theme CreateLight()
    {
        var palette = new Palette(
            name: "Light",
            windowBackground: Color.FromRgb(244, 244, 244),
            windowText: Color.FromRgb(30, 30, 30),
            controlBackground: Color.White,
            buttonFace: Color.FromRgb(232, 232, 232),
            buttonDisabledBackground: Color.FromRgb(204, 204, 204),
            accent: Color.FromRgb(214, 176, 82));

        return new Theme(
            name: "Light",
            palette: palette,
            controlCornerRadius: 4,
            fontFamily: "Segoe UI",
            fontSize: 12,
            fontWeight: FontWeight.Normal,
            scrollBarThickness: 4,
            scrollBarHitThickness: 10,
            scrollBarMinThumbLength: 14,
            scrollWheelStep: 32,
            scrollBarSmallChange: 24,
            scrollBarLargeChange: 120,
            scrollBarThumb: Color.FromArgb(0x44, 0, 0, 0),
            scrollBarThumbHover: Color.FromArgb(0x66, 0, 0, 0),
            scrollBarThumbActive: Color.FromArgb(0x88, 0, 0, 0));
    }

    private static Theme CreateDark()
    {
        var palette = new Palette(
            name: "Dark",
            windowBackground: Color.FromRgb(24, 24, 24),
            windowText: Color.FromRgb(230, 230, 232),
            controlBackground: Color.FromRgb(38, 38, 40),
            buttonFace: Color.FromRgb(48, 48, 50),
            buttonDisabledBackground: Color.FromRgb(60, 60, 64),
            accent: Color.FromRgb(214, 165, 94));

        return new Theme(
            name: "Dark",
            palette: palette,
            controlCornerRadius: 4,
            fontFamily: "Segoe UI",
            fontSize: 12,
            fontWeight: FontWeight.Normal,
            scrollBarThickness: 4,
            scrollBarHitThickness: 10,
            scrollBarMinThumbLength: 14,
            scrollWheelStep: 32,
            scrollBarSmallChange: 24,
            scrollBarLargeChange: 120,
            scrollBarThumb: Color.FromArgb(0x44, 255, 255, 255),
            scrollBarThumbHover: Color.FromArgb(0x66, 255, 255, 255),
            scrollBarThumbActive: Color.FromArgb(0x88, 255, 255, 255));
    }
}
