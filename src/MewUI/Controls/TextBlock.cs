using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Diagnostics;
using Aprillz.MewUI.Controls.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Lightweight text element (WPF-like) that does not carry full <see cref="Control"/> features.
/// Inherits <see cref="TextElement.ForegroundProperty"/>, <see cref="TextElement.FontFamilyProperty"/>,
/// <see cref="TextElement.FontSizeProperty"/>, and <see cref="TextElement.FontWeightProperty"/> so that
/// inherited values propagate naturally from parent controls without style-target interference.
/// </summary>
public partial class TextBlock : TextElement, IDisposable
{
    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<TextBlock>(nameof(Text), string.Empty,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnTextChanged());

    public static readonly MewProperty<TextAlignment> TextAlignmentProperty =
        MewProperty<TextAlignment>.Register<TextBlock>(nameof(TextAlignment), TextAlignment.Left,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<TextAlignment> VerticalTextAlignmentProperty =
        MewProperty<TextAlignment>.Register<TextBlock>(nameof(VerticalTextAlignment), TextAlignment.Center,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<TextWrapping> TextWrappingProperty =
        MewProperty<TextWrapping>.Register<TextBlock>(nameof(TextWrapping), TextWrapping.NoWrap,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnTextWrappingChanged());

    public static readonly MewProperty<TextTrimming> TextTrimmingProperty =
        MewProperty<TextTrimming>.Register<TextBlock>(nameof(TextTrimming), TextTrimming.None,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnTextTrimmingChanged());

    private IFont? _font;
    private TextMeasureCache _textMeasureCache;
    private double? _lastWrapMeasureWidth;
    private readonly FormattedTextStore _textStore = new();

    protected virtual void OnTextChanged() => InvalidateTextLayout();
    private void OnTextWrappingChanged() => InvalidateTextLayout();
    private void OnTextTrimmingChanged() => InvalidateTextLayout();

    protected void InvalidateTextLayout()
    {
        InvalidateTextMeasure();
        _lastWrapMeasureWidth = null;
        _textStore.InvalidateLayout();
    }

    private uint _lastFontDpi;
    private Theme? _lastFontTheme;
    private string? _lastFontFamily;
    private double _lastFontSize;
    private FontWeight _lastFontWeight;

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

    private void InvalidateTextMeasure() => _textMeasureCache.Invalidate();

    private void InvalidateFont()
    {
        _font?.Dispose();
        _font = null;
        _lastFontDpi = 0;
        _lastFontTheme = null;
        _lastFontFamily = null;
        _lastFontSize = 0;
        _lastFontWeight = default;
        InvalidateTextMeasure();
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);

        if (property.Id == TextElement.FontFamilyProperty.Id ||
            property.Id == TextElement.FontSizeProperty.Id ||
            property.Id == TextElement.FontWeightProperty.Id)
        {
            InvalidateFont();
        }
    }

    protected IFont EnsureFont(IGraphicsFactory factory)
    {
        var dpi = GetDpi();
        var family = FontFamily;
        var size = FontSize;
        var weight = FontWeight;

        if (_font != null &&
            _lastFontDpi == dpi &&
            ReferenceEquals(_lastFontTheme, Theme) &&
            _lastFontFamily == family &&
            _lastFontSize == size &&
            _lastFontWeight == weight)
        {
            return _font;
        }

        _font?.Dispose();
        _font = factory.CreateFont(family, size, dpi, weight);
        _lastFontDpi = dpi;
        _lastFontTheme = Theme;
        _lastFontFamily = family;
        _lastFontSize = size;
        _lastFontWeight = weight;
        return _font;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (string.IsNullOrEmpty(Text))
        {
            _textStore.Invalidate();
            return Size.Empty;
        }

        var wrapping = TextWrapping;
        if (wrapping == TextWrapping.NoWrap && HasExplicitLineBreaks)
        {
            wrapping = TextWrapping.Wrap;
        }

        var factory = GetGraphicsFactory();
        var font = EnsureFont(factory);

        double maxWidth = 0;
        if (wrapping != TextWrapping.NoWrap)
        {
            maxWidth = availableSize.Width;
            if (double.IsNaN(maxWidth) || maxWidth <= 0)
                maxWidth = 0;
            if (double.IsPositiveInfinity(maxWidth))
                maxWidth = 1_000_000;
            maxWidth = maxWidth > 0 ? maxWidth : 1_000_000;
            _lastWrapMeasureWidth = maxWidth;
        }

        var constraintWidth = wrapping != TextWrapping.NoWrap ? maxWidth : double.PositiveInfinity;
        var constraints = new TextLayoutConstraints(new Rect(0, 0, constraintWidth, 0));

        _textStore.SetFormat(new TextFormat
        {
            Font = font,
            HorizontalAlignment = TextAlignment,
            VerticalAlignment = VerticalTextAlignment,
            Wrapping = wrapping,
            Trimming = TextTrimming
        });

        if (_textStore.TryGetMeasuredSize(in constraints, out var measuredSize))
        {
            return measuredSize;
        }

        using var ctx = factory.CreateMeasurementContext(GetDpi());
        return _textStore.Measure(ctx, Text, in constraints);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);

        if (TextWrapping == TextWrapping.NoWrap)
        {
            return;
        }

        var contentWidth = bounds.Width;
        if (double.IsNaN(contentWidth) || double.IsInfinity(contentWidth))
        {
            return;
        }

        if (!_lastWrapMeasureWidth.HasValue || !_lastWrapMeasureWidth.Value.Equals(contentWidth))
        {
            _lastWrapMeasureWidth = contentWidth;
            InvalidateMeasure();
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        if (_textStore.Format == null)
        {
            return;
        }
        TextLayout? layout;
        using (ProfilerMarkers.TextLayout.Auto())
        {
            layout = _textStore.EnsureRenderLayout(context, Text, Bounds);
        }
        if (layout == null) return;
        layout.EffectiveBounds = Bounds;
        using (ProfilerMarkers.TextDraw.Auto())
        {
            context.DrawTextLayout(Text, _textStore.Format, layout, Foreground, owner: this);
        }
        OnRenderTextDecorations(context, _textStore.Format, layout, Text, Bounds);
    }

    protected virtual void OnRenderTextDecorations(
        IGraphicsContext context,
        TextFormat format,
        TextLayout layout,
        string text,
        Rect bounds)
    {
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        _font?.Dispose();
        _font = null;
    }

    protected override void OnDispose()
    {
        base.OnDispose();

        _textStore.Invalidate();

        _font?.Dispose();
        _font = null;
    }
}
