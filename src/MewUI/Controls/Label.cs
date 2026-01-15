using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A control that displays text.
/// </summary>
public class Label : Control
{
    private ValueBinding<string>? _textBinding;

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
        set { field = value; InvalidateMeasure(); }
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

        using var measure = BeginTextMeasurement();

        Size textSize;
        var wrapping = TextWrapping;
        if (wrapping == TextWrapping.NoWrap && HasExplicitLineBreaks)
        {
            wrapping = TextWrapping.Wrap;
        }

        if (wrapping == TextWrapping.NoWrap)
        {
            textSize = measure.Context.MeasureText(Text, measure.Font);
        }
        else
        {
            double maxWidth = availableSize.Width - Padding.HorizontalThickness;
            if (double.IsNaN(maxWidth) || maxWidth <= 0)
            {
                maxWidth = 0;
            }

            // Avoid passing infinity into backend implementations that convert to int pixel widths.
            if (double.IsPositiveInfinity(maxWidth))
            {
                maxWidth = 1_000_000;
            }

            textSize = measure.Context.MeasureText(Text, measure.Font, maxWidth > 0 ? maxWidth : 1_000_000);
        }

        return textSize.Inflate(Padding);
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
            set: null,
            subscribe,
            unsubscribe,
            onSourceChanged: () => SetTextFromBinding(get()));

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
}
