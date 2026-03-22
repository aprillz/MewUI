using Aprillz.MewUI.Controls.Text;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A control that displays text.
/// </summary>
public partial class Label : Control
{
    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<Label>(nameof(Text), string.Empty,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnTextChanged());

    public static readonly MewProperty<TextAlignment> TextAlignmentProperty =
        MewProperty<TextAlignment>.Register<Label>(nameof(TextAlignment), TextAlignment.Left,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<TextAlignment> VerticalTextAlignmentProperty =
        MewProperty<TextAlignment>.Register<Label>(nameof(VerticalTextAlignment), TextAlignment.Center,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<TextWrapping> TextWrappingProperty =
        MewProperty<TextWrapping>.Register<Label>(nameof(TextWrapping), TextWrapping.NoWrap,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnTextWrappingChanged());

    public static readonly MewProperty<TextTrimming> TextTrimmingProperty =
        MewProperty<TextTrimming>.Register<Label>(nameof(TextTrimming), TextTrimming.None,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnTextTrimmingChanged());

    private TextMeasureCache _textMeasureCache;
    private double? _lastWrapMeasureWidth;

    private void OnTextChanged() => InvalidateTextLayout();
    private void OnTextWrappingChanged() => InvalidateTextLayout();
    private void OnTextTrimmingChanged() => InvalidateTextLayout();

    private void InvalidateTextLayout()
    {
        _textMeasureCache.Invalidate();
        _lastWrapMeasureWidth = null;
    }

    protected override bool InvalidateOnMouseOverChanged => false;

    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets the horizontal text alignment.
    /// </summary>
    public TextAlignment TextAlignment
    {
        get => GetValue(TextAlignmentProperty);
        set => SetValue(TextAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the vertical text alignment.
    /// </summary>
    public TextAlignment VerticalTextAlignment
    {
        get => GetValue(VerticalTextAlignmentProperty);
        set => SetValue(VerticalTextAlignmentProperty, value);
    }

    /// <summary>
    /// Gets or sets the text wrapping mode.
    /// </summary>
    public TextWrapping TextWrapping
    {
        get => GetValue(TextWrappingProperty);
        set => SetValue(TextWrappingProperty, value);
    }

    /// <summary>
    /// Gets or sets the text trimming mode.
    /// </summary>
    public TextTrimming TextTrimming
    {
        get => GetValue(TextTrimmingProperty);
        set => SetValue(TextTrimmingProperty, value);
    }

    private bool HasExplicitLineBreaks => Text.AsSpan().IndexOfAny('\r', '\n') >= 0;

    protected override Size MeasureContent(Size availableSize)
    {
        if (string.IsNullOrEmpty(Text))
        {
            return Padding.HorizontalThickness > 0 || Padding.VerticalThickness > 0
                ? new Size(Padding.HorizontalThickness, Padding.VerticalThickness)
                : Size.Empty;
        }

        var wrapping = TextWrapping;
        if (wrapping == TextWrapping.NoWrap && HasExplicitLineBreaks)
        {
            wrapping = TextWrapping.Wrap;
        }

        var factory = GetGraphicsFactory();
        var font = GetFont(factory);

        double maxWidth = 0;
        if (wrapping != TextWrapping.NoWrap)
        {
            maxWidth = availableSize.Width - Padding.HorizontalThickness;
            if (double.IsNaN(maxWidth) || maxWidth <= 0)
            {
                maxWidth = 0;
            }

            if (double.IsPositiveInfinity(maxWidth))
            {
                maxWidth = 1_000_000;
            }

            maxWidth = maxWidth > 0 ? maxWidth : 1_000_000;
            _lastWrapMeasureWidth = maxWidth;
        }

        var size = _textMeasureCache.Measure(factory, GetDpi(), font, Text, wrapping, maxWidth);

        return size.Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);

        if (TextWrapping == TextWrapping.NoWrap)
        {
            return;
        }

        var contentWidth = bounds.Width - Padding.HorizontalThickness;
        if (double.IsNaN(contentWidth) || double.IsInfinity(contentWidth))
        {
            return;
        }

        if (!_lastWrapMeasureWidth.HasValue || !_lastWrapMeasureWidth.Value.Equals(contentWidth))
        {
            // If layout gives us a different width than we measured with, re-measure so wrap height is correct.
            _lastWrapMeasureWidth = contentWidth;

            InvalidateMeasure();
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        var wrapping = TextWrapping;
        if (wrapping == TextWrapping.NoWrap && HasExplicitLineBreaks)
        {
            wrapping = TextWrapping.Wrap;
        }

        var bounds = Bounds.Deflate(Padding);
        var font = GetFont();

        var color = Foreground;
        context.DrawText(Text, bounds, font, color, TextAlignment, VerticalTextAlignment, wrapping, TextTrimming);
    }

    protected override void OnDispose()
    {
        base.OnDispose();
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
        _textMeasureCache.Invalidate();
    }
}
