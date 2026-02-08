using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A progress bar control for displaying completion percentage.
/// </summary>
public sealed partial class ProgressBar : RangeBase
{

    /// <summary>
    /// Gets the default background color.
    /// </summary>
    protected override Color DefaultBackground => Theme.Palette.ControlBackground;

    /// <summary>
    /// Gets the default border brush color.
    /// </summary>
    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;

    /// <summary>
    /// Initializes a new instance of the ProgressBar class.
    /// </summary>
    public ProgressBar()
    {
        Maximum = 100;
        BorderThickness = 1;
        Padding = new Thickness(1);
        Height = 10;
    }

    /// <summary>
    /// Sets a one-way binding for the Value property.
    /// </summary>
    /// <param name="get">Function to get the current value.</param>
    /// <param name="subscribe">Optional action to subscribe to change notifications.</param>
    /// <param name="unsubscribe">Optional action to unsubscribe from change notifications.</param>
    public void SetValueBinding(
        Func<double> get,
        Action<Action>? subscribe = null,
        Action<Action>? unsubscribe = null)
    {
        SetValueBindingCore(get, subscribe, unsubscribe);
    }

    protected override Size MeasureContent(Size availableSize) => new Size(120, Height);

    protected override void OnRender(IGraphicsContext context)
    {
        double radius = Theme.Metrics.ControlCornerRadius;

        if (TryGetBinding(ValueBindingSlot, out ValueBinding<double> valueBinding))
        {
            // Pull latest value at paint time (one-way).
            SetValueFromSource(valueBinding.Get());
        }

        var bounds = GetSnappedBorderBounds(Bounds);
        var borderInset = GetBorderVisualInset();
        var contentBounds = bounds.Deflate(Padding).Deflate(new Thickness(borderInset));

        var bg = IsEnabled ? Background : Theme.Palette.DisabledControlBackground;
        DrawBackgroundAndBorder(context, bounds, bg, BorderBrush, radius);

        double t = GetNormalizedValue();

        var fillRect = new Rect(contentBounds.X, contentBounds.Y, contentBounds.Width * t, contentBounds.Height);
        if (fillRect.Width > 0)
        {
            var fillColor = IsEnabled ? Theme.Palette.Accent : Theme.Palette.DisabledAccent;
            if (radius - 1 > 0)
            {
                double rx = Math.Min(radius - 1, fillRect.Width / 2.0);
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
