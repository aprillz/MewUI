using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public class CheckBox : ToggleBase
{
    private bool _isPressed;

    public CheckBox()
    {
        BorderThickness = 1;
        Padding = new Thickness(2);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        const double boxSize = 14;
        const double spacing = 6;

        double width = boxSize + spacing;
        double height = boxSize;

        if (!string.IsNullOrEmpty(Text))
        {
            using var measure = BeginTextMeasurement();
            var textSize = measure.Context.MeasureText(Text, measure.Font);
            width += textSize.Width;
            height = Math.Max(height, textSize.Height);
        }

        return new Size(width, height).Inflate(Padding);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var theme = GetTheme();
        var bounds = Bounds;
        var contentBounds = bounds.Deflate(Padding);
        var state = GetVisualState(_isPressed, _isPressed);

        const double boxSize = 14;
        const double spacing = 6;

        double boxY = contentBounds.Y + (contentBounds.Height - boxSize) / 2;
        var boxRect = new Rect(contentBounds.X, boxY, boxSize, boxSize);

        var fill = state.IsEnabled ? theme.Palette.ControlBackground : theme.Palette.DisabledControlBackground;
        var radius = Math.Max(0, theme.ControlCornerRadius * 0.5);

        var borderColor = PickAccentBorder(theme, BorderBrush, state, 0.6);
        var stroke = Math.Max(1, BorderThickness);
        DrawBackgroundAndBorder(context, boxRect, fill, borderColor, radius);

        if (IsChecked)
        {
            // Check mark
            var p1 = new Point(boxRect.X + 3, boxRect.Y + boxRect.Height * 0.55);
            var p2 = new Point(boxRect.X + boxRect.Width * 0.45, boxRect.Bottom - 3);
            var p3 = new Point(boxRect.Right - 3, boxRect.Y + 3);
            context.DrawLine(p1, p2, theme.Palette.Accent, 2);
            context.DrawLine(p2, p3, theme.Palette.Accent, 2);
        }

        if (!string.IsNullOrEmpty(Text))
        {
            var font = GetFont();
            var textColor = state.IsEnabled ? Foreground : theme.Palette.DisabledText;
            var textBounds = new Rect(contentBounds.X + boxSize + spacing, contentBounds.Y, contentBounds.Width - boxSize - spacing, contentBounds.Height);
            context.DrawText(Text, textBounds, font, textColor, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        _isPressed = true;
        Focus();

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.CaptureMouse(this);
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButton.Left || !_isPressed)
        {
            return;
        }

        _isPressed = false;

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.ReleaseMouseCapture();
        }

        if (IsEnabled && Bounds.Contains(e.Position))
        {
            IsChecked = !IsChecked;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        // Space handled by ToggleBase
    }
}
