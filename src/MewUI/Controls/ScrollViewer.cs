using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public enum ScrollMode
{
    Disabled,
    Auto,
    Visible
}

public sealed class ScrollViewer : ContentControl
    , IVisualTreeHost
{
    private readonly ScrollBar _vBar;
    private readonly ScrollBar _hBar;

    private Size _extent = Size.Empty;
    private Size _viewport = Size.Empty;

    public ScrollMode VerticalScroll
    {
        get;
        set { field = value; InvalidateMeasure(); InvalidateVisual(); }
    } = ScrollMode.Auto;

    public ScrollMode HorizontalScroll
    {
        get;
        set { field = value; InvalidateMeasure(); InvalidateVisual(); }
    } = ScrollMode.Disabled;

    public double VerticalOffset
    {
        get;
        private set
        {
            double clamped = ClampOffset(value, axis: 1);
            if (field.Equals(clamped))
            {
                return;
            }

            field = clamped;
            InvalidateArrange();
        }
    }

    public double HorizontalOffset
    {
        get;
        private set
        {
            double clamped = ClampOffset(value, axis: 0);
            if (field.Equals(clamped))
            {
                return;
            }

            field = clamped;
            InvalidateArrange();
        }
    }

    public void SetScrollOffsets(double horizontalOffset, double verticalOffset)
    {
        HorizontalOffset = horizontalOffset;
        VerticalOffset = verticalOffset;
        InvalidateVisual();
    }

    public ScrollViewer()
    {
        BorderThickness = 0;

        Padding = new Thickness(16);

        _vBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false };
        _hBar = new ScrollBar { Orientation = Orientation.Horizontal, IsVisible = false };

        _vBar.Parent = this;
        _hBar.Parent = this;

        _vBar.ValueChanged += v =>
        {
            VerticalOffset = v;
            InvalidateVisual();
        };

        _hBar.ValueChanged += v =>
        {
            HorizontalOffset = v;
            InvalidateVisual();
        };
    }

    protected override Size MeasureContent(Size availableSize)
    {
        // We don't draw our own border by default; rely on content.
        var borderInset = GetBorderVisualInset();
        var chromeSlot = new Rect(0, 0, availableSize.Width, availableSize.Height)
            .Deflate(new Thickness(borderInset));

        // Get DPI scale for consistent layout rounding between Measure and Arrange.
        // Without this, viewport calculated here may differ from the one in ArrangeContent/Render
        // due to rounding differences, causing content clipping at non-100% DPI.
        var dpiScale = GetDpi() / 96.0;

        if (Content is not UIElement content)
        {
            _extent = Size.Empty;
            _vBar.IsVisible = false;
            _hBar.IsVisible = false;
            _viewport = Size.Empty;
            return new Size(0, 0).Inflate(Padding);
        }

        double slotW = Math.Max(0, chromeSlot.Width);
        double slotH = Math.Max(0, chromeSlot.Height);

        // Overlay scrollbars: viewport does not change when bars appear/disappear.
        // This avoids resize jitter and feedback loops when scrollable controls are nested.
        double viewportW = Math.Max(0, slotW - Padding.HorizontalThickness);
        double viewportH = Math.Max(0, slotH - Padding.VerticalThickness);

        viewportW = LayoutRounding.RoundToPixel(viewportW, dpiScale);
        viewportH = LayoutRounding.RoundToPixel(viewportH, dpiScale);
        _viewport = new Size(viewportW, viewportH);

        var measureSize = new Size(
            HorizontalScroll == ScrollMode.Disabled ? viewportW : double.PositiveInfinity,
            VerticalScroll == ScrollMode.Disabled ? viewportH : double.PositiveInfinity);

        content.Measure(measureSize);
        _extent = content.DesiredSize;

        bool needV = _extent.Height > _viewport.Height + 0.5;
        bool needH = _extent.Width > _viewport.Width + 0.5;

        _vBar.IsVisible = IsBarVisible(VerticalScroll, needV);
        _hBar.IsVisible = IsBarVisible(HorizontalScroll, needH);
        SyncBars();

        // Extent/viewport changes (e.g. content becomes empty) can make existing offsets invalid.
        // Clamp them against the latest _extent/_viewport even when scrollbars are hidden.
        HorizontalOffset = HorizontalOffset;
        VerticalOffset = VerticalOffset;

        // Desired size: cap by available chrome slot (exclude padding here because we inflate it below).
        double capW = Math.Max(0, slotW - Padding.HorizontalThickness);
        double capH = Math.Max(0, slotH - Padding.VerticalThickness);
        double desiredW = double.IsPositiveInfinity(availableSize.Width) ? _extent.Width : Math.Min(_extent.Width, capW);
        double desiredH = double.IsPositiveInfinity(availableSize.Height) ? _extent.Height : Math.Min(_extent.Height, capH);

        return new Size(desiredW, desiredH).Inflate(Padding).Inflate(new Thickness(borderInset));
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var borderInset = GetBorderVisualInset();
        var viewport = GetContentViewportBounds(bounds, borderInset);

        if (Content is UIElement content)
        {
            content.Arrange(new Rect(
                viewport.X - HorizontalOffset,
                viewport.Y - VerticalOffset,
                Math.Max(_extent.Width, viewport.Width),
                Math.Max(_extent.Height, viewport.Height)));
        }

        ArrangeBars(GetChromeBounds(bounds, borderInset));
    }

    public override void Render(IGraphicsContext context)
    {
        if (!IsVisible)
        {
            return;
        }

        // Optional background/border (thin style defaults to none).
        if (Background.A > 0 || BorderThickness > 0)
        {
            var theme = GetTheme();
            DrawBackgroundAndBorder(context, Bounds, Background, BorderBrush, theme.ControlCornerRadius);
        }

        var borderInset = GetBorderVisualInset();
        var viewport = GetContentViewportBounds(Bounds, borderInset);

        // Render content clipped to viewport.
        context.Save();
        context.SetClip(viewport);
        Content?.Render(context);
        context.Restore();

        // Bars render on top (overlay).
        if (_vBar.IsVisible)
        {
            _vBar.Render(context);
        }

        if (_hBar.IsVisible)
        {
            _hBar.Render(context);
        }
    }

    public override UIElement? HitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible)
        {
            return null;
        }

        if (_vBar.IsVisible && _vBar.Bounds.Contains(point))
        {
            return _vBar;
        }

        if (_hBar.IsVisible && _hBar.Bounds.Contains(point))
        {
            return _hBar;
        }

        var borderInset = GetBorderVisualInset();
        var viewport = GetContentViewportBounds(Bounds, borderInset);
        if (!viewport.Contains(point))
        {
            return Bounds.Contains(point) ? this : null;
        }

        if (Content is UIElement uiContent)
        {
            var hit = uiContent.HitTest(point);
            if (hit != null)
            {
                return hit;
            }
        }

        return this;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (e.Handled)
        {
            return;
        }

        if (!_vBar.IsVisible && !_hBar.IsVisible)
        {
            return;
        }

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

    void IVisualTreeHost.VisitChildren(Action<Element> visitor)
    {
        if (Content != null)
        {
            visitor(Content);
        }

        visitor(_vBar);
        visitor(_hBar);
    }

    public void ScrollBy(double delta)
    {
        // delta is in wheel units; map to DIPs using a simple step.
        double step = GetTheme().ScrollWheelStep;
        int notches = Math.Sign(delta);
        if (notches == 0)
        {
            return;
        }

        VerticalOffset += notches * step;
        SyncBars();
    }

    public void ScrollByHorizontal(double delta)
    {
        double step = GetTheme().ScrollWheelStep;
        int notches = Math.Sign(delta);
        if (notches == 0)
        {
            return;
        }

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
                Math.Max(0, viewport.Height - (_hBar.IsVisible ? t : 0) - inset * 2)));
        }

        if (_hBar.IsVisible)
        {
            _hBar.Arrange(new Rect(
                viewport.X + inset,
                viewport.Bottom - t - inset,
                Math.Max(0, viewport.Width - (_vBar.IsVisible ? t : 0) - inset * 2),
                t));
        }
    }

    private Rect GetViewportBounds(Rect bounds, double borderInset)
        => GetSnappedBorderBounds(bounds).Deflate(Padding).Deflate(new Thickness(borderInset));

    private Rect GetChromeBounds(Rect bounds, double borderInset)
        => GetSnappedBorderBounds(bounds).Deflate(new Thickness(borderInset));

    private Rect GetContentViewportBounds(Rect bounds, double borderInset)
    {
        // Overlay scrollbars: content viewport does not reserve space for bars.
        return GetChromeBounds(bounds, borderInset).Deflate(Padding);
    }

    private static bool IsBarVisible(ScrollMode visibility, bool needed)
        => visibility switch
        {
            ScrollMode.Disabled => false,
            ScrollMode.Visible => true,
            ScrollMode.Auto => needed,
            _ => false
        };

    private double ClampOffset(double value, int axis)
    {
        double extent = axis == 0 ? _extent.Width : _extent.Height;
        double viewport = axis == 0 ? _viewport.Width : _viewport.Height;
        double max = Math.Max(0, extent - viewport);
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

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
        if (_vBar is IDisposable dv)
        {
            dv.Dispose();
        }

        if (_hBar is IDisposable dh)
        {
            dh.Dispose();
        }
    }
}
