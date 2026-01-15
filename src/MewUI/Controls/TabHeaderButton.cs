using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

internal sealed class TabHeaderButton : ContentControl
{
    private bool _isPressed;

    public int Index { get; set; }
    public bool IsSelected { get; set; }
    public bool IsTabEnabled { get; set; } = true;
    public event Action<int>? Clicked;

    protected override Color DefaultBackground => Theme.Current.Palette.ButtonFace;
    protected override Color DefaultBorderBrush => Theme.Current.Palette.ControlBorder;

    public TabHeaderButton()
    {
        BorderThickness = 1;
        Padding = new Thickness(10, 5, 10, 5);
        MinHeight = Theme.Current.BaseControlHeight;
    }

    // Keep header buttons out of the default Tab focus order.
    // Keyboard navigation is handled by TabControl itself (arrows / Ctrl+PgUp/PgDn).
    public override bool Focusable => false;

    public override UIElement? HitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible)
        {
            return null;
        }

        return Bounds.Contains(point) ? this : null;
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var theme = GetTheme();
        var bounds = GetSnappedBorderBounds(Bounds);
        double radius = Math.Max(0, theme.ControlCornerRadius);
        var state = GetVisualState(isPressed: _isPressed, isActive: _isPressed);

        var host = Parent?.Parent as TabControl;
        var tabBg = host?.GetTabBackground(theme, IsSelected) ?? (IsSelected ? theme.Palette.ControlBackground : theme.Palette.ButtonFace);
        var outline = host?.GetOutlineColor(theme) ?? theme.Palette.ControlBorder;

        Color bg = tabBg;
        if (!IsEnabled || !IsTabEnabled)
        {
            bg = theme.Palette.ButtonDisabledBackground;
        }
        else if (state.IsPressed)
        {
            bg = theme.Palette.ButtonPressedBackground;
        }
        else if (state.IsHot && !IsSelected)
        {
            bg = theme.Palette.ButtonHoverBackground;
        }

        var border = IsSelected ? outline : theme.Palette.ControlBorder;

        // Top-only rounding via clipping:
        // Draw a taller rounded-rect, then clip to the real bounds so the bottom corners are clipped away.
        // This keeps the header looking like VS-style "document tabs" without requiring path geometry support.
        var rounded = new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height + radius);

        context.Save();
        context.SetClip(bounds);
        DrawBackgroundAndBorder(context, rounded, bg, border, radius);

        context.Restore();
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

        if (e.Button == MouseButton.Left && IsEnabled && IsTabEnabled)
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

            if (IsEnabled && IsTabEnabled && Bounds.Contains(e.Position))
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
        if (e.Handled || !IsEnabled || !IsTabEnabled)
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
        if (e.Handled || !IsEnabled || !IsTabEnabled)
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
