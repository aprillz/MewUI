namespace Aprillz.MewUI;

/// <summary>
/// Defines common layout and typography metrics used by themes and controls.
/// </summary>
public sealed record class ThemeMetrics
{
    private const string FALLBACK_FONT_FAMILY = "Segoe UI";

    // Set when the platform package registers, before any window exists. Reads before that fall back.
    private static string _platformFontFamily = FALLBACK_FONT_FAMILY;

    private readonly string _fontFamily = string.Empty;

    /// <summary>
    /// Gets the <see cref="FontFamily"/> value that follows the platform's system UI font.
    /// </summary>
    public static string SystemFontFamily { get; } = string.Empty;

    internal static string PlatformFontFamily
    {
        get => _platformFontFamily;
        set => _platformFontFamily = string.IsNullOrWhiteSpace(value) ? FALLBACK_FONT_FAMILY : value;
    }

    /// <summary>
    /// Gets the default theme metrics.
    /// </summary>
    public static ThemeMetrics Default { get; } = new ThemeMetrics
    {
        BaseControlHeight = 28,
        ControlCornerRadius = 4,
        ControlBorderThickness = 0.5,
        ItemPadding = new Thickness(8, 2, 8, 2),
        ContainerPadding = new Thickness(8),
        FontFamily = SystemFontFamily,
        FontSizeSmall = 11,
        FontSize = 12,
        FontSizeMedium = 14,
        FontSizeLarge = 18,
        FontSizeExtraLarge = 22,
        FontWeight = FontWeight.Normal,
        ScrollBarThickness = 4,
        ScrollBarHitThickness = 10,
        ScrollBarMinThumbLength = 14,
        ScrollWheelStep = 50,
        ScrollBarSmallChange = 24,
        ScrollBarLargeChange = 120,
        ContextMenuIconSize = 16,
        ToolBarIconSize = 24
    };

    /// <summary>
    /// Gets the baseline height for standard controls (in DIPs).
    /// </summary>
    public required double BaseControlHeight { get; init; }

    /// <summary>
    /// Gets the default corner radius for controls (in DIPs).
    /// </summary>
    public required double ControlCornerRadius { get; init; }

    /// <summary>
    /// Gets the default border thickness for standard controls (in DIPs).
    /// </summary>
    public required double ControlBorderThickness { get; init; }

    /// <summary>
    /// Gets the default padding for container controls (in DIPs).
    /// </summary>
    public required Thickness ContainerPadding { get; init; }

    /// <summary>
    /// Gets the default padding for list items (in DIPs).
    /// </summary>
    public required Thickness ItemPadding { get; init; }

    /// <summary>
    /// Gets the default font family name, resolved to the platform's system UI font
    /// when <see cref="SystemFontFamily"/> was assigned.
    /// </summary>
    public required string FontFamily
    {
        get => _fontFamily.Length == 0 ? PlatformFontFamily : _fontFamily;
        init => _fontFamily = string.IsNullOrWhiteSpace(value) ? SystemFontFamily : value;
    }

    /// <summary>
    /// Gets whether the font family follows the platform's system UI font.
    /// </summary>
    public bool IsSystemFontFamily => _fontFamily.Length == 0;

    /// <summary>
    /// Gets the default font size (in DIPs).
    /// </summary>
    public required double FontSize { get; init; }

    /// <summary>
    /// Gets the small font size, one step below <see cref="FontSize"/> (in DIPs).
    /// </summary>
    public required double FontSizeSmall { get; init; }

    /// <summary>
    /// Gets the medium font size, one step above <see cref="FontSize"/> (in DIPs).
    /// </summary>
    public required double FontSizeMedium { get; init; }

    /// <summary>
    /// Gets the large font size, for section headings (in DIPs).
    /// </summary>
    public double FontSizeLarge { get; init; } = 18;

    /// <summary>
    /// Gets the extra large font size, for page titles (in DIPs).
    /// </summary>
    public double FontSizeExtraLarge { get; init; } = 22;

    /// <summary>
    /// Gets the default font weight.
    /// </summary>
    public required FontWeight FontWeight { get; init; }

    /// <summary>
    /// Gets the visual thickness of the scroll bar thumb/track (in DIPs).
    /// </summary>
    public required double ScrollBarThickness { get; init; }

    /// <summary>
    /// Gets the minimum hit-test thickness for scroll bars (in DIPs).
    /// </summary>
    public required double ScrollBarHitThickness { get; init; }

    /// <summary>
    /// Gets the minimum thumb length for scroll bars (in DIPs).
    /// </summary>
    public required double ScrollBarMinThumbLength { get; init; }

    /// <summary>
    /// Gets the scroll wheel step (in DIPs).
    /// </summary>
    public required double ScrollWheelStep { get; init; }

    /// <summary>
    /// Gets the small-change amount used by scroll bars (in DIPs).
    /// </summary>
    public required double ScrollBarSmallChange { get; init; }

    /// <summary>
    /// Gets the large-change amount used by scroll bars (in DIPs).
    /// </summary>
    public required double ScrollBarLargeChange { get; init; }

    /// <summary>
    /// Gets the command icon size used by context menus and menu-bar dropdowns (in DIPs).
    /// </summary>
    public double ContextMenuIconSize { get; init; } = 16;

    /// <summary>
    /// Gets the default command icon size reserved for toolbar presenters (in DIPs).
    /// </summary>
    public double ToolBarIconSize { get; init; } = 24;
}
