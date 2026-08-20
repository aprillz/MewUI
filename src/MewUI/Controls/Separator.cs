using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A rule that divides the elements around it. One device pixel thick, drawn in the theme's control
/// border colour.
/// </summary>
public sealed class Separator : FrameworkElement
{
    // Clear space on either side of the rule, measured along the run it divides.
    private const double GAP = 3;

    // How far the rule stops short of each end of its slot.
    private const double INSET = 2;

    /// <summary>
    /// Direction of the run this rule divides, as in <see cref="SplitPanel.Orientation"/>:
    /// <see cref="Orientation.Horizontal"/> divides a left-to-right run and so draws a vertical rule.
    /// </summary>
    public static readonly MewProperty<Orientation> OrientationProperty =
        MewProperty<Orientation>.Register<Separator>(nameof(Orientation), Orientation.Horizontal,
            MewPropertyOptions.AffectsLayout);

    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        // Only the axis the rule sits across is asked for; the other one stretches to the slot.
        double across = (GAP * 2) + GetSnappedThickness();
        return Orientation == Orientation.Horizontal ? new Size(across, 0) : new Size(0, across);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        double dpiScale = GetDpi() / 96.0;
        double thickness = GetSnappedThickness();
        var color = Theme.Palette.ControlBorder;

        if (Orientation == Orientation.Horizontal)
        {
            double x = LayoutRounding.RoundToPixel(Bounds.X + ((Bounds.Width - thickness) / 2), dpiScale);
            context.FillRectangle(
                new Rect(x, Bounds.Y + INSET, thickness, Math.Max(0, Bounds.Height - (INSET * 2))),
                color);
        }
        else
        {
            double y = LayoutRounding.RoundToPixel(Bounds.Y + ((Bounds.Height - thickness) / 2), dpiScale);
            context.FillRectangle(
                new Rect(Bounds.X + INSET, y, Math.Max(0, Bounds.Width - (INSET * 2)), thickness),
                color);
        }
    }

    // Whole device pixels, so the rule has the same weight wherever it lands.
    private double GetSnappedThickness()
        => LayoutRounding.SnapThicknessToPixels(Theme.Metrics.ControlBorderThickness, GetDpi() / 96.0, 1);
}
