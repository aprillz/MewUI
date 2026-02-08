using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A tooltip popup control for displaying help text.
/// </summary>
public sealed class ToolTip : Control
{
    /// <summary>
    /// Gets or sets the tooltip text.
    /// </summary>
    public string Text
    {
        get;
        set
        {
            value ??= string.Empty;
            if (field == value)
            {
                return;
            }

            field = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    } = string.Empty;

    /// <summary>
    /// Initializes a new instance of the ToolTip class.
    /// </summary>
    public ToolTip()
    {
        Padding = new Thickness(8, 4, 8, 4);
        BorderThickness = 1;
    }

    protected override Color DefaultBackground => Theme.Palette.ControlBackground;

    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;

    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;

        if (string.IsNullOrEmpty(Text))
        {
            return new Size(Padding.HorizontalThickness, Padding.VerticalThickness)
                .Inflate(border);
        }

        using var measure = BeginTextMeasurement();
        var textSize = measure.Context.MeasureText(Text, measure.Font);
        return textSize.Inflate(Padding).Inflate(border);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        
        var bounds = GetSnappedBorderBounds(Bounds);
        var dpiScale = GetDpi() / 96.0;
        double radius = LayoutRounding.RoundToPixel(Theme.Metrics.ControlCornerRadius, dpiScale);

        DrawBackgroundAndBorder(context, bounds, Background, BorderBrush, radius);

        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        var borderInset = GetBorderVisualInset();
        var contentBounds = bounds.Deflate(new Thickness(borderInset)).Deflate(Padding);
        context.DrawText(Text, contentBounds, GetFont(), Foreground,
            TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
    }
}
