using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public sealed class ProgressBar : RangeBase
{
    private ValueBinding<double>? _valueBinding;

    protected override Color DefaultBackground => GetTheme().Palette.ControlBackground;
    protected override Color DefaultBorderBrush => GetTheme().Palette.ControlBorder;

    public ProgressBar()
    {
        Maximum = 100;
        BorderThickness = 1;
        Padding = new Thickness(1);
        Height = 10;
    }

    public void SetValueBinding(
        Func<double> get,
        Action<Action>? subscribe = null,
        Action<Action>? unsubscribe = null)
    {
        _valueBinding?.Dispose();
        _valueBinding = new ValueBinding<double>(
            get,
            null,
            subscribe,
            unsubscribe,
            () => Value = get());

        Value = get();
    }

    protected override Size MeasureContent(Size availableSize) => new Size(120, Height);

    protected override void OnRender(IGraphicsContext context)
    {
        var theme = GetTheme();
        double radius = theme.ControlCornerRadius;

        if (_valueBinding != null)
        {
            // Pull latest value at paint time (one-way).
            SetValueFromSource(_valueBinding.Get());
        }

        var bounds = GetSnappedBorderBounds(Bounds);
        var borderInset = GetBorderVisualInset();
        var contentBounds = bounds.Deflate(Padding).Deflate(new Thickness(borderInset));

        var bg = IsEnabled ? Background : theme.Palette.DisabledControlBackground;
        DrawBackgroundAndBorder(context, bounds, bg, BorderBrush, radius);

        double t = GetNormalizedValue();

        var fillRect = new Rect(contentBounds.X, contentBounds.Y, contentBounds.Width * t, contentBounds.Height);
        if (fillRect.Width > 0)
        {
            var fillColor = IsEnabled ? theme.Palette.Accent : theme.Palette.DisabledAccent;
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
        _valueBinding?.Dispose();
        _valueBinding = null;
    }
}
