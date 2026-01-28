namespace Aprillz.MewUI;

public sealed record class ThemeMetrics
{
    public static ThemeMetrics Default { get; } = new ThemeMetrics
    {
        BaseControlHeight = 28,
        ControlCornerRadius = 4,
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

    public required double BaseControlHeight { get; init; }

    public required double ControlCornerRadius { get; init; }

    public required Thickness ItemPadding { get; init; }

    public required string FontFamily { get; init; }

    public required double FontSize { get; init; }

    public required FontWeight FontWeight { get; init; }

    public required double ScrollBarThickness { get; init; }

    public required double ScrollBarHitThickness { get; init; }

    public required double ScrollBarMinThumbLength { get; init; }

    public required double ScrollWheelStep { get; init; }

    public required double ScrollBarSmallChange { get; init; }

    public required double ScrollBarLargeChange { get; init; }
}
