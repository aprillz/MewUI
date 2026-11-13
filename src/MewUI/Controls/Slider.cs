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

    /// <summary>
    /// The optional track part of a template. The control pushes its range, value and orientation into it, moves
    /// the value to a press on it and maps its thumb's drags to the value.
    /// </summary>
    public const string PART_TRACK = "PART_Track";

    private bool _isDragging;

    private Track? _trackPart;
    private Thumb? _thumbPart;
    private double _thumbDragStartValue;

    public static readonly MewProperty<Color> FillProperty =
        MewProperty<Color>.Register<Slider>(nameof(Fill), default, MewPropertyOptions.AffectsRender);

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

    // Thickness of the drawn track; the thumb is wider so it reads as the handle.
    private const double TRACK_THICKNESS = 4;

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
    /// While the extent across the track is unset and no template is applied, it defaults to 24 DIPs
    /// (Height when horizontal, Width when vertical).
    /// </remarks>
    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    // A template sizes the slider from its own tree, so the default thickness is the built-in drawing's only.
    internal override double LayoutWidth
        => !HasTemplateInstance && Orientation == Orientation.Vertical && !PropertyStore.HasOwnValue(WidthProperty.Id) ? DEFAULT_THICKNESS : Width;

    internal override double LayoutHeight
        => !HasTemplateInstance && Orientation == Orientation.Horizontal && !PropertyStore.HasOwnValue(HeightProperty.Id) ? DEFAULT_THICKNESS : Height;

    /// <summary>
    /// Gets or sets whether the mouse wheel changes the value by
    /// <see cref="RangeBase.SmallChange"/> per notch while the pointer is over the slider.
    /// </summary>
    public bool ChangeOnWheel
    {
        get => GetValue(ChangeOnWheelProperty);
        set => SetValue(ChangeOnWheelProperty, value);
    }

    /// <summary>Gets or sets the color of the track from the minimum up to the value.</summary>
    public Color Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
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
        if (HasTemplateInstance)
        {
            return base.MeasureContent(availableSize);
        }

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
        // A template owns the control's entire visuals; the built-in track and thumb would double-render.
        if (HasTemplateInstance)
        {
            return;
        }

        var contentBounds = Bounds.Deflate(Padding);
        var layout = ComputeLayout(contentBounds);

        // The visible track runs between the thumb centers at either extreme, so the thumb stays inside the
        // control bounds; ink past them is cut by any caching or clipping ancestor.
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

            // Shorter than the thumb, the track has no length; the thumb stays at the top and nothing is filled.
            double fillHeight = Math.Min(layout.DecreaseRect.Height, trackRect.Height);
            fillRect = new Rect(trackRect.X, trackRect.Bottom - fillHeight, trackRect.Width, fillHeight);
            double thumbY = Math.Clamp(layout.ThumbRect.Y, contentBounds.Y, Math.Max(contentBounds.Y, contentBounds.Bottom - ThumbSize));
            thumbRect = new Rect(contentBounds.X + (contentBounds.Width - ThumbSize) / 2, thumbY, ThumbSize, ThumbSize);
        }
        else
        {
            trackRect = new Rect(
                contentBounds.X + ThumbSize / 2,
                contentBounds.Y + (contentBounds.Height - TRACK_THICKNESS) / 2,
                Math.Max(0, contentBounds.Width - ThumbSize),
                TRACK_THICKNESS);

            fillRect = new Rect(trackRect.X, trackRect.Y, Math.Min(layout.DecreaseRect.Width, trackRect.Width), trackRect.Height);
            double thumbX = Math.Clamp(layout.ThumbRect.X, contentBounds.X, Math.Max(contentBounds.X, contentBounds.Right - ThumbSize));
            thumbRect = new Rect(thumbX, contentBounds.Y + (contentBounds.Height - ThumbSize) / 2, ThumbSize, ThumbSize);
        }

        context.FillRoundedRectangle(trackRect, 2, 2, Background);

        if (Theme.Metrics.ControlBorderThickness > 0)
        {
            context.DrawRoundedRectangle(trackRect, 2, 2, BorderBrush, Theme.Metrics.ControlBorderThickness, strokeInset: true);
        }

        if (fillRect.Width > 0 && fillRect.Height > 0)
        {
            context.FillRoundedRectangle(fillRect, 2, 2, Fill);
        }

        context.FillEllipse(thumbRect.Deflate(BorderThickness / 2.0), ThumbBrush);

        if (BorderThickness > 0)
        {
            context.DrawEllipse(thumbRect, ThumbBorderBrush, BorderThickness, strokeInset: true);
        }
    }

    /// <summary>The built-in geometry: a fixed-size thumb travelling the content area, minimum at the bottom when vertical.</summary>
    private RangeTrackLayout ComputeLayout(Rect area)
        => RangeTrackLayout.Compute(
            area, Orientation, isDirectionReversed: Orientation == Orientation.Vertical, Minimum, Maximum, Value,
            viewportSize: double.NaN, ThumbSize, minThumbLength: 0);

    private protected override void OnTemplateInstanceAttached()
    {
        base.OnTemplateInstanceAttached();

        _trackPart = GetTemplateChild<Track>(PART_TRACK);
        if (_trackPart == null)
        {
            return;
        }

        var context = TemplateContext!;
        context.Bind(_trackPart, Track.MinimumProperty, MinimumProperty);
        context.Bind(_trackPart, Track.MaximumProperty, MaximumProperty);
        context.Bind(_trackPart, Track.ValueProperty, ValueProperty);
        context.Bind(_trackPart, Track.OrientationProperty, OrientationProperty);
        context.Bind(_trackPart, Track.IsDirectionReversedProperty, OrientationProperty,
            static (Orientation orientation) => orientation == Orientation.Vertical);

        _thumbPart = _trackPart.Thumb;
        if (_thumbPart != null)
        {
            _thumbPart.DragStarted += OnThumbDragStarted;
            _thumbPart.DragDelta += OnThumbDragDelta;
        }
    }

    private protected override void OnTemplateInstanceDetached()
    {
        base.OnTemplateInstanceDetached();

        if (_thumbPart != null)
        {
            _thumbPart.DragStarted -= OnThumbDragStarted;
            _thumbPart.DragDelta -= OnThumbDragDelta;
            _thumbPart = null;
        }

        _trackPart = null;
    }

    private void OnThumbDragStarted()
    {
        Focus();
        _thumbDragStartValue = Value;
    }

    private void OnThumbDragDelta(Vector delta)
    {
        if (_trackPart == null || !IsEffectivelyEnabled)
        {
            return;
        }

        double distance = Orientation == Orientation.Vertical ? delta.Y : delta.X;
        SetValueInternal(_thumbDragStartValue + _trackPart.ValueFromDistance(distance), true);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEffectivelyEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        Focus();

        // The thumb part handles its own press; without a track part a press only takes focus.
        if (HasTemplateInstance && (e.Handled || _trackPart == null))
        {
            return;
        }

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
        double value = _trackPart != null
            ? _trackPart.ValueFromPoint(position)
            : ComputeLayout(Bounds.Deflate(Padding)).ValueFromPoint(position);
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
