using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A button control that responds to clicks.
/// </summary>
public partial class Button : Control
{
    private bool _isPressed;
    private TextMeasureCache _textMeasureCache;

    protected override Color DefaultBackground => Theme.Palette.ButtonFace;

    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;

    protected override double DefaultMinHeight => Theme.Metrics.BaseControlHeight;

    public Button()
    {
        BorderThickness = 1;
        Padding = new Thickness(8, 4, 8, 4);
    }

    /// <summary>
    /// Gets or sets the button content text.
    /// </summary>
    public string Content
    {
        get;
        set
        {
            value ??= string.Empty;
            if (field == value)
            {
                return;
            }

            field = value;
            _textMeasureCache.Invalidate();
            InvalidateMeasure();
        }
    } = string.Empty;

    /// <summary>
    /// Click event handler (AOT-compatible).
    /// </summary>
    public event Action? Click;

    public Func<bool>? CanClick
    {
        get;
        set
        {
            field = value;
            ReevaluateSuggestedIsEnabled();
        }
    }

    public override bool Focusable => true;

    protected override bool ComputeIsEnabledSuggestion() => CanClick?.Invoke() ?? true;

    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;

        // Keep the previous fallback sizing behavior for empty content.
        if (string.IsNullOrEmpty(Content))
        {
            return new Size(Padding.HorizontalThickness + 20, Padding.VerticalThickness + 10).Inflate(border);
        }

        var factory = GetGraphicsFactory();
        var font = GetFont(factory);
        var size = _textMeasureCache.Measure(factory, GetDpi(), font, Content, TextWrapping.NoWrap, 0);
        return size.Inflate(Padding).Inflate(border);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var state = GetVisualState(_isPressed, _isPressed);

        // Determine visual state
        Color bgColor;
        Color borderColor = PickAccentBorder(Theme, BorderBrush, state, 0.6);

        if (!state.IsEnabled)
        {
            bgColor = Theme.Palette.ButtonDisabledBackground;
        }
        else if (state.IsPressed)
        {
            bgColor = Theme.Palette.ButtonPressedBackground;
        }
        else if (state.IsHot)
        {
            bgColor = Theme.Palette.ButtonHoverBackground;
        }
        else
        {
            bgColor = Background;
        }

        var bounds = GetSnappedBorderBounds(Bounds);
        double radius = Theme.Metrics.ControlCornerRadius;
        DrawBackgroundAndBorder(context, bounds, bgColor, borderColor, radius);

        // Draw text
        if (!string.IsNullOrEmpty(Content))
        {
            var contentBounds = bounds.Deflate(Padding).Deflate(new Thickness(GetBorderVisualInset()));
            var font = GetFont();
            var textColor = state.IsEnabled ? Foreground : Theme.Palette.DisabledText;
            context.DrawText(Content, contentBounds, font, textColor, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButton.Left && IsEffectivelyEnabled)
        {
            _isPressed = true;
            Focus();

            // Capture mouse
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

            // Release capture
            var root = FindVisualRoot();
            if (root is Window window)
            {
                window.ReleaseMouseCapture();
            }

            // Fire click if still over button
            if (IsEffectivelyEnabled && Bounds.Contains(e.Position))
            {
                OnClick();
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

        // Space or Enter triggers click
        if ((e.Key == Key.Space || e.Key == Key.Enter) && IsEffectivelyEnabled)
        {
            _isPressed = true;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if ((e.Key == Key.Space || e.Key == Key.Enter) && _isPressed)
        {
            _isPressed = false;
            if (IsEffectivelyEnabled)
            {
                OnClick();
            }

            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected virtual void OnClick() => Click?.Invoke();
}
