using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A control that displays text.
/// </summary>
public partial class Label : Control
{
    private TextMeasureCache _textMeasureCache;
    protected override bool InvalidateOnMouseOverChanged => false;

    /// <summary>
    /// Gets or sets the text content.
    /// </summary>
    public string Text
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
    /// Gets or sets the horizontal text alignment.
    /// </summary>
    public TextAlignment TextAlignment
    {
        get;
        set
        {
            field = value;
            InvalidateVisual();
        }
    } = TextAlignment.Left;

    /// <summary>
    /// Gets or sets the vertical text alignment.
    /// </summary>
    public TextAlignment VerticalTextAlignment
    {
        get;
        set
        {
            field = value;
            InvalidateVisual();
        }
    } = TextAlignment.Top;

    /// <summary>
    /// Gets or sets the text wrapping mode.
    /// </summary>
    public TextWrapping TextWrapping
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            _textMeasureCache.Invalidate();
            InvalidateMeasure();
        }
    } = TextWrapping.NoWrap;

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
        }

        var size = _textMeasureCache.Measure(factory, GetDpi(), font, Text, wrapping, maxWidth);
        return size.Inflate(Padding);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        if (TryGetBinding(TextBindingSlot, out ValueBinding<string> textBinding))
        {
            SetTextFromBinding(textBinding.Get());
        }

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
        
        var color = IsEffectivelyEnabled ? Foreground : Theme.Palette.DisabledText;
        context.DrawText(Text, bounds, font, color, TextAlignment, VerticalTextAlignment, wrapping);
    }

    public void SetTextBinding(Func<string> get, Action<Action>? subscribe = null, Action<Action>? unsubscribe = null)
    {
        SetTextBindingCore(get, subscribe, unsubscribe);
    }

    private void SetTextFromBinding(string value)
    {
        value ??= string.Empty;
        if (Text == value)
        {
            return;
        }

        Text = value;
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
