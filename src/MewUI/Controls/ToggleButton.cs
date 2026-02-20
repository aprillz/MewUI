using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Controls.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A button-like toggle control. When checked, its background is tinted with the theme accent (50%).
/// </summary>
public sealed partial class ToggleButton : ToggleBase
{
    private bool _isPressed;
    private TextMeasureCache _textMeasureCache;

    protected override Color DefaultBackground => Theme.Palette.ButtonFace;

    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;

    protected override double DefaultBorderThickness => Theme.Metrics.ControlBorderThickness;

    protected override double DefaultMinHeight => Theme.Metrics.BaseControlHeight;

    public ToggleButton()
    {
        Padding = new Thickness(8, 4, 8, 4);
    }

    /// <summary>
    /// Gets or sets the button content text (alias for <see cref="ToggleBase.Text"/>).
    /// </summary>
    public string Content
    {
        get => Text;
        set => Text = value;
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
        _textMeasureCache.Invalidate();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;

        if (string.IsNullOrEmpty(Text))
        {
            return new Size(Padding.HorizontalThickness + 20, Padding.VerticalThickness + 10).Inflate(border);
        }

        var factory = GetGraphicsFactory();
        var font = GetFont(factory);
        var size = _textMeasureCache.Measure(factory, GetDpi(), font, Text, TextWrapping.NoWrap, 0);
        return size.Inflate(Padding).Inflate(border);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var state = GetVisualState(_isPressed, _isPressed);

        var bgColor = PickButtonBackground(state);
        if (IsChecked)
        {
            bgColor = Color.Composite(bgColor,state .IsEnabled?     Theme.Palette.Accent.WithAlpha(96): Theme.Palette.WindowText.WithAlpha(48));
        }

        var borderColor = PickAccentBorder(Theme, BorderBrush, state, 0.6);

        var bounds = GetSnappedBorderBounds(Bounds);
        double radius = Theme.Metrics.ControlCornerRadius;
        DrawBackgroundAndBorder(context, bounds, bgColor, borderColor, radius);

        if (!string.IsNullOrEmpty(Text))
        {
            var contentBounds = bounds.Deflate(Padding).Deflate(new Thickness(GetBorderVisualInset()));
            var font = GetFont();
            var textColor = state.IsEnabled ? Foreground : Theme.Palette.DisabledText;
            context.DrawText(Text, contentBounds, font, textColor, TextAlignment.Center, TextAlignment.Center, TextWrapping.NoWrap);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Handled || e.Button != MouseButton.Left || !IsEffectivelyEnabled)
        {
            return;
        }

        _isPressed = true;
        Focus();

        if (FindVisualRoot() is Window window)
        {
            window.CaptureMouse(this);
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Handled || e.Button != MouseButton.Left || !_isPressed)
        {
            return;
        }

        _isPressed = false;

        if (FindVisualRoot() is Window window)
        {
            window.ReleaseMouseCapture();
        }

        if (IsEffectivelyEnabled && Bounds.Contains(e.Position))
        {
            IsChecked = !IsChecked;
        }

        InvalidateVisual();
        e.Handled = true;
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

        if (e.Handled || !IsEffectivelyEnabled)
        {
            return;
        }

        if (e.Key == Key.Space || e.Key == Key.Enter)
        {
            _isPressed = true;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (!IsEffectivelyEnabled)
        {
            return;
        }

        if ((e.Key == Key.Space || e.Key == Key.Enter) && _isPressed)
        {
            _isPressed = false;

            if (e.Key == Key.Enter)
            {
                IsChecked = !IsChecked;
                e.Handled = true;
            }

            InvalidateVisual();
        }
    }
}
