using Aprillz.MewUI.Core;
using Aprillz.MewUI.Elements;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Panels;
using Aprillz.MewUI.Primitives;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public enum ScrollBarVisibility
{
    Disabled,
    Hidden,
    Auto,
    Visible
}

public sealed class ScrollViewer : Control
{
    private readonly ScrollBar _vBar;
    private readonly ScrollBar _hBar;

    private Size _extent = Size.Empty;
    private Size _viewport = Size.Empty;

    public Element? Content
    {
        get;
        set
        {
            if (ReferenceEquals(field, value))
                return;

            if (field != null)
                field.Parent = null;

            field = value;

            if (field != null)
                field.Parent = this;

            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public ScrollBarVisibility VerticalScrollBarVisibility
    {
        get;
        set { field = value; InvalidateMeasure(); InvalidateVisual(); }
    } = ScrollBarVisibility.Auto;

    public ScrollBarVisibility HorizontalScrollBarVisibility
    {
        get;
        set { field = value; InvalidateMeasure(); InvalidateVisual(); }
    } = ScrollBarVisibility.Disabled;

    public double VerticalOffset
    {
        get;
        private set
        {
            double clamped = ClampOffset(value, axis: 1);
            if (field.Equals(clamped))
                return;
            field = clamped;
            InvalidateVisual();
        }
    }

    public double HorizontalOffset
    {
        get;
        private set
        {
            double clamped = ClampOffset(value, axis: 0);
            if (field.Equals(clamped))
                return;
            field = clamped;
            InvalidateVisual();
        }
    }

    public ScrollViewer()
    {
        BorderThickness = 0;

        _vBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false };
        _hBar = new ScrollBar { Orientation = Orientation.Horizontal, IsVisible = false };

        _vBar.Parent = this;
        _hBar.Parent = this;

        _vBar.ValueChanged = v =>
        {
            VerticalOffset = v;
            InvalidateVisual();
        };

        _hBar.ValueChanged = v =>
        {
            HorizontalOffset = v;
            InvalidateVisual();
        };
    }

    protected override Size MeasureContent(Size availableSize)
    {
        // We don't draw our own border by default; rely on content.
        var borderInset = GetBorderVisualInset();
        var contentSlot = new Rect(0, 0, availableSize.Width, availableSize.Height)
            .Deflate(Padding)
            .Deflate(new Thickness(borderInset));

        double slotW = Math.Max(0, contentSlot.Width);
        double slotH = Math.Max(0, contentSlot.Height);

        _viewport = new Size(slotW, slotH);

        if (Content is not UIElement content)
        {
            _extent = Size.Empty;
            _vBar.IsVisible = false;
            _hBar.IsVisible = false;
            return new Size(0, 0).Inflate(Padding);
        }

        var measureSize = new Size(
            HorizontalScrollBarVisibility == ScrollBarVisibility.Disabled ? slotW : double.PositiveInfinity,
            VerticalScrollBarVisibility == ScrollBarVisibility.Disabled ? slotH : double.PositiveInfinity);

        content.Measure(measureSize);
        _extent = content.DesiredSize;

        bool needV = _extent.Height > _viewport.Height + 0.5;
        bool needH = _extent.Width > _viewport.Width + 0.5;

        _vBar.IsVisible = IsBarVisible(VerticalScrollBarVisibility, needV);
        _hBar.IsVisible = IsBarVisible(HorizontalScrollBarVisibility, needH);

        SyncBars();

        // Desired size: like ContentControl but capped by available size.
        double desiredW = double.IsPositiveInfinity(availableSize.Width) ? _extent.Width : Math.Min(_extent.Width, slotW);
        double desiredH = double.IsPositiveInfinity(availableSize.Height) ? _extent.Height : Math.Min(_extent.Height, slotH);

        return new Size(desiredW, desiredH).Inflate(Padding).Inflate(new Thickness(borderInset));
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var borderInset = GetBorderVisualInset();
        var viewport = GetViewportBounds(bounds, borderInset);

        if (Content is UIElement content)
        {
            content.Arrange(new Rect(
                viewport.X - HorizontalOffset,
                viewport.Y - VerticalOffset,
                Math.Max(_extent.Width, viewport.Width),
                Math.Max(_extent.Height, viewport.Height)));
        }

        ArrangeBars(viewport);
    }

    public override void Render(IGraphicsContext context)
    {
        if (!IsVisible)
            return;

        // Optional background/border (thin style defaults to none).
        if (Background.A > 0 || BorderThickness > 0)
        {
            var theme = GetTheme();
            DrawBackgroundAndBorder(context, Bounds, Background, BorderBrush, theme.ControlCornerRadius);
        }

        var borderInset = GetBorderVisualInset();
        var viewport = GetViewportBounds(Bounds, borderInset);

        // Render content clipped to viewport.
        context.Save();
        context.SetClip(viewport);
        Content?.Render(context);
        context.Restore();

        // Bars render on top (overlay).
        if (_vBar.IsVisible) _vBar.Render(context);
        if (_hBar.IsVisible) _hBar.Render(context);
    }

    public override UIElement? HitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible)
            return null;

