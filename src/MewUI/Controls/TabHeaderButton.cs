using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Tab header presenter used by <see cref="TabControl"/> to render and interact with individual tab headers.
/// </summary>
internal sealed class TabHeaderButton : ContentControl
{
    private bool _isPressed;

    /// <summary>
    /// Gets or sets the tab index this header represents.
    /// </summary>
    public int Index { get; set; }

    /// <summary>
    /// Gets or sets whether this tab is currently selected.
    /// </summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// Gets or sets whether the associated tab is enabled for interaction.
    /// </summary>
    public bool IsTabEnabled { get; set; } = true;

    /// <summary>
    /// Raised when the header is clicked (tab selection request).
    /// </summary>
    public event Action<int>? Clicked;

    protected override Color DefaultBackground => Theme.Palette.ButtonFace;

    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;

    protected override double DefaultBorderThickness => Theme.Metrics.ControlBorderThickness;

    protected override double DefaultMinHeight => Theme.Metrics.BaseControlHeight;

    public TabHeaderButton()
    {
        Padding = new Thickness(8, 4, 8, 4);
    }

    // Keep header buttons out of the default Tab focus order.
    // Keyboard navigation is handled by TabControl itself (arrows / Ctrl+PgUp/PgDn).
    public override bool Focusable => false;

    protected override UIElement? OnHitTest(Point point)
    {
        // Match WPF semantics: disabled tabs should not participate in hit testing,
        // otherwise they keep receiving hover/mouse-over changes and triggering redraw.
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled || !IsTabEnabled)
        {
            return null;
        }

        // Prefer inner button hit targets (e.g. close button), but keep the rest of the header
        // clickable as a tab-select surface.
        var hit = base.OnHitTest(point);
        if (hit == null)
        {
            return null;
        }

        if (hit != this)
        {
            return hit is Button ? hit : this;
        }

        return this;
    }

    protected override void OnRender(IGraphicsContext context)
    {
        double radiusDip = Math.Max(0, Theme.Metrics.ControlCornerRadius);
        var metrics = GetBorderRenderMetrics(Bounds, radiusDip);
        var bounds = metrics.Bounds;
        var radius = metrics.CornerRadius;

        var host = Parent?.Parent as TabControl;
        var tabBg = host?.GetTabBackground(Theme, IsSelected) ?? (IsSelected ? Theme.Palette.ControlBackground : Theme.Palette.ButtonFace);
        var outline = host?.GetOutlineColor(Theme) ?? Theme.Palette.ControlBorder;

        var isEffectivelyEnabled = IsEffectivelyEnabled && IsTabEnabled;
        var state = GetVisualState(_isPressed, _isPressed, isEffectivelyEnabled);

        Color bg = IsSelected ? tabBg : PickButtonBackground(state, tabBg);

        var baseBorder = IsSelected && isEffectivelyEnabled ? outline : Theme.Palette.ControlBorder;
        var border = PickAccentBorder(Theme, baseBorder, state, hoverMix: 0.4);

        // Top-only rounding via clipping:
        // Draw a taller rounded-rect, then clip to the real bounds so the bottom corners are clipped away.
        // This keeps the header looking like VS-style "document tabs" without requiring path geometry support.
        var rounded = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height + radius + 4);

        // Use fill-based border (outer + inner) to avoid stroke centering being clipped by the tight clip.
        DrawBackgroundAndBorder(context, rounded, bg, border, radiusDip);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);

        if (Content == null)
        {
            return;
        }

        // Keep the tab label vertically centered.
        var contentBounds = bounds.Deflate(Padding).Deflate(new Thickness(GetBorderVisualInset()));
        var desired = Content.DesiredSize;
        if (desired.Height > 0 && contentBounds.Height > desired.Height + 0.5)
        {
            double y = contentBounds.Y + (contentBounds.Height - desired.Height) / 2;
            Content.Arrange(new Rect(contentBounds.X, y, contentBounds.Width, desired.Height));
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (Content == null)
        {
            return Size.Empty;
        }

        // Keep measure/arrange symmetric: ArrangeContent deflates border inset (snapped to pixels),
        // so measurement must include it to avoid text clipping (GDI/OpenGL).
        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;
        var contentSize = availableSize.Deflate(Padding).Deflate(border);

        Content.Measure(contentSize);
        return Content.DesiredSize.Inflate(Padding).Inflate(border);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Handled)
        {
            return;
        }

        if (e.Button == MouseButton.Left && IsEffectivelyEnabled && IsTabEnabled)
        {
            _isPressed = true;

            var root = FindVisualRoot();
            if (root is Window window)
            {
                window.CaptureMouse(this);
            }

            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button == MouseButton.Left && _isPressed)
        {
            _isPressed = false;

            var root = FindVisualRoot();
            if (root is Window window)
            {
                window.ReleaseMouseCapture();
            }

            if (IsEffectivelyEnabled && IsTabEnabled && Bounds.Contains(e.Position))
            {
                Clicked?.Invoke(Index);
            }

            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        if (_isPressed)
        {
            _isPressed = false;
            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || !IsEffectivelyEnabled || !IsTabEnabled)
        {
            return;
        }

        if (e.Key is Key.Space or Key.Enter)
        {
            _isPressed = true;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Handled || !IsEffectivelyEnabled || !IsTabEnabled)
        {
            return;
        }

        if (e.Key is Key.Space or Key.Enter)
        {
            if (_isPressed)
            {
                _isPressed = false;
                Clicked?.Invoke(Index);
                InvalidateVisual();
            }

            e.Handled = true;
        }
    }
}
