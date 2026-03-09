using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Styling;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A scrollbar control used to represent and change a scroll offset.
/// </summary>
public sealed class ScrollBar : RangeBase
{
    private static readonly MewProperty<bool> IsDraggingProperty =
        MewProperty<bool>.Register<ScrollBar>(nameof(IsDragging), false,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<Orientation> OrientationProperty =
        MewProperty<Orientation>.Register<ScrollBar>(nameof(Orientation), Orientation.Vertical, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<double> ViewportSizeProperty =
        MewProperty<double>.Register<ScrollBar>(nameof(ViewportSize), 0.0, MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<double> SmallChangeProperty =
        MewProperty<double>.Register<ScrollBar>(nameof(SmallChange), 24.0, MewPropertyOptions.None);

    public static readonly MewProperty<double> LargeChangeProperty =
        MewProperty<double>.Register<ScrollBar>(nameof(LargeChange), 120.0, MewPropertyOptions.None);

    private double _dragStartPos;
    private double _dragStartValue;

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

    /// <summary>
    /// Gets or sets the viewport size in DIPs.
    /// Used to compute thumb size relative to the scroll range.
    /// </summary>
    public double ViewportSize
    {
        get => GetValue(ViewportSizeProperty);
        set => SetValue(ViewportSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the amount to scroll for small changes (e.g. mouse wheel).
    /// </summary>
    public double SmallChange
    {
        get => GetValue(SmallChangeProperty);
        set => SetValue(SmallChangeProperty, value);
    }

    /// <summary>
    /// Gets or sets the amount to scroll for large changes (e.g. track click).
    /// </summary>
    public double LargeChange
    {
        get => GetValue(LargeChangeProperty);
        set => SetValue(LargeChangeProperty, value);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ScrollBar"/> class.
    /// </summary>
    public ScrollBar()
    {
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
        var thickness = Theme.Metrics.ScrollBarHitThickness;
        return Orientation == Orientation.Vertical
            ? new Size(thickness, availableSize.Height)
            : new Size(availableSize.Width, thickness);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        if (!IsEffectivelyEnabled)
        {
            return;
        }

        var bounds = Bounds;

        var track = GetTrackRect(bounds, Theme);
        var thumb = GetThumbRect(track, Theme);
        var thumbColor = GetValue(BackgroundProperty);

        double radius = Theme.Metrics.ScrollBarThickness / 2;
        if (thumb.Width > 0 && thumb.Height > 0 && thumbColor.A > 0)
        {
            context.FillRoundedRectangle(thumb, radius, radius, thumbColor);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEffectivelyEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        
        var track = GetTrackRect(Bounds, Theme);
        var thumb = GetThumbRect(track, Theme);
        var thumbHit = GetThumbHitRect(thumb, Bounds);

        double pos = Orientation == Orientation.Vertical ? e.Position.Y : e.Position.X;

        if (thumbHit.Contains(e.Position))
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

        // Page up/down on track click
        var clickValue = ValueFromPosition(track, Theme, pos);
        if (clickValue < Value)
        {
            Value -= LargeChange;
        }
        else
        {
            Value += LargeChange;
        }

        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!IsEffectivelyEnabled || !IsDragging || !IsMouseCaptured || !e.LeftButton)
        {
            return;
        }

        
        var track = GetTrackRect(Bounds, Theme);
        var thumb = GetThumbRect(track, Theme);

        double pos = Orientation == Orientation.Vertical ? e.Position.Y : e.Position.X;
        double deltaPx = pos - _dragStartPos;

        double trackLength = Orientation == Orientation.Vertical ? track.Height : track.Width;
        double thumbLength = Orientation == Orientation.Vertical ? thumb.Height : thumb.Width;
        double scrollRange = GetScrollRange();

        double usable = Math.Max(1, trackLength - thumbLength);
        double deltaValue = scrollRange <= 0 ? 0 : deltaPx / usable * scrollRange;

        Value = _dragStartValue + deltaValue;
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

        // 120 is a common wheel delta unit on Windows, but we treat it as "one notch"
        int steps = Math.Sign(e.Delta);
        if (steps == 0)
        {
            return;
        }

        Value -= steps * SmallChange;
        e.Handled = true;
    }

    private double GetScrollRange()
    {
        double min = Math.Min(Minimum, Maximum);
        double max = Math.Max(Minimum, Maximum);
        return Math.Max(0, max - min);
    }

    private Rect GetThumbRect(Rect track, Theme theme)
    {
        double scrollRange = GetScrollRange();
        double viewport = Math.Max(0, ViewportSize);

        double length = Orientation == Orientation.Vertical ? track.Height : track.Width;
        double thickness = Orientation == Orientation.Vertical ? track.Width : track.Height;

        double minThumb = Math.Max(8, theme.Metrics.ScrollBarMinThumbLength);

        // When viewport is known, use ratio; otherwise default to 1/4 of track.
        double ratio = viewport > 0 && (scrollRange + viewport) > 0
            ? viewport / (scrollRange + viewport)
            : 0.25;

        double thumbLength = Math.Clamp(length * ratio, minThumb, length);
        double usable = Math.Max(1, length - thumbLength);

        double min = Math.Min(Minimum, Maximum);
        double max = Math.Max(Minimum, Maximum);
        double t = (max - min) <= 0 ? 0 : Math.Clamp((Value - min) / (max - min), 0, 1);
        double offset = usable * t;

        if (Orientation == Orientation.Vertical)
        {
            return new Rect(track.X, track.Y + offset, thickness, thumbLength);
        }

        return new Rect(track.X + offset, track.Y, thumbLength, thickness);
    }

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

    private double ValueFromPosition(Rect track, Theme theme, double pos)
    {
        var thumb = GetThumbRect(track, theme);
        double length = Orientation == Orientation.Vertical ? track.Height : track.Width;
        double thumbLength = Orientation == Orientation.Vertical ? thumb.Height : thumb.Width;
        double usable = Math.Max(1, length - thumbLength);

        double start = Orientation == Orientation.Vertical ? track.Y : track.X;
        double t = Math.Clamp((pos - start - (thumbLength / 2)) / usable, 0, 1);

        double min = Math.Min(Minimum, Maximum);
        double max = Math.Max(Minimum, Maximum);
        return min + (max - min) * t;
    }
}
