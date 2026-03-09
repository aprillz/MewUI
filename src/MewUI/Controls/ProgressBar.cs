using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A progress bar control for displaying completion percentage.
/// </summary>
public sealed class ProgressBar : RangeBase
{
    static ProgressBar()
    {
        MaximumProperty.OverrideDefaultValue<ProgressBar>(100.0);
        HeightProperty.OverrideDefaultValue<ProgressBar>(10.0);
    }

    protected override Size MeasureContent(Size availableSize) => new Size(120, Height);

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        var borderInset = GetBorderVisualInset();
        var contentBounds = bounds.Deflate(Padding).Deflate(new Thickness(borderInset));
        double radius = Math.Min(bounds.Height / 2, CornerRadius);

        var bg = GetValue(BackgroundProperty);
        var border = GetValue(BorderBrushProperty);
        DrawBackgroundAndBorder(context, bounds, bg, border, radius);

        double t = GetNormalizedValue();

        var fillRect = new Rect(contentBounds.X, contentBounds.Y, contentBounds.Width * t, contentBounds.Height);
        if (fillRect.Width > 0)
        {
            var fillColor = IsEffectivelyEnabled ? Theme.Palette.Accent : Theme.Palette.DisabledAccent;
            if (radius - 1 > 0)
            {
                double rx = Math.Min(radius - 1, fillRect.Height / 2.0);
                context.FillRoundedRectangle(fillRect, rx, rx, fillColor);
            }
            else
            {
                context.FillRectangle(fillRect, fillColor);
            }
        }
    }

    protected override void OnDispose()
    {
        base.OnDispose();
    }
}
