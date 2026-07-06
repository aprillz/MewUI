using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A toggle switch control with optional content label.
/// </summary>
public sealed class ToggleSwitch : ToggleBase
{
    private const double SPACING = 8;

    public static readonly MewProperty<Color> ThumbBrushProperty =
        MewProperty<Color>.Register<ToggleSwitch>(nameof(ThumbBrush), default, MewPropertyOptions.AffectsRender);

    static ToggleSwitch()
    {
        HorizontalAlignmentProperty.OverrideDefaultValue<ToggleSwitch>(HorizontalAlignment.Left);
    }

    private readonly PressCaptureHelper _pressCapture;

    public ToggleSwitch()
    {
        _pressCapture = new PressCaptureHelper(this, SetPressed);
    }

    public Color ThumbBrush
    {
        get => GetValue(ThumbBrushProperty);
        set => SetValue(ThumbBrushProperty, value);
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
    }

    private (double trackWidth, double trackHeight) GetTrackSize()
    {
        double trackHeight = Math.Max(16, Theme.Metrics.BaseControlHeight - 8);
        double trackWidth = Math.Max(trackHeight * 2, trackHeight + 18);
        return (trackWidth, trackHeight);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var (trackWidth, trackHeight) = GetTrackSize();

        double width = trackWidth;
        double height = trackHeight;

        if (Content != null)
        {
            var contentAvailable = new Size(
                Math.Max(0, availableSize.Width - width - SPACING - Padding.HorizontalThickness),
                double.PositiveInfinity);
            Content.Measure(contentAvailable);
            width += SPACING + Content.DesiredSize.Width;
            height = Math.Max(height, Content.DesiredSize.Height);
        }

        return new Size(width, height).Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        if (Content == null)
        {
            return;
        }

        var snappedBounds = GetSnappedBorderBounds(bounds);
        var contentBounds = snappedBounds.Deflate(Padding);

        var (trackWidth, trackHeight) = GetTrackSize();

        double y = contentBounds.Y + (contentBounds.Height - trackHeight) / 2;
        var trackRect = new Rect(contentBounds.X, y, trackWidth, trackHeight);
        trackRect = LayoutRounding.SnapBoundsRectToPixels(trackRect, GetDpi() / 96.0);

        var labelBounds = new Rect(
            trackRect.Right + SPACING,
            contentBounds.Y,
            Math.Max(0, contentBounds.Width - trackRect.Width - SPACING),
            contentBounds.Height);
        Content.Arrange(labelBounds);
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
        var borderColor = GetValue(BorderBrushProperty);
        var thumbFill = ThumbBrush;

        if (IsChecked)
        {
            context.FillRoundedRectangle(trackRect, radius, radius, trackFill);
        }
        else
        {
            DrawBackgroundAndBorder(context, trackRect, trackFill, borderColor, BorderThickness, radius);
        }

        double thumbInset = Math.Max(2, trackRect.Height * 0.20) + borderInset;
        double thumbSize = Math.Max(0, trackRect.Height - thumbInset * 2);
        double thumbXMin = trackRect.X + thumbInset;
        double thumbXMax = trackRect.Right - thumbInset - thumbSize;
        double thumbX = IsChecked ? thumbXMax : thumbXMin;
        var thumbRect = new Rect(thumbX, trackRect.Y + thumbInset, thumbSize, thumbSize);
        context.FillEllipse(thumbRect, thumbFill);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEffectivelyEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        _pressCapture.BeginPress(() => Focus());

        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButton.Left || !IsPressed)
        {
            return;
        }

        _pressCapture.EndPress();

        if (!IsEffectivelyEnabled)
        {
            return;
        }

        IsChecked = !IsChecked;

        e.Handled = true;
    }

}
