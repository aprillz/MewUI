using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Scroll mode for scrollbars.
/// </summary>
public enum ScrollMode
{
    /// <summary>Scrolling is disabled.</summary>
    Disabled,
    /// <summary>Scrollbars appear automatically when needed.</summary>
    Auto,
    /// <summary>Scrollbars are always visible.</summary>
    Visible
}

/// <summary>
/// A scrollable content container with horizontal and vertical scrollbars.
/// </summary>
public sealed class ScrollViewer : ContentControl
    , IVisualTreeHost
{
    private readonly ScrollBar _vBar;
    private readonly ScrollBar _hBar;
    private readonly ScrollController _scroll = new();

    private Size _extent = Size.Empty;
    private Size _viewport = Size.Empty;

    /// <summary>
    /// Gets or sets the vertical scrollbar mode.
    /// </summary>
    public ScrollMode VerticalScroll
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
    } = ScrollMode.Auto;

    /// <summary>
    /// Gets or sets the horizontal scrollbar mode.
    /// </summary>
    public ScrollMode HorizontalScroll
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
    } = ScrollMode.Disabled;

    /// <summary>
    /// Gets the vertical scroll offset.
    /// </summary>
    public double VerticalOffset
    {
        get => _scroll.GetOffsetDip(1);
        private set
        {
            _scroll.DpiScale = DpiScale;
            if (_scroll.SetOffsetDip(1, value))
            {
                InvalidateArrange();
            }
        }
    }

    /// <summary>
    /// Gets the horizontal scroll offset.
    /// </summary>
    public double HorizontalOffset
    {
        get => _scroll.GetOffsetDip(0);
        private set
        {
            _scroll.DpiScale = DpiScale;
            if (_scroll.SetOffsetDip(0, value))
            {
                InvalidateArrange();
            }
        }
    }

    /// <summary>
    /// Sets both scroll offsets simultaneously.
    /// </summary>
    /// <param name="horizontalOffset">The horizontal offset.</param>
    /// <param name="verticalOffset">The vertical offset.</param>
    public void SetScrollOffsets(double horizontalOffset, double verticalOffset)
    {
        HorizontalOffset = horizontalOffset;
        VerticalOffset = verticalOffset;
        SyncBars();
        InvalidateVisual();
    }

    /// <summary>
    /// Initializes a new instance of the ScrollViewer class.
    /// </summary>
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

    private double DpiScale => GetDpi() / 96.0;

    protected override Size MeasureContent(Size availableSize)
    {
        // We don't draw our own border by default; rely on content.
        var borderInset = GetBorderVisualInset();
        var chromeSlot = new Rect(0, 0, availableSize.Width, availableSize.Height)
            .Deflate(new Thickness(borderInset));

        // Get DPI scale for consistent layout rounding between Measure and Arrange.
        // Without this, viewport calculated here may differ from the one in ArrangeContent/Render
        // due to rounding differences, causing content clipping at non-100% DPI.
        var dpiScale = DpiScale;
        _scroll.DpiScale = dpiScale;

        if (Content is not UIElement content)
        {
            _extent = Size.Empty;
            _vBar.IsVisible = false;
            _hBar.IsVisible = false;
            _viewport = Size.Empty;
            _scroll.SetMetricsPx(0, 0, 0);
            _scroll.SetMetricsPx(1, 0, 0);
            _scroll.SetOffsetPx(0, 0);
            _scroll.SetOffsetPx(1, 0);
            return new Size(0, 0).Inflate(Padding);
        }

        double slotW = Math.Max(0, chromeSlot.Width);
        double slotH = Math.Max(0, chromeSlot.Height);

        
        int barPx = Math.Max(0, LayoutRounding.RoundToPixelInt(Theme.Metrics.ScrollBarHitThickness, dpiScale));

        // Reserve space for scrollbars inside the viewport (WPF-like), but keep it stable by iterating
        // until visibility decisions stop changing.
        int reserveW = 0;
        int reserveH = 0;
        bool needV = false;
        bool needH = false;

        for (int pass = 0; pass < 2; pass++)
        {
            double viewportW0 = Math.Max(0, slotW - Padding.HorizontalThickness - (reserveW > 0 ? reserveW / dpiScale : 0));
            double viewportH0 = Math.Max(0, slotH - Padding.VerticalThickness - (reserveH > 0 ? reserveH / dpiScale : 0));

            var viewportRect = LayoutRounding.SnapConstraintRectToPixels(new Rect(0, 0, viewportW0, viewportH0), dpiScale);
            _viewport = viewportRect.Size;

            var measureSize = new Size(
                HorizontalScroll == ScrollMode.Disabled ? _viewport.Width : double.PositiveInfinity,
                VerticalScroll == ScrollMode.Disabled ? _viewport.Height : double.PositiveInfinity);

            content.Measure(measureSize);
            _extent = content.DesiredSize;

            _scroll.SetMetricsDip(0, _extent.Width, _viewport.Width);
            _scroll.SetMetricsDip(1, _extent.Height, _viewport.Height);

            needV = _scroll.GetExtentPx(1) > _scroll.GetViewportPx(1);
            needH = _scroll.GetExtentPx(0) > _scroll.GetViewportPx(0);

            bool showV = IsBarVisible(VerticalScroll, needV);
            bool showH = IsBarVisible(HorizontalScroll, needH);
            int nextReserveW = showV ? barPx : 0;
            int nextReserveH = showH ? barPx : 0;

            if (nextReserveW == reserveW && nextReserveH == reserveH)
            {
                _vBar.IsVisible = showV;
                _hBar.IsVisible = showH;
                break;
            }

            reserveW = nextReserveW;
            reserveH = nextReserveH;
            _vBar.IsVisible = showV;
            _hBar.IsVisible = showH;
        }

        SyncBars();

        // Extent/viewport changes (e.g. content becomes empty) can make existing offsets invalid.
        // Clamp them against the latest _extent/_viewport even when scrollbars are hidden.
        // Extent/viewport changes can make existing offsets invalid.
        _scroll.SetOffsetPx(0, _scroll.GetOffsetPx(0));
        _scroll.SetOffsetPx(1, _scroll.GetOffsetPx(1));
        SyncBars();

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

        // Keep viewport consistent with the one used for clamping offsets and bar ranges.
        _viewport = viewport.Size;
        var dpiScale = DpiScale;
        _scroll.DpiScale = dpiScale;
        _scroll.SetMetricsDip(0, _extent.Width, _viewport.Width);
        _scroll.SetMetricsDip(1, _extent.Height, _viewport.Height);

        // Clamp offsets against the latest extent/viewport before arranging children.
        _scroll.SetOffsetPx(0, _scroll.GetOffsetPx(0));
        _scroll.SetOffsetPx(1, _scroll.GetOffsetPx(1));
        SyncBars();

        if (Content is UIElement content)
        {
            content.Arrange(new Rect(
                viewport.X - _scroll.GetOffsetDip(0),
                viewport.Y - _scroll.GetOffsetDip(1),
                Math.Max(_extent.Width, viewport.Width),
                Math.Max(_extent.Height, viewport.Height)));
        }

        ArrangeBars(GetChromeBounds(bounds, borderInset));
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        // Optional background/border (thin style defaults to none).
        if (Background.A > 0 || BorderThickness > 0)
        {
            
            DrawBackgroundAndBorder(context, Bounds, Background, BorderBrush, Theme.Metrics.ControlCornerRadius);
        }

        var borderInset = GetBorderVisualInset();
        var viewport = GetContentViewportBounds(Bounds, borderInset);
        var clip = GetContentClipBounds(viewport);

        // Render content clipped to viewport.
        context.Save();
        context.SetClip(clip);
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

    public override void Render(IGraphicsContext context)
    {
        // ContentControl.Render() would render Content again after OnRender().
        // ScrollViewer renders its content inside a clip and with scroll offsets, so we must avoid double-rendering.
        if (!IsVisible)
        {
            return;
        }

        OnRender(context);
    }

    protected override UIElement? OnHitTest(Point point)
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
            ScrollBy(-e.Delta);
            e.Handled = true;
            return;
        }

        if (e.IsHorizontal && _hBar.IsVisible)
        {
            ScrollByHorizontal(-e.Delta);
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
        double step = Theme.Metrics.ScrollWheelStep;
        int notches = Math.Sign(delta);
        if (notches == 0)
        {
            return;
        }

        _scroll.DpiScale = DpiScale;
        if (_scroll.ScrollByNotches(1, notches, step))
        {
            InvalidateArrange();
        }
        SyncBars();
        InvalidateVisual();
    }

    public void ScrollByHorizontal(double delta)
    {
        double step = Theme.Metrics.ScrollWheelStep;
        int notches = Math.Sign(delta);
        if (notches == 0)
        {
            return;
        }

        _scroll.DpiScale = DpiScale;
        if (_scroll.ScrollByNotches(0, notches, step))
        {
            InvalidateArrange();
        }
        SyncBars();
        InvalidateVisual();
    }

    private void ArrangeBars(Rect viewport)
    {
        
        double t = Theme.Metrics.ScrollBarHitThickness;
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

    private Rect GetChromeBounds(Rect bounds, double borderInset)
    {
        // Avoid using GetSnappedBorderBounds here: it rounds edges and can shift the viewport by 1px at fractional DPI.
        // For scroll chrome/viewport we prefer outward snapping so the clip never shrinks.
        var chrome = bounds.Deflate(new Thickness(borderInset));
        return LayoutRounding.SnapViewportRectToPixels(chrome, DpiScale);
    }

    private Rect GetContentViewportBounds(Rect bounds, double borderInset)
    {
        var viewport = bounds.Deflate(new Thickness(borderInset)).Deflate(Padding);
        return LayoutRounding.SnapViewportRectToPixels(viewport, DpiScale);
    }

    private Rect GetContentClipBounds(Rect viewport)
    {
        // At fractional DPI (e.g. 150%), many primitives draw strokes centered on the edge of their bounds.
        // When a child is aligned exactly on the viewport edge, the stroke can overhang by ~0.5px and get clipped.
        //
        // Expand the clip by 1 device pixel horizontally into the ScrollViewer padding so borders/glyph overhang
        // don't get cut, while still keeping the clip strict against the chrome/border areas.
        var onePx = 1.0 / DpiScale;
        var expanded = new Rect(viewport.X - onePx, viewport.Y, viewport.Width + onePx * 2, viewport.Height);
        return LayoutRounding.MakeClipRect(expanded, DpiScale, rightPx: 0, bottomPx: 0);
    }

    private static bool IsBarVisible(ScrollMode visibility, bool needed)
        => visibility switch
        {
            ScrollMode.Disabled => false,
            ScrollMode.Visible => true,
            ScrollMode.Auto => needed,
            _ => false
        };

    private void SyncBars()
    {
        _scroll.DpiScale = DpiScale;
        double viewportW = _scroll.GetViewportDip(0);
        double viewportH = _scroll.GetViewportDip(1);
        double maxH = _scroll.GetMaxDip(0);
        double maxV = _scroll.GetMaxDip(1);

        if (_vBar.IsVisible)
        {
            _vBar.Minimum = 0;
            _vBar.Maximum = maxV;
            _vBar.ViewportSize = viewportH;
            _vBar.SmallChange = Theme.Metrics.ScrollBarSmallChange;
            _vBar.LargeChange = Theme.Metrics.ScrollBarLargeChange;
            _vBar.Value = _scroll.GetOffsetDip(1);
        }

        if (_hBar.IsVisible)
        {
            _hBar.Minimum = 0;
            _hBar.Maximum = maxH;
            _hBar.ViewportSize = viewportW;
            _hBar.SmallChange = Theme.Metrics.ScrollBarSmallChange;
            _hBar.LargeChange = Theme.Metrics.ScrollBarLargeChange;
            _hBar.Value = _scroll.GetOffsetDip(0);
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
