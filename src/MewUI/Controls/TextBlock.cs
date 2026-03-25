using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Controls.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Lightweight text element (WPF-like) that does not carry full <see cref="Control"/> features.
/// Shares <see cref="Control.ForegroundProperty"/>, <see cref="Control.FontFamilyProperty"/>,
/// <see cref="Control.FontSizeProperty"/>, and <see cref="Control.FontWeightProperty"/> so that
/// inherited values propagate naturally from parent controls without style-target interference.
/// </summary>
public partial class TextBlock : FrameworkElement, IDisposable
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

    protected virtual void OnTextChanged() => InvalidateTextLayout();
    private void OnTextWrappingChanged() => InvalidateTextLayout();
    private void OnTextTrimmingChanged() => InvalidateTextLayout();

    protected void InvalidateTextLayout()
    {
        InvalidateTextMeasure();
        _lastWrapMeasureWidth = null;
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
    /// Gets or sets the text foreground color.
    /// Shares <see cref="Control.ForegroundProperty"/> — inherits from the nearest parent Control.
    /// </summary>
    public Color Foreground
    {
        get => GetValue(Control.ForegroundProperty);
        set => SetValue(Control.ForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the font family.
    /// Shares <see cref="Control.FontFamilyProperty"/> — inherits from the nearest parent Control.
    /// </summary>
    public string FontFamily
    {
        get => GetValue(Control.FontFamilyProperty);
        set => SetValue(Control.FontFamilyProperty, value);
    }

    /// <summary>
    /// Gets or sets the font size.
    /// Shares <see cref="Control.FontSizeProperty"/> — inherits from the nearest parent Control.
    /// </summary>
    public double FontSize
    {
        get => GetValue(Control.FontSizeProperty);
        set => SetValue(Control.FontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the font weight.
    /// Shares <see cref="Control.FontWeightProperty"/> — inherits from the nearest parent Control.
    /// </summary>
    public FontWeight FontWeight
    {
        get => GetValue(Control.FontWeightProperty);
        set => SetValue(Control.FontWeightProperty, value);
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

        if (property.Id == Control.FontFamilyProperty.Id ||
            property.Id == Control.FontSizeProperty.Id ||
            property.Id == Control.FontWeightProperty.Id)
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

        return _textMeasureCache.Measure(factory, GetDpi(), font, Text, wrapping, maxWidth);
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
        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        var wrapping = TextWrapping;
        if (wrapping == TextWrapping.NoWrap && HasExplicitLineBreaks)
        {
            wrapping = TextWrapping.Wrap;
        }

        var factory = GetGraphicsFactory();
        var font = EnsureFont(factory);
        context.DrawText(Text, Bounds, font, Foreground, TextAlignment, VerticalTextAlignment, wrapping, TextTrimming);
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
        _font?.Dispose();
        _font = null;
    }
}
