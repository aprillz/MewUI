using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public sealed class Slider : RangeBase
{
    private bool _isDragging;
    private ValueBinding<double>? _valueBinding;

    public double SmallChange { get; set; } = 1;

    protected override Color DefaultBorderBrush => Theme.Current.Palette.ControlBorder;

    public override bool Focusable => true;

    public Slider()
    {
        Maximum = 100;
        Background = Color.Transparent;
        BorderThickness = 1;
        Height = 24;
    }

    public void SetValueBinding(
        Func<double> get,
        Action<double> set,
        Action<Action>? subscribe = null,
        Action<Action>? unsubscribe = null)
    {
        _valueBinding?.Dispose();
        _valueBinding = new ValueBinding<double>(
            get,
            set,
            subscribe,
            unsubscribe,
            onSourceChanged: () =>
            {
                if (_isDragging)
                {
                    return;
                }

                SetValueFromSource(get());
            });

        SetValueFromSource(get());
    }

    protected override Size MeasureContent(Size availableSize) => new Size(160, Height);

    protected override void OnRender(IGraphicsContext context)
    {
        var theme = GetTheme();

        if (_valueBinding != null && !_isDragging)
        {
            SetValueFromSource(_valueBinding.Get());
        }

        var bounds = Bounds;
        var contentBounds = bounds.Deflate(Padding);

        // Track
        double trackHeight = 4;
        double trackY = contentBounds.Y + (contentBounds.Height - trackHeight) / 2;
        var trackRect = new Rect(contentBounds.X, trackY, contentBounds.Width, trackHeight);

        var trackBg = IsEnabled
            ? theme.Palette.ControlBackground.Lerp(theme.Palette.WindowText, 0.12)
            : theme.Palette.DisabledControlBackground;

        context.FillRoundedRectangle(trackRect, 2, 2, trackBg);
        if (IsEnabled)
        {
            var trackBorder = trackBg.Lerp(theme.Palette.WindowText, 0.12);
            context.DrawRoundedRectangle(trackRect, 2, 2, trackBorder, 1);
        }

        // Filled track
        double t = GetNormalizedValue();
        var fillRect = new Rect(trackRect.X, trackRect.Y, trackRect.Width * t, trackRect.Height);
        if (fillRect.Width > 0)
        {
            context.FillRoundedRectangle(fillRect, 2, 2, theme.Palette.Accent);
        }

        // Thumb
        double thumbSize = 14;
        double thumbX = trackRect.X + trackRect.Width * t - thumbSize / 2;
        thumbX = Math.Clamp(thumbX, contentBounds.X - thumbSize / 2, contentBounds.Right - thumbSize / 2);

        double thumbY = contentBounds.Y + (contentBounds.Height - thumbSize) / 2;
        var thumbRect = new Rect(thumbX, thumbY, thumbSize, thumbSize);

        var thumbFill = IsEnabled ? theme.Palette.ControlBackground : theme.Palette.DisabledControlBackground;

        context.FillEllipse(thumbRect, thumbFill);

        var state = GetVisualState(IsFocused, IsFocused);
        Color thumbBorder = PickAccentBorder(theme, BorderBrush, state, hoverMix: 0.6);


        context.DrawEllipse(thumbRect, thumbBorder, 1);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        Focus();
        _isDragging = true;
        SetValueFromPosition(e.Position.X);

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.CaptureMouse(this);
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!IsEnabled || !_isDragging || !IsMouseCaptured || !e.LeftButton)
        {
            return;
        }

        SetValueFromPosition(e.Position.X);
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButton.Left || !_isDragging)
        {
            return;
        }

        _isDragging = false;

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.ReleaseMouseCapture();
        }

        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || !IsEnabled)
        {
            return;
        }

        double step = GetKeyboardSmallStep();
        double largeStep = GetKeyboardLargeStep(step);

        if (e.Key is Key.Left or Key.Down)
        {
            SetValueInternal(Value - step, fromUser: true);
            e.Handled = true;
        }
        else if (e.Key is Key.Right or Key.Up)
        {
            SetValueInternal(Value + step, fromUser: true);
            e.Handled = true;
        }
        else if (e.Key == Key.PageDown)
        {
            SetValueInternal(Value - largeStep, fromUser: true);
            e.Handled = true;
        }
        else if (e.Key == Key.PageUp)
        {
            SetValueInternal(Value + largeStep, fromUser: true);
            e.Handled = true;
        }
        else if (e.Key == Key.Home)
        {
            SetValueInternal(Minimum, fromUser: true);
            e.Handled = true;
        }
        else if (e.Key == Key.End)
        {
            SetValueInternal(Maximum, fromUser: true);
            e.Handled = true;
        }
    }

    private double GetKeyboardSmallStep()
    {
        if (SmallChange > 0 && !double.IsNaN(SmallChange) && !double.IsInfinity(SmallChange))
        {
            return SmallChange;
        }

        double range = Math.Abs(Maximum - Minimum);
        if (range > 0)
        {
            return range / 100.0;
        }

        return 1;
    }

    private double GetKeyboardLargeStep(double smallStep)
    {
        double range = Math.Abs(Maximum - Minimum);
        if (range > 0)
        {
            return Math.Max(smallStep * 10, range / 10.0);
        }

        return smallStep * 10;
    }

    private void SetValueFromPosition(double x)
    {
        var contentBounds = Bounds.Deflate(Padding);
        double left = contentBounds.X;
        double width = Math.Max(1e-6, contentBounds.Width);
        double t = Math.Clamp((x - left) / width, 0, 1);
        double range = Maximum - Minimum;
        double value = range <= 0 ? Minimum : Minimum + t * range;
        SetValueInternal(value, fromUser: true);
    }

    private void SetValueInternal(double value, bool fromUser)
    {
        double clamped = ClampToRange(value);
        if (Value.Equals(clamped))
        {
            return;
        }

        Value = clamped;

        if (fromUser && _valueBinding != null)
        {
            _valueBinding.Set(clamped);
        }
    }

    protected override void OnDispose()
    {
        _valueBinding?.Dispose();
        _valueBinding = null;
    }
}
