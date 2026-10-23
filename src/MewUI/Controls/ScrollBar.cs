using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;


namespace Aprillz.MewUI.Controls;

/// <summary>
/// A scrollbar control used to represent and change a scroll offset.
/// </summary>
public sealed partial class ScrollBar : RangeBase
{
    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<ScrollBar>(DefaultStyles.CreateScrollBarStyle);

    /// <summary>
    /// The optional track part of a template. The control pushes its range, value, viewport and orientation into
    /// it, pages on a press in its regions and maps its thumb's drags to the value.
    /// </summary>
    public const string PART_TRACK = "PART_Track";

    private static readonly MewProperty<bool> IsDraggingProperty =
        MewProperty<bool>.Register<ScrollBar>(nameof(IsDragging), false,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsVisualState);

    public static readonly MewProperty<Orientation> OrientationProperty =
        MewProperty<Orientation>.Register<ScrollBar>(nameof(Orientation), Orientation.Vertical, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<double> ViewportSizeProperty =
        MewProperty<double>.Register<ScrollBar>(nameof(ViewportSize), 0.0, MewPropertyOptions.AffectsRender);

    static ScrollBar()
    {
        SmallChangeProperty.OverrideDefaultValue<ScrollBar>(24.0);
        LargeChangeProperty.OverrideDefaultValue<ScrollBar>(120.0);
    }

    private double _dragStartPos;
    private double _dragStartValue;
    private WheelNotchAccumulator _wheelAccumulator;

    private Track? _trackPart;
    private Thumb? _thumbPart;
    private RepeatButton? _decreasePart;
    private RepeatButton? _increasePart;
    private double _thumbDragStartValue;

    /// <summary>
    /// Gets or sets the multiplier on the rendered thumb alpha (0..1); layout and hit testing are
    /// unaffected.
    /// </summary>
    /// <remarks>
    /// An owner uses it to fade the bar in and out.
    /// </remarks>
    [FluentExcluded("Render-internal value")]
    public double RenderOpacity
    {
        get => _renderOpacity;
        set
        {
            if (_renderOpacity != value)
            {
                // The owner that fades the bar repaints itself, not the bar, and the bar is drawn from
                // its own recording.
                _renderOpacity = value;
                InvalidateVisual();
            }
        }
    }

    private double _renderOpacity = 1.0;

    private bool IsDragging
    {
        get => GetValue(IsDraggingProperty);
        set => SetValue(IsDraggingProperty, value);
    }

    /// <summary>
    /// Gets or sets the scroll direction (vertical or horizontal).
    /// </summary>
    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Gets or sets the viewport size in DIPs, which sizes the thumb relative to the scroll range.</summary>
    public double ViewportSize
    {
        get => GetValue(ViewportSizeProperty);
        set => SetValue(ViewportSizeProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollBar"/> class.
    /// </summary>
    public ScrollBar()
    {
        Cursor = CursorType.Arrow;
    }

    /// <summary>Called when this bar stops holding the mouse capture, so its owner can refresh state it kept for the drag.</summary>
    internal Action? CaptureEnded { get; set; }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);

        if (property == IsMouseCapturedProperty && !IsMouseCaptured)
        {
            CaptureEnded?.Invoke();
        }
    }

    protected override VisualState ComputeVisualState()
    {
        var state = base.ComputeVisualState();
        if (IsDragging)
        {
            state = new VisualState { Flags = state.Flags | VisualStateFlags.Pressed };
        }

        return state;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (HasTemplateInstance)
        {
            return base.MeasureContent(availableSize);
        }

        var thickness = Theme.Metrics.ScrollBarHitThickness;
        return Orientation == Orientation.Vertical
            ? new Size(thickness, availableSize.Height)
            : new Size(availableSize.Width, thickness);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // A template owns the control's entire visuals; the built-in thumb would double-render.
        if (HasTemplateInstance || !IsEffectivelyEnabled)
        {
            return;
        }

        var bounds = Bounds;

        double opacity = Math.Clamp(RenderOpacity, 0, 1);
        if (opacity <= 0)
        {
            return;
        }

        var track = GetTrackRect(bounds, Theme);
        var thumb = ComputeLayout(track).ThumbRect;
        var thumbColor = GetValue(BackgroundProperty);
        if (opacity < 1)
        {
            thumbColor = thumbColor.WithAlpha((byte)Math.Round(thumbColor.A * opacity));
        }

        if (thumb.Width > 0 && thumb.Height > 0 && thumbColor.A > 0)
        {
            double radius = Theme.Metrics.ScrollBarThickness / 2;
            context.FillRoundedRectangle(thumb, radius, radius, thumbColor);
        }
    }

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
        context.Bind(_trackPart, Track.ViewportSizeProperty, ViewportSizeProperty);
        context.Bind(_trackPart, Track.OrientationProperty, OrientationProperty);

        _thumbPart = _trackPart.Thumb;
        if (_thumbPart != null)
        {
            _thumbPart.DragStarted += OnThumbDragStarted;
            _thumbPart.DragDelta += OnThumbDragDelta;
        }

        _decreasePart = _trackPart.DecreaseElement as RepeatButton;
        if (_decreasePart != null)
        {
            _decreasePart.Click += OnDecreaseClick;
        }

        _increasePart = _trackPart.IncreaseElement as RepeatButton;
        if (_increasePart != null)
        {
            _increasePart.Click += OnIncreaseClick;
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

        if (_decreasePart != null)
        {
            _decreasePart.Click -= OnDecreaseClick;
            _decreasePart = null;
        }

        if (_increasePart != null)
        {
            _increasePart.Click -= OnIncreaseClick;
            _increasePart = null;
        }

        _trackPart = null;
    }

    private void OnThumbDragStarted() => _thumbDragStartValue = Value;

    private void OnThumbDragDelta(Vector delta)
    {
        if (_trackPart == null)
        {
            return;
        }

        double distance = Orientation == Orientation.Vertical ? delta.Y : delta.X;
        CommitValue(_thumbDragStartValue + _trackPart.ValueFromDistance(distance));
    }

    private void OnDecreaseClick() => CommitValue(Value - LargeChange);

    private void OnIncreaseClick() => CommitValue(Value + LargeChange);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEffectivelyEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        if (HasTemplateInstance)
        {
            // The thumb handles its own press and a repeat button its own click; a press on a plain
            // paging region bubbles up to here.
            if (_trackPart == null || e.Handled)
            {
                return;
            }

            PageToward(_trackPart.ValueFromPoint(e.Position));
            e.Handled = true;
            return;
        }

        var track = GetTrackRect(Bounds, Theme);
        var layout = ComputeLayout(track);
        var thumbHit = GetThumbHitRect(layout.ThumbRect, Bounds);

        var position = e.Position;
        double pos = Orientation == Orientation.Vertical ? position.Y : position.X;

        if (thumbHit.Contains(position))
        {
            IsDragging = true;
            _dragStartPos = pos;
            _dragStartValue = Value;

            var root = FindVisualRoot();
            if (root is Window window)
            {
                window.CaptureMouse(this);
            }

            e.Handled = true;
            return;
        }

        PageToward(layout.ValueFromPoint(position));
        e.Handled = true;
    }

