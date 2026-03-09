using Aprillz.MewUI.Controls.Text;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A toggle switch control with optional text label.
/// </summary>
public sealed class ToggleSwitch : ToggleBase
{
    private TextMeasureCache _textMeasureCache;

    static ToggleSwitch()
    {
        HorizontalAlignmentProperty.OverrideDefaultValue<ToggleSwitch>(HorizontalAlignment.Left);
    }

    private const double spacing = 8;

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
        _textMeasureCache.Invalidate();
    }

    private (double trackWidth, double trackHeight) GetTrackSize()
    {
        // Match the overall control sizing style (Button/ComboBox) while keeping the switch itself compact.
        // BaseControlHeight is 28 in the default InternalTheme -> trackHeight 20.
        double trackHeight = Math.Max(16, Theme.Metrics.BaseControlHeight - 8);
        double trackWidth = Math.Max(trackHeight * 2, trackHeight + 18);
        return (trackWidth, trackHeight);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var (trackWidth, trackHeight) = GetTrackSize();

        double width = trackWidth;
        double height = trackHeight;

        if (!string.IsNullOrEmpty(Text))
        {
            var factory = GetGraphicsFactory();
            var font = GetFont(factory);
            var textSize = _textMeasureCache.Measure(factory, GetDpi(), font, Text, TextWrapping.NoWrap, 0);
            width += spacing + textSize.Width;
            height = Math.Max(height, textSize.Height);
        }

        return new Size(width, height).Inflate(Padding);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        var contentBounds = bounds.Deflate(Padding);

        var (trackWidth, trackHeight) = GetTrackSize();

        double y = contentBounds.Y + (contentBounds.Height - trackHeight) / 2;
        var trackRect = new Rect(contentBounds.X, y, trackWidth, trackHeight);
        trackRect = LayoutRounding.SnapBoundsRectToPixels(trackRect, context.DpiScale);

        double radius = trackRect.Height / 2.0;
        double borderInset = GetBorderVisualInset();

        var trackFill = GetValue(BackgroundProperty);
        var thumbFill = GetValue(ForegroundProperty);
        var borderColor = GetValue(BorderBrushProperty);

        if (IsChecked)
        {
            context.FillRoundedRectangle(trackRect, radius, radius, trackFill);
        }
        else
        {
            DrawBackgroundAndBorder(context, trackRect, trackFill, borderColor, radius);
        }

        double thumbInset = Math.Max(2, trackRect.Height * 0.20) + borderInset;
        double thumbSize = Math.Max(0, trackRect.Height - thumbInset * 2);
        double thumbXMin = trackRect.X + thumbInset;
        double thumbXMax = trackRect.Right - thumbInset - thumbSize;
        double thumbX = IsChecked ? thumbXMax : thumbXMin;
        var thumbRect = new Rect(thumbX, trackRect.Y + thumbInset, thumbSize, thumbSize);
        context.FillEllipse(thumbRect, thumbFill);

        if (!string.IsNullOrEmpty(Text))
        {
            var font = GetFont();
            var textColor = GetValue(ForegroundProperty);
            var textBounds = new Rect(trackRect.Right + spacing, contentBounds.Y, contentBounds.Width - trackRect.Width - spacing, contentBounds.Height);
            context.DrawText(Text, textBounds, font, textColor, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEffectivelyEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        SetPressed(true);
        Focus();

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.CaptureMouse(this);
        }

        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButton.Left || !IsPressed)
        {
            return;
        }

        SetPressed(false);

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.ReleaseMouseCapture();
        }

        if (!IsEffectivelyEnabled)
        {
            return;
        }

        IsChecked = !IsChecked;

        e.Handled = true;
    }

    // Binding provided by ToggleBase
}
