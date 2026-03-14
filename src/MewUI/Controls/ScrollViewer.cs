using Aprillz.MewUI.Input;
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
    , IFocusIntoViewHost
{
    public static readonly MewProperty<double> ViewportCornerRadiusProperty =
        MewProperty<double>.Register<ScrollViewer>(nameof(ViewportCornerRadius), 0.0, MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<ScrollMode> VerticalScrollProperty =
        MewProperty<ScrollMode>.Register<ScrollViewer>(nameof(VerticalScroll), ScrollMode.Auto, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<ScrollMode> HorizontalScrollProperty =
        MewProperty<ScrollMode>.Register<ScrollViewer>(nameof(HorizontalScroll), ScrollMode.Disabled, MewPropertyOptions.AffectsLayout);

    private readonly ScrollBar _vBar;
    private readonly ScrollBar _hBar;
    private readonly ScrollController _scroll = new();

    private Size _extent = Size.Empty;
    private Size _viewport = Size.Empty;
    private Size _lastNotifiedExtent = Size.Empty;
    private Size _lastNotifiedViewport = Size.Empty;
    private Point _lastNotifiedOffset = new(double.NaN, double.NaN);
/// <summary>
    /// Raised when scroll metrics or offsets change.
    /// </summary>
    public event Action? ScrollChanged;

    /// <summary>
    /// Optional corner radius applied to the content viewport clip.
    /// </summary>
    public double ViewportCornerRadius
    {
        get => GetValue(ViewportCornerRadiusProperty);
        set => SetValue(ViewportCornerRadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical scrollbar mode.
    /// </summary>
    public ScrollMode VerticalScroll
    {
        get => GetValue(VerticalScrollProperty);
        set => SetValue(VerticalScrollProperty, value);
    }

    /// <summary>
    /// Gets or sets the horizontal scrollbar mode.
    /// </summary>
    public ScrollMode HorizontalScroll
    {
        get => GetValue(HorizontalScrollProperty);
        set => SetValue(HorizontalScrollProperty, value);
    }

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
        ReevaluateMouseOverAfterScroll();
        NotifyScrollChanged();
    }

    bool IFocusIntoViewHost.OnDescendantFocused(UIElement focusedElement)
    {
        if (focusedElement == this || Content == null)
        {
            return false;
        }

        // Don't scroll when the focused element is the direct content itself —
        // it spans the entire scrollable area and scrolling it "into view" is nonsensical.
        if (focusedElement == Content)
        {
            return true;
        }

        var size = focusedElement.RenderSize;
        var localRect = new Rect(0, 0, size.Width, size.Height);

        Rect rectInViewer;
        try
        {
            // TranslateRect returns coords in ScrollViewer-local space (relative to this.Bounds.TopLeft).
            rectInViewer = focusedElement.TranslateRect(localRect, this);
        }
        catch (InvalidOperationException)
        {
            return false;
        }

        // GetContentViewportBounds returns in parent coordinate space; convert to local space.
        var borderInset = GetBorderVisualInset();
        var vpParent = GetContentViewportBounds(Bounds, borderInset);
        var vp = new Rect(vpParent.X - Bounds.X, vpParent.Y - Bounds.Y, vpParent.Width, vpParent.Height);

        double newOffsetX = HorizontalOffset;
        double newOffsetY = VerticalOffset;

        if (_vBar.IsVisible)
        {
            if (rectInViewer.Y < vp.Y)
                newOffsetY = VerticalOffset - (vp.Y - rectInViewer.Y);
            else if (rectInViewer.Bottom > vp.Bottom)
                newOffsetY = VerticalOffset + (rectInViewer.Bottom - vp.Bottom);
        }

        if (_hBar.IsVisible)
        {
            if (rectInViewer.X < vp.X)
                newOffsetX = HorizontalOffset - (vp.X - rectInViewer.X);
            else if (rectInViewer.Right > vp.Right)
                newOffsetX = HorizontalOffset + (rectInViewer.Right - vp.Right);
        }

        // Clamp via ScrollController (DPI-aware pixel-accurate max) rather than raw extent arithmetic.
        _scroll.DpiScale = DpiScale;
        newOffsetX = Math.Clamp(newOffsetX, 0, _scroll.GetMaxDip(0));
        newOffsetY = Math.Clamp(newOffsetY, 0, _scroll.GetMaxDip(1));

        bool changed = !newOffsetX.Equals(HorizontalOffset) || !newOffsetY.Equals(VerticalOffset);
        if (changed)
        {
            SetScrollOffsets(newOffsetX, newOffsetY);
        }

        return true;
    } 

    /// <summary>
    /// Initializes a new instance of the ScrollViewer class.
    /// </summary>
    public ScrollViewer()
    {
        _vBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false };
        _hBar = new ScrollBar { Orientation = Orientation.Horizontal, IsVisible = false };

        _vBar.Parent = this;
        _hBar.Parent = this;

        _vBar.ValueChanged += v =>
        {
            VerticalOffset = v;
            InvalidateVisual();
            ReevaluateMouseOverAfterScroll();
            NotifyScrollChanged();
        };

        _hBar.ValueChanged += v =>
        {
            HorizontalOffset = v;
            InvalidateVisual();
            ReevaluateMouseOverAfterScroll();
            NotifyScrollChanged();
        };
    }

    private double DpiScale => GetDpi() / 96.0;

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        if (oldDpi == 0 || newDpi == 0 || oldDpi == newDpi)
        {
            return;
        }

        double oldScale = oldDpi / 96.0;
        double newScale = newDpi / 96.0;
        if (oldScale <= 0 || newScale <= 0 || double.IsNaN(oldScale) || double.IsNaN(newScale) || double.IsInfinity(oldScale) || double.IsInfinity(newScale))
        {
            return;
        }

        // Preserve logical (DIP) scroll offsets across DPI changes.
        // ScrollController stores metrics/offsets in pixels for stable rounding, so when the DPI scale changes
        // we must rescale the stored pixel offset to keep the same DIP position visible.
        _scroll.DpiScale = oldScale;
        double offsetX = _scroll.GetOffsetDip(0);
        double offsetY = _scroll.GetOffsetDip(1);

        _scroll.DpiScale = newScale;
        _scroll.SetMetricsDip(0, _extent.Width, _viewport.Width);
        _scroll.SetMetricsDip(1, _extent.Height, _viewport.Height);

        bool changed = false;
        changed |= _scroll.SetOffsetDip(0, offsetX);
        changed |= _scroll.SetOffsetDip(1, offsetY);

        if (changed)
        {
            InvalidateArrange();
        }

        SyncBars();
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


        double viewportW0 = Math.Max(0, slotW - Padding.HorizontalThickness);
        double viewportH0 = Math.Max(0, slotH - Padding.VerticalThickness);

        var viewportRect = LayoutRounding.SnapConstraintRectToPixels(new Rect(0, 0, viewportW0, viewportH0), dpiScale);
        _viewport = LayoutRounding.RoundSizeToPixels(viewportRect.Size, dpiScale);

        var measureSize = new Size(
            HorizontalScroll == ScrollMode.Disabled ? _viewport.Width : double.PositiveInfinity,
            VerticalScroll == ScrollMode.Disabled ? _viewport.Height : double.PositiveInfinity);

        if (content is IScrollContent scrollContent)
        {
            scrollContent.SetViewport(_viewport);

            // Scroll-driven content should not require infinite measurement; it virtualizes internally.
            content.Measure(_viewport);

            // Read extent AFTER measuring: measurement may sync containers and compute the real extent
            // (e.g. StackItemsPresenter computes _totalHeight in MeasureAllItems).
            _extent = LayoutRounding.RoundSizeToPixels(scrollContent.Extent, dpiScale);
        }
        else
        {
            content.Measure(measureSize);
            _extent = LayoutRounding.RoundSizeToPixels(content.DesiredSize, dpiScale);
        }

        _scroll.SetMetricsDip(0, _extent.Width, _viewport.Width);
        _scroll.SetMetricsDip(1, _extent.Height, _viewport.Height);

        // Allow 1 device-pixel tolerance to suppress scrollbars caused by sub-pixel
        // rounding differences between extent and viewport at non-integer DPI scales.
        bool needV = _scroll.GetExtentPx(1) > _scroll.GetViewportPx(1) + 1;
        bool needH = _scroll.GetExtentPx(0) > _scroll.GetViewportPx(0) + 1;
        _vBar.IsVisible = IsBarVisible(VerticalScroll, needV);
        _hBar.IsVisible = IsBarVisible(HorizontalScroll, needH);

        SyncBars();

        // Extent/viewport changes (e.g. content becomes empty) can make existing offsets invalid.
        // Clamp them against the latest _extent/_viewport even when scrollbars are hidden.
        // Extent/viewport changes can make existing offsets invalid.
        // Clamp using DIP offsets to avoid quantizing the logical offset via px roundtrips,
        // especially noticeable when DPI changes.
        {
            _scroll.SetOffsetDip(0, _scroll.GetOffsetDip(0));
            _scroll.SetOffsetDip(1, _scroll.GetOffsetDip(1));
        }
        SyncBars();
        NotifyScrollChanged();

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

        var dpiScale = DpiScale;
        // Keep viewport consistent with the one used for clamping offsets and bar ranges.
        _viewport = LayoutRounding.RoundSizeToPixels(viewport.Size, dpiScale);
        _scroll.DpiScale = dpiScale;
        _scroll.SetMetricsDip(0, _extent.Width, _viewport.Width);
        _scroll.SetMetricsDip(1, _extent.Height, _viewport.Height);

        // Clamp offsets against the latest extent/viewport before arranging children.
        // Clamp using DIP offsets to avoid quantizing the logical offset via px roundtrips,
        // especially noticeable when DPI changes.
        _scroll.SetOffsetDip(0, _scroll.GetOffsetDip(0));
        _scroll.SetOffsetDip(1, _scroll.GetOffsetDip(1));
        SyncBars();

        if (Content is UIElement content)
        {
            if (content is IScrollContent scrollContent)
            {
                scrollContent.SetViewport(_viewport);
                scrollContent.SetOffset(new Point(_scroll.GetOffsetDip(0), _scroll.GetOffsetDip(1)));

                // Do not translate content via Arrange when it is scroll-driven.
                // Content renders/arranges internally based on the provided offset.
                content.Arrange(new Rect(
                    viewport.X,
                    viewport.Y,
                    viewport.Width,
                    viewport.Height));
            }
            else
            {
                content.Arrange(new Rect(
                    viewport.X - _scroll.GetOffsetDip(0),
                    viewport.Y - _scroll.GetOffsetDip(1),
                    Math.Max(_extent.Width, viewport.Width),
                    Math.Max(_extent.Height, viewport.Height)));
            }
        }

        ArrangeBars(GetChromeBounds(bounds, borderInset));
        NotifyScrollChanged();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // Optional background/border (thin style defaults to none).
        if (Background.A > 0 || BorderThickness > 0)
        {
            DrawBackgroundAndBorder(context, Bounds, Background, BorderBrush, CornerRadius);
        }
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        var borderInset = GetBorderVisualInset();
        var viewport = GetContentViewportBounds(Bounds, borderInset);
        var clip = GetContentClipBounds(viewport);

        // Render content clipped to viewport.
        context.Save();
        double r = Math.Max(0, ViewportCornerRadius - Math.Min(Padding.Left, Math.Min(Padding.Right, Math.Min(Padding.Top, Padding.Bottom))));
        if (r > 0)
        {
            r = Math.Min(r, Math.Min(clip.Width, clip.Height) / 2);
            context.SetClipRoundedRect(clip, r, r);
        }
        else
        {
            context.SetClip(clip);
        }
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

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        if (Content != null && !visitor(Content)) return false;
        if (!visitor(_vBar)) return false;
        return visitor(_hBar);
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
        ReevaluateMouseOverAfterScroll();
        NotifyScrollChanged();
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
        ReevaluateMouseOverAfterScroll();
        NotifyScrollChanged();
    }

    private void ReevaluateMouseOverAfterScroll()
    {
        if (FindVisualRoot() is Window window)
        {
            window.ReevaluateMouseOver();
        }
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
        var dpiScale = DpiScale;
        var onePx = 1.0 / dpiScale;

        // Avoid expanding past the scroll chrome (or into negative coordinates). Some backends clamp
        // negative clip origins, which effectively shifts the clip right and can "eat" the leftmost pixel.
        var borderInset = GetBorderVisualInset();
        var chrome = GetChromeBounds(Bounds, borderInset);
        double leftRoom = Math.Max(0, viewport.X - chrome.X);
        double rightRoom = Math.Max(0, chrome.Right - viewport.Right);

        double expandL = Math.Min(onePx, leftRoom);
        double expandR = Math.Min(onePx, rightRoom);

        var expanded = new Rect(
            viewport.X - expandL,
            viewport.Y,
            viewport.Width + expandL + expandR,
            viewport.Height);
        return LayoutRounding.MakeClipRect(expanded, dpiScale, rightPx: 0, bottomPx: 0);
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

    private void NotifyScrollChanged()
    {
        var offset = new Point(HorizontalOffset, VerticalOffset);
        if (_lastNotifiedExtent == _extent && _lastNotifiedViewport == _viewport && _lastNotifiedOffset == offset)
        {
            return;
        }

        _lastNotifiedExtent = _extent;
        _lastNotifiedViewport = _viewport;
        _lastNotifiedOffset = offset;
        ScrollChanged?.Invoke();

        // Close context menus when content scrolls (standard desktop UX).
        if (FindVisualRoot() is Window window)
        {
            window.RequestClosePopups(PopupCloseRequest.Scroll());
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
