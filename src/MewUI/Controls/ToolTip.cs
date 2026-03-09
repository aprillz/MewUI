using Aprillz.MewUI.Controls.Text;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A tooltip popup control for displaying help text.
/// </summary>
public sealed class ToolTip : ContentControl
{
    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<ToolTip>(nameof(Text), string.Empty, MewPropertyOptions.AffectsLayout,
            static (self, _, _) =>self._textMeasureCache.Invalidate());

    private TextMeasureCache _textMeasureCache;

    /// <summary>
    /// Gets or sets the tooltip text.
    /// When <see cref="ContentControl.Content"/> is set, this text is ignored.
    /// </summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    static ToolTip()
    {
        IsHitTestVisibleProperty.OverrideDefaultValue<ToolTip>(false);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (Content != null)
        {
            return base.MeasureContent(availableSize);
        }

        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;

        if (string.IsNullOrEmpty(Text))
        {
            return new Size(Padding.HorizontalThickness, Padding.VerticalThickness)
                .Inflate(border);
        }

        var factory = GetGraphicsFactory();
        var font = GetFont(factory);
        var size = _textMeasureCache.Measure(factory, GetDpi(), font, Text, TextWrapping.NoWrap, 0);
        return size.Inflate(Padding).Inflate(border);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        var dpiScale = GetDpi() / 96.0;
        double radius = LayoutRounding.RoundToPixel(CornerRadius, dpiScale);

        DrawBackgroundAndBorder(context, bounds, Background, BorderBrush, radius);

        if (Content != null)
        {
            return;
        }

        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        var borderInset = GetBorderVisualInset();
        var contentBounds = bounds.Deflate(new Thickness(borderInset)).Deflate(Padding);
        context.DrawText(Text, contentBounds, GetFont(), Foreground,
            TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
    }
}
