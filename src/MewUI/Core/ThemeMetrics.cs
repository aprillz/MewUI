namespace Aprillz.MewUI;

/// <summary>
/// Defines common layout and typography metrics used by themes and controls.
/// </summary>
public sealed record class ThemeMetrics
{
    /// <summary>
    /// Gets the default theme metrics.
    /// </summary>
    public static ThemeMetrics Default { get; } = new ThemeMetrics
    {
        BaseControlHeight = 28,
        ControlCornerRadius = 4,
        ControlBorderThickness = 1,
        ItemPadding = new Thickness(8, 2, 8, 2),
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
    /// Gets the default padding for list items (in DIPs).
    /// </summary>
    public required Thickness ItemPadding { get; init; }

    /// <summary>
    /// Gets the default font family name.
    /// </summary>
    public required string FontFamily { get; init; }

    /// <summary>
    /// Gets the default font size (in DIPs).
    /// </summary>
    public required double FontSize { get; init; }

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
}
