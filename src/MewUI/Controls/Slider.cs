using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A slider control for selecting a numeric value within a range.
/// </summary>
public sealed partial class Slider : RangeBase
{
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<Slider>(DefaultStyles.CreateSliderStyle);

    private bool _isDragging;

    public static readonly MewProperty<Color> ThumbBrushProperty =
        MewProperty<Color>.Register<Slider>(nameof(ThumbBrush), default, MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<Color> ThumbBorderBrushProperty =
        MewProperty<Color>.Register<Slider>(nameof(ThumbBorderBrush), default, MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<bool> ChangeOnWheelProperty =
        MewProperty<bool>.Register<Slider>(nameof(ChangeOnWheel), true, MewPropertyOptions.None);

    public static readonly MewProperty<Orientation> OrientationProperty =
        MewProperty<Orientation>.Register<Slider>(nameof(Orientation), Orientation.Horizontal,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender);

    // Extent across the track while the matching Width or Height is left unset.
    private const double DEFAULT_THICKNESS = 24;

    private WheelNotchAccumulator _wheelAccumulator;

    static Slider()
    {
        MaximumProperty.OverrideDefaultValue<Slider>(100.0);
        SmallChangeProperty.OverrideDefaultValue<Slider>(1.0);
        LargeChangeProperty.OverrideDefaultValue<Slider>(10.0);
        FocusableProperty.OverrideDefaultValue<Slider>(true);
    }

    /// <summary>
    /// Gets or sets the direction of the track; a vertical slider has its minimum at the bottom.
    /// </summary>
    /// <remarks>
    /// While the extent across the track is unset it defaults to 24 DIPs (Height when horizontal,
    /// Width when vertical).
    /// </remarks>
    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    internal override double LayoutWidth
        => Orientation == Orientation.Vertical && !PropertyStore.HasOwnValue(WidthProperty.Id) ? DEFAULT_THICKNESS : Width;

    internal override double LayoutHeight
        => Orientation == Orientation.Horizontal && !PropertyStore.HasOwnValue(HeightProperty.Id) ? DEFAULT_THICKNESS : Height;

    /// <summary>
    /// Gets or sets whether the mouse wheel changes the value by
    /// <see cref="RangeBase.SmallChange"/> per notch while the pointer is over the slider.
    /// </summary>
    public bool ChangeOnWheel
    {
        get => GetValue(ChangeOnWheelProperty);
        set => SetValue(ChangeOnWheelProperty, value);
    }

    /// <summary>Gets or sets the thumb color.</summary>
    public Color ThumbBrush
    {
        get => GetValue(ThumbBrushProperty);
        set => SetValue(ThumbBrushProperty, value);
    }

    /// <summary>Gets or sets the thumb border color.</summary>
    public Color ThumbBorderBrush
    {
        get => GetValue(ThumbBorderBrushProperty);
        set => SetValue(ThumbBorderBrushProperty, value);
    }

    private double ThumbSize => 14;

    protected override Size MeasureContent(Size availableSize)
    {
        if (Orientation == Orientation.Vertical)
        {
            return new Size(ThumbSize, ThumbSize * 3);
        }
        else
        {
            return new Size(ThumbSize * 3, ThumbSize);
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = Bounds;
        var contentBounds = bounds.Deflate(Padding);
        var state = CurrentVisualState;
        double fraction = GetNormalizedValue();

        // Track, inset half a thumb on both ends so the thumb at either extreme stays inside
        // the control bounds; ink past them is cut by any caching or clipping ancestor.
        const double TRACK_THICKNESS = 4;
        Rect trackRect;
        Rect fillRect;
        Rect thumbRect;
        if (Orientation == Orientation.Vertical)
        {
            trackRect = new Rect(
                contentBounds.X + (contentBounds.Width - TRACK_THICKNESS) / 2,
                contentBounds.Y + ThumbSize / 2,
                TRACK_THICKNESS,
                Math.Max(0, contentBounds.Height - ThumbSize));

            double fillHeight = trackRect.Height * fraction;
            fillRect = new Rect(trackRect.X, trackRect.Bottom - fillHeight, trackRect.Width, fillHeight);

            double thumbY = trackRect.Bottom - fillHeight - ThumbSize / 2;
            thumbY = Math.Clamp(thumbY, contentBounds.Y, Math.Max(contentBounds.Y, contentBounds.Bottom - ThumbSize));
            double thumbX = contentBounds.X + (contentBounds.Width - ThumbSize) / 2;
            thumbRect = new Rect(thumbX, thumbY, ThumbSize, ThumbSize);
        }
        else
        {
            trackRect = new Rect(
                contentBounds.X + ThumbSize / 2,
                contentBounds.Y + (contentBounds.Height - TRACK_THICKNESS) / 2,
                Math.Max(0, contentBounds.Width - ThumbSize),
                TRACK_THICKNESS);

            fillRect = new Rect(trackRect.X, trackRect.Y, trackRect.Width * fraction, trackRect.Height);

            double thumbX = trackRect.X + trackRect.Width * fraction - ThumbSize / 2;
            thumbX = Math.Clamp(thumbX, contentBounds.X, Math.Max(contentBounds.X, contentBounds.Right - ThumbSize));
            double thumbY = contentBounds.Y + (contentBounds.Height - ThumbSize) / 2;
            thumbRect = new Rect(thumbX, thumbY, ThumbSize, ThumbSize);
        }

        var trackBg = Background;

        context.FillRoundedRectangle(trackRect, 2, 2, trackBg);

        if (Theme.Metrics.ControlBorderThickness > 0)
        {
            var trackBorder = BorderBrush;
            context.DrawRoundedRectangle(trackRect, 2, 2, trackBorder, Theme.Metrics.ControlBorderThickness, strokeInset: true);
        }

        if (fillRect.Width > 0 && fillRect.Height > 0)
        {
            var fillColor = state.IsEnabled ? Theme.Palette.Accent : Theme.Palette.DisabledAccent;
            context.FillRoundedRectangle(fillRect, 2, 2, fillColor);
        }

        var thumbFill = ThumbBrush;

        context.FillEllipse(thumbRect.Deflate(BorderThickness / 2.0), thumbFill);
        var thumbBorder = ThumbBorderBrush;

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
        SetValueFromPosition(TransformFromRoot(e.Position));

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.CaptureMouse(this);
        }

        // Only a held capture delivers the release that ends the drag.
        _isDragging = IsMouseCaptured;
        SetPressed(_isDragging);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!IsEffectivelyEnabled || !_isDragging || !IsMouseCaptured || !e.LeftButton)
        {
            return;
        }

        SetValueFromPosition(TransformFromRoot(e.Position));
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
        SetPressed(false);

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.ReleaseMouseCapture();
        }

        e.Handled = true;
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);

