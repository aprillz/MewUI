using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A slider control for selecting a numeric value within a range.
/// </summary>
public sealed class Slider : RangeBase
{
    private bool _isDragging;

    public static readonly MewProperty<double> SmallChangeProperty =
        MewProperty<double>.Register<Slider>(nameof(SmallChange), 1.0, MewPropertyOptions.None);

    public static readonly MewProperty<bool> ChangeOnWheelProperty =
        MewProperty<bool>.Register<Slider>(nameof(ChangeOnWheel), true, MewPropertyOptions.None);

    static Slider()
    {
        MaximumProperty.OverrideDefaultValue<Slider>(100.0);
        HeightProperty.OverrideDefaultValue<Slider>(24.0);
    }

    /// <summary>
    /// Gets or sets the increment for small changes.
    /// </summary>
    public double SmallChange
    {
        get => GetValue(SmallChangeProperty);
        set => SetValue(SmallChangeProperty, value);
    }

    public bool ChangeOnWheel
    {
        get => GetValue(ChangeOnWheelProperty);
        set => SetValue(ChangeOnWheelProperty, value);
    }

    /// <summary>
    /// Gets whether the slider can receive keyboard focus.
    /// </summary>
    public override bool Focusable => true;

    private double ThumbSize => 14;

    protected override Size MeasureContent(Size availableSize) => new Size(ThumbSize * 3, ThumbSize);

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = Bounds;
        var contentBounds = bounds.Deflate(Padding);
        var state = CurrentVisualState;

        // Track
        double trackHeight = 4;
        double trackY = contentBounds.Y + (contentBounds.Height - trackHeight) / 2;
        var trackRect = new Rect(contentBounds.X, trackY, contentBounds.Width, trackHeight);

        var trackBg = state.IsEnabled
            ? Theme.Palette.ControlBackground.Lerp(Theme.Palette.WindowText, 0.12)
            : Theme.Palette.DisabledControlBackground;

        context.FillRoundedRectangle(trackRect, 2, 2, trackBg);

        if (state.IsEnabled && Theme.Metrics.ControlBorderThickness > 0)
        {
            var trackBorder = trackBg.Lerp(Theme.Palette.WindowText, 0.12);
            context.DrawRoundedRectangle(trackRect, 2, 2, trackBorder, Theme.Metrics.ControlBorderThickness, strokeInset: true);
        }

        // Filled track
        double t = GetNormalizedValue();
        var fillRect = new Rect(trackRect.X, trackRect.Y, trackRect.Width * t, trackRect.Height);
        if (fillRect.Width > 0)
        {
            var fillColor = state.IsEnabled ? Theme.Palette.Accent : Theme.Palette.DisabledAccent;
            context.FillRoundedRectangle(fillRect, 2, 2, fillColor);
        }

        // Thumb
        double thumbX = trackRect.X + trackRect.Width * t - ThumbSize / 2;
        thumbX = Math.Clamp(thumbX, contentBounds.X - ThumbSize / 2, contentBounds.Right - ThumbSize / 2);

        double thumbY = contentBounds.Y + (contentBounds.Height - ThumbSize) / 2;
        var thumbRect = new Rect(thumbX, thumbY, ThumbSize, ThumbSize);

        var thumbFill = PickControlBackground(state, Theme.Palette.ControlBackground);

        context.FillEllipse(thumbRect, thumbFill);

        var thumbFlags = state.Flags;
        if (_isDragging) thumbFlags |= Styling.VisualStateFlags.Pressed;
        var thumbState = new VisualState { Flags = thumbFlags };
        Color thumbBorder = PickAccentBorder(Theme, BorderBrush, thumbState, 0.6);

        if (BorderThickness > 0)
        {
            context.DrawEllipse(thumbRect, thumbBorder, BorderThickness, strokeInset: true);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEffectivelyEnabled || e.Button != MouseButton.Left)
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

        if (!IsEffectivelyEnabled || !_isDragging || !IsMouseCaptured || !e.LeftButton)
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

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!IsEffectivelyEnabled || !ChangeOnWheel)
        {
            return;
        }

        double step = GetKeyboardSmallStep();
        SetValueInternal(Value + (e.Delta > 0 ? step : -step), true);
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || !IsEffectivelyEnabled)
        {
            return;
        }

        double step = GetKeyboardSmallStep();
        double largeStep = GetKeyboardLargeStep(step);

        if (e.Key is Key.Left or Key.Down)
        {
            SetValueInternal(Value - step, true);
            e.Handled = true;
        }
        else if (e.Key is Key.Right or Key.Up)
        {
            SetValueInternal(Value + step, true);
            e.Handled = true;
        }
        else if (e.Key == Key.PageDown)
        {
            SetValueInternal(Value - largeStep, true);
            e.Handled = true;
        }
        else if (e.Key == Key.PageUp)
        {
            SetValueInternal(Value + largeStep, true);
            e.Handled = true;
        }
        else if (e.Key == Key.Home)
        {
            SetValueInternal(Minimum, true);
            e.Handled = true;
        }
        else if (e.Key == Key.End)
        {
            SetValueInternal(Maximum, true);
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
        SetValueInternal(value, true);
    }

    private void SetValueInternal(double value, bool fromInput)
    {
        double clamped = ClampToRange(value);
        if (Value.Equals(clamped))
        {
            return;
        }

        Value = clamped;
    }

    protected override void OnDispose()
    {
        base.OnDispose();
    }
}
