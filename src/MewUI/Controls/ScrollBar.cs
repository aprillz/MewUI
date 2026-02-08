using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public sealed class ScrollBar : RangeBase
{
    private bool _dragging;
    private double _dragStartPos;
    private double _dragStartValue;

    public Orientation Orientation
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    } = Orientation.Vertical;

    public double ViewportSize
    {
        get;
        set
        {
            if (field.Equals(value))
            {
                return;
            }

            field = value;
            InvalidateVisual();
        }
    }

    public double SmallChange { get; set; } = 24;

    public double LargeChange { get; set; } = 120;

    public ScrollBar()
    {
        Background = Color.Transparent;
        BorderThickness = 0;
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
        if (!IsEnabled)
        {
            return;
        }

        
        var bounds = Bounds;

        var track = GetTrackRect(bounds, Theme);
        var thumb = GetThumbRect(track, Theme);

        var thumbColor = Theme.Palette.ScrollBarThumb;
        if (_dragging || IsMouseCaptured)
        {
            thumbColor = Theme.Palette.ScrollBarThumbActive;
        }
        else if (IsMouseOver)
        {
            thumbColor = Theme.Palette.ScrollBarThumbHover;
        }

        double radius = Math.Min(Theme.Metrics.ScrollBarThickness / 2, Theme.Metrics.ControlCornerRadius);
        if (thumb.Width > 0 && thumb.Height > 0 && thumbColor.A > 0)
        {
            context.FillRoundedRectangle(thumb, radius, radius, thumbColor);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        
        var track = GetTrackRect(Bounds, Theme);
        var thumb = GetThumbRect(track, Theme);
        var thumbHit = GetThumbHitRect(thumb, Bounds);

        double pos = Orientation == Orientation.Vertical ? e.Position.Y : e.Position.X;

        if (thumbHit.Contains(e.Position))
        {
            _dragging = true;
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

        if (!IsEnabled || !_dragging || !IsMouseCaptured || !e.LeftButton)
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

        if (_dragging)
        {
            _dragging = false;
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

        if (!IsEnabled || e.Handled)
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