    private void PageToward(double targetValue)
    {
        if (targetValue < Value)
        {
            CommitValue(Value - LargeChange);
        }
        else
        {
            CommitValue(Value + LargeChange);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (HasTemplateInstance || !IsEffectivelyEnabled || !IsDragging || !IsMouseCaptured || !e.LeftButton)
        {
            return;
        }

        var layout = ComputeLayout(GetTrackRect(Bounds, Theme));
        var position = e.Position;
        double pos = Orientation == Orientation.Vertical ? position.Y : position.X;

        CommitValue(_dragStartValue + layout.ValueFromDistance(pos - _dragStartPos));
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButton.Left)
        {
            return;
        }

        if (IsDragging)
        {
            IsDragging = false;
            var root = FindVisualRoot();
            if (root is Window window)
            {
                window.ReleaseMouseCapture();
            }

            e.Handled = true;
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (!IsEffectivelyEnabled || e.Handled)
        {
            return;
        }

        // ScrollBar honours its axis: vertical bar reads Y, horizontal reads X.
        double axisDelta = Orientation == Orientation.Vertical ? e.Delta.Y : e.Delta.X;
        int notches = _wheelAccumulator.TakeY(axisDelta);
        if (notches == 0)
        {
            e.Handled = true;
            return;
        }

        CommitValue(Value - notches * SmallChange);
        e.Handled = true;
    }

    private RangeTrackLayout ComputeLayout(Rect track)
        => RangeTrackLayout.Compute(
            track, Orientation, isDirectionReversed: false, Minimum, Maximum, Value, ViewportSize,
            thumbLength: double.NaN, Math.Max(8, Theme.Metrics.ScrollBarMinThumbLength));

    private Rect GetTrackRect(Rect bounds, Theme theme)
    {
        // The control Bounds represent the hit test region ("A").
        // The actual visible track/thumb ("ScrollBarThickness") is centered inside it.
        double hit = Orientation == Orientation.Vertical ? bounds.Width : bounds.Height;
        double visual = Math.Max(0, theme.Metrics.ScrollBarThickness);
        if (visual <= 0 || hit <= 0)
        {
            return bounds;
        }

        var dpiScale = GetDpi() / 96.0;
        visual = LayoutRounding.RoundToPixel(Math.Min(visual, hit), dpiScale);
        double pad = Math.Max(0, (hit - visual) / 2);

        if (Orientation == Orientation.Vertical)
        {
            double x = bounds.X + pad;
            double y = bounds.Y + pad;
            double h = Math.Max(0, bounds.Height - pad * 2);
            return LayoutRounding.SnapBoundsRectToPixels(new Rect(x, y, visual, h), dpiScale);
        }

        double y0 = bounds.Y + pad;
        double x0 = bounds.X + pad;
        double w = Math.Max(0, bounds.Width - pad * 2);
        return LayoutRounding.SnapBoundsRectToPixels(new Rect(x0, y0, w, visual), dpiScale);
    }

    private Rect GetThumbHitRect(Rect thumbVisual, Rect hitBounds)
    {
        // Use the full hit-test thickness ("A") for grabbing the thumb,
        // while keeping the visible thumb thickness thin.
        if (Orientation == Orientation.Vertical)
        {
            return new Rect(hitBounds.X, thumbVisual.Y, hitBounds.Width, thumbVisual.Height);
        }

        return new Rect(thumbVisual.X, hitBounds.Y, thumbVisual.Width, hitBounds.Height);
    }
}