        // A capture taken away never delivers the release that would end the drag.
        if (property == IsMouseCapturedProperty && !IsMouseCaptured && _isDragging)
        {
            _isDragging = false;
            SetPressed(false);
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || !IsEffectivelyEnabled || e.Delta.Y == 0 || !ChangeOnWheel)
        {
            return;
        }

        int notches = _wheelAccumulator.TakeY(e.Delta.Y);
        if (notches != 0)
        {
            SetValueInternal(Value + notches * GetKeyboardSmallStep(), true);
        }

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
        if (LargeChange > 0 && double.IsFinite(LargeChange))
        {
            return LargeChange;
        }

        double range = Math.Abs(Maximum - Minimum);
        if (range > 0)
        {
            return Math.Max(smallStep * 10, range / 10.0);
        }

        return smallStep * 10;
    }

    private void SetValueFromPosition(Point position)
    {
        // Mirrors the render-side track inset so a click lands on the value under the pointer.
        var contentBounds = Bounds.Deflate(Padding);
        double fraction;
        if (Orientation == Orientation.Vertical)
        {
            double bottom = contentBounds.Bottom - ThumbSize / 2;
            double height = Math.Max(1e-6, contentBounds.Height - ThumbSize);
            fraction = Math.Clamp((bottom - position.Y) / height, 0, 1);
        }
        else
        {
            double left = contentBounds.X + ThumbSize / 2;
            double width = Math.Max(1e-6, contentBounds.Width - ThumbSize);
            fraction = Math.Clamp((position.X - left) / width, 0, 1);
        }

        double range = Maximum - Minimum;
        double value = range <= 0 ? Minimum : Minimum + fraction * range;
        SetValueInternal(value, true);
    }

    private void SetValueInternal(double value, bool fromInput)
    {
        double clamped = ClampToRange(value);
        if (Value.Equals(clamped))
        {
            return;
        }

        if (fromInput)
            CommitValue(clamped);
        else
            Value = clamped;
    }

    protected override void OnDispose()
    {
        base.OnDispose();
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        if (!_isDragging)
        {
            SetPressed(false);
        }
    }
}