        if (_vBar.IsVisible && _vBar.Bounds.Contains(point))
            return _vBar;
        if (_hBar.IsVisible && _hBar.Bounds.Contains(point))
            return _hBar;

        var borderInset = GetBorderVisualInset();
        var viewport = GetViewportBounds(Bounds, borderInset);
        if (!viewport.Contains(point))
            return Bounds.Contains(point) ? this : null;

        if (Content is UIElement uiContent)
        {
            var hit = uiContent.HitTest(point);
            if (hit != null)
                return hit;
        }

        return this;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (e.Handled)
            return;

        if (!_vBar.IsVisible && !_hBar.IsVisible)
            return;

        // Prefer vertical scroll unless horizontal wheel is explicit.
        if (!e.IsHorizontal && _vBar.IsVisible)
        {
            ScrollBy(delta: -e.Delta);
            e.Handled = true;
            return;
        }

        if (e.IsHorizontal && _hBar.IsVisible)
        {
            ScrollByHorizontal(delta: -e.Delta);
            e.Handled = true;
        }
    }

    public void ScrollBy(double delta)
    {
        // delta is in wheel units; map to DIPs using a simple step.
        double step = GetTheme().ScrollWheelStep;
        int notches = Math.Sign(delta);
        if (notches == 0)
            return;
        VerticalOffset += notches * step;
        SyncBars();
    }

    public void ScrollByHorizontal(double delta)
    {
        double step = GetTheme().ScrollWheelStep;
        int notches = Math.Sign(delta);
        if (notches == 0)
            return;
        HorizontalOffset += notches * step;
        SyncBars();
    }

    private void ArrangeBars(Rect viewport)
    {
        var theme = GetTheme();
        double t = theme.ScrollBarHitThickness;
        const double inset = 0;

        if (_vBar.IsVisible)
        {
            _vBar.Arrange(new Rect(
                viewport.Right - t - inset,
                viewport.Y + inset,
                t,
                Math.Max(0, viewport.Height - inset * 2)));
        }

        if (_hBar.IsVisible)
        {
            _hBar.Arrange(new Rect(
                viewport.X + inset,
                viewport.Bottom - t - inset,
                Math.Max(0, viewport.Width - inset * 2),
                t));
        }
    }

    private Rect GetViewportBounds(Rect bounds, double borderInset)
        => GetSnappedBorderBounds(bounds).Deflate(Padding).Deflate(new Thickness(borderInset));

    private static bool IsBarVisible(ScrollBarVisibility visibility, bool needed)
        => visibility switch
        {
            ScrollBarVisibility.Disabled => false,
            ScrollBarVisibility.Hidden => false,
            ScrollBarVisibility.Visible => true,
            ScrollBarVisibility.Auto => needed,
            _ => false
        };

    private double ClampOffset(double value, int axis)
    {
        double extent = axis == 0 ? _extent.Width : _extent.Height;
        double viewport = axis == 0 ? _viewport.Width : _viewport.Height;
        double max = Math.Max(0, extent - viewport);
        if (double.IsNaN(value) || double.IsInfinity(value))
            return 0;
        return Math.Clamp(value, 0, max);
    }

    private void SyncBars()
    {
        if (_vBar.IsVisible)
        {
            _vBar.Minimum = 0;
            _vBar.Maximum = Math.Max(0, _extent.Height - _viewport.Height);
            _vBar.ViewportSize = _viewport.Height;
            _vBar.SmallChange = GetTheme().ScrollBarSmallChange;
            _vBar.LargeChange = GetTheme().ScrollBarLargeChange;
            _vBar.Value = VerticalOffset;
        }

        if (_hBar.IsVisible)
        {
            _hBar.Minimum = 0;
            _hBar.Maximum = Math.Max(0, _extent.Width - _viewport.Width);
            _hBar.ViewportSize = _viewport.Width;
            _hBar.SmallChange = GetTheme().ScrollBarSmallChange;
            _hBar.LargeChange = GetTheme().ScrollBarLargeChange;
            _hBar.Value = HorizontalOffset;
        }
    }

    protected override void OnDispose()
    {
        if (_vBar is IDisposable dv) dv.Dispose();
        if (_hBar is IDisposable dh) dh.Dispose();
    }
}
