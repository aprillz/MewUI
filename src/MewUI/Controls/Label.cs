using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A control that displays text.
/// </summary>
public class Label : Control
{
    private ValueBinding<string>? _textBinding;
    protected override bool InvalidateOnMouseOverChanged => false;
    private TextMeasureKey _lastMeasureKey;
    private Size _lastMeasuredTextSize;
    private bool _hasMeasuredText;

    private readonly record struct TextMeasureKey(
        string Text,
        IFont Font,
        TextWrapping Wrapping,
        double MaxWidthDip,
        uint Dpi);

    private void InvalidateTextMeasure()
    {
        _lastMeasureKey = default;
        _lastMeasuredTextSize = default;
        _hasMeasuredText = false;
    }

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
            InvalidateTextMeasure();
            InvalidateMeasure();
        }
    } = string.Empty;

    /// <summary>
    /// Gets or sets the horizontal text alignment.
    /// </summary>
    public TextAlignment TextAlignment
    {
        get;
        set { field = value; InvalidateVisual(); }
    } = TextAlignment.Left;

    /// <summary>
    /// Gets or sets the vertical text alignment.
    /// </summary>
    public TextAlignment VerticalTextAlignment
    {
        get;
        set { field = value; InvalidateVisual(); }
    } = TextAlignment.Top;

    /// <summary>
    /// Gets or sets the text wrapping mode.
    /// </summary>
    public TextWrapping TextWrapping
    {
        get;
        set
        {
            field = value;
            InvalidateTextMeasure();
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
        var dpi = GetDpi();

        double maxWidth = 0;
        if (wrapping == TextWrapping.NoWrap)
        {
            var key = new TextMeasureKey(Text, font, wrapping, 0, dpi);
            if (_hasMeasuredText && key == _lastMeasureKey)
            {
                return _lastMeasuredTextSize.Inflate(Padding);
            }

            using var ctx = factory.CreateMeasurementContext(dpi);
            _lastMeasuredTextSize = ctx.MeasureText(Text, font);
            _lastMeasureKey = key;
            _hasMeasuredText = true;
            return _lastMeasuredTextSize.Inflate(Padding);
        }
        else
        {
            maxWidth = availableSize.Width - Padding.HorizontalThickness;
            if (double.IsNaN(maxWidth) || maxWidth <= 0)
            {
                maxWidth = 0;
            }

            // Avoid passing infinity into backend implementations that convert to int pixel widths.
            if (double.IsPositiveInfinity(maxWidth))
            {
                maxWidth = 1_000_000;
            }

            maxWidth = maxWidth > 0 ? maxWidth : 1_000_000;

            var key = new TextMeasureKey(Text, font, wrapping, maxWidth, dpi);
            if (_hasMeasuredText && key == _lastMeasureKey)
            {
                return _lastMeasuredTextSize.Inflate(Padding);
            }

            using var ctx = factory.CreateMeasurementContext(dpi);
            _lastMeasuredTextSize = ctx.MeasureText(Text, font, maxWidth);
            _lastMeasureKey = key;
            _hasMeasuredText = true;
            return _lastMeasuredTextSize.Inflate(Padding);
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        if (_textBinding != null)
        {
            SetTextFromBinding(_textBinding.Get());
        }

        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        var contentBounds = Bounds.Deflate(Padding);
        var font = GetFont();

        var wrapping = TextWrapping;
        if (wrapping == TextWrapping.NoWrap && HasExplicitLineBreaks)
        {
            wrapping = TextWrapping.Wrap;
        }

        context.DrawText(Text, contentBounds, font, Foreground,
            TextAlignment, VerticalTextAlignment, wrapping);
    }

    public void SetTextBinding(Func<string> get, Action<Action>? subscribe = null, Action<Action>? unsubscribe = null)
    {
        _textBinding?.Dispose();
        _textBinding = new ValueBinding<string>(
            get,
            null,
            subscribe,
            unsubscribe,
            () => SetTextFromBinding(get()));

        SetTextFromBinding(get());
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
        _textBinding?.Dispose();
        _textBinding = null;
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        InvalidateTextMeasure();
        base.OnDpiChanged(oldDpi, newDpi);
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        InvalidateTextMeasure();
        base.OnThemeChanged(oldTheme, newTheme);
    }
}
