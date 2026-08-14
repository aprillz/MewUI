using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Diagnostics;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Shared text layout and rendering base for text elements. Carries alignment, wrapping and trimming,
/// owns the font and formatted-text layout, and renders <see cref="DisplayText"/>. Subclasses decide what
/// the displayed string is: <see cref="TextBlock"/> exposes it as <see cref="TextBlock.Text"/>, while
/// <see cref="AccessText"/> derives it from raw mnemonic markup.
/// </summary>
public abstract partial class TextBlockBase : TextElement, IDisposable
{
    public static readonly MewProperty<TextAlignment> TextAlignmentProperty =
        MewProperty<TextAlignment>.Register<TextBlockBase>(nameof(TextAlignment), TextAlignment.Left,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<TextAlignment> VerticalTextAlignmentProperty =
        MewProperty<TextAlignment>.Register<TextBlockBase>(nameof(VerticalTextAlignment), TextAlignment.Center,
            MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<TextWrapping> TextWrappingProperty =
        MewProperty<TextWrapping>.Register<TextBlockBase>(nameof(TextWrapping), TextWrapping.NoWrap,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnTextWrappingChanged());

    public static readonly MewProperty<TextTrimming> TextTrimmingProperty =
        MewProperty<TextTrimming>.Register<TextBlockBase>(nameof(TextTrimming), TextTrimming.None,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnTextTrimmingChanged());

    public static readonly MewProperty<double> LineSpacingProperty =
        MewProperty<double>.Register<TextBlockBase>(nameof(LineSpacing), 0.0,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.InvalidateTextLayout());

    public static readonly MewProperty<LineBoxTrim> LineBoxTrimProperty =
        MewProperty<LineBoxTrim>.Register<TextBlockBase>(nameof(LineBoxTrim), LineBoxTrim.None,
            MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.InvalidateTextLayout());

    private double? _lastWrapMeasureWidth;
    private readonly List<TextPaintSpan> _paintSpans = [];
    private readonly List<GeometryStyleRun> _geometryRuns = [];

    private ITextLayout? _layout;
    private double _layoutMaxWidth;
    private double _layoutMaxHeight;
    private TextRunStyle _layoutStyle;
    private TextWrapping _layoutWrapping;
    private uint _layoutDpi;
    private long _textRevision;
    private long _breaksRevision = -1;
    private bool _hasExplicitLineBreaks;

    /// <summary>
    /// The string actually measured and rendered. Subclasses map their own content onto it.
    /// </summary>
    protected abstract string DisplayText { get; }

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

    /// <summary>
    /// Gets or sets the extra gap between lines (in DIPs); 0 keeps the natural leading and
    /// negative values tighten.
    /// </summary>
    public double LineSpacing
    {
        get => GetValue(LineSpacingProperty);
        set => SetValue(LineSpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets cap-height trimming of the outer line boxes; ink may paint outside the
    /// trimmed bounds.
    /// </summary>
    public LineBoxTrim LineBoxTrim
    {
        get => GetValue(LineBoxTrimProperty);
        set => SetValue(LineBoxTrimProperty, value);
    }

    private void OnTextWrappingChanged() => InvalidateTextLayout();
    private void OnTextTrimmingChanged() => InvalidateTextLayout();

    protected void InvalidateTextLayout()
    {
        _lastWrapMeasureWidth = null;
        _layout = null;

        // The owner cache key excludes the text, so the revision is what tells the engine that a
        // layout built for the same constraints is stale.
        _textRevision++;
    }

    // Rendering resolves wrapping every frame, so the scan is kept until the text revision moves.
    private bool HasExplicitLineBreaks
    {
        get
        {
            if (_breaksRevision != _textRevision)
            {
                _hasExplicitLineBreaks = DisplayText.AsSpan().IndexOfAny('\r', '\n') >= 0;
                _breaksRevision = _textRevision;
            }
            return _hasExplicitLineBreaks;
        }
    }

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);

        // Everything the layout request carries: the font style, and the alignment the engine
        // resolves per line. VerticalTextAlignment only moves the draw origin.
        if (property.Id == TextElement.FontFamilyProperty.Id ||
            property.Id == TextElement.FontSizeProperty.Id ||
            property.Id == TextElement.FontWeightProperty.Id ||
            property.Id == TextAlignmentProperty.Id)
        {
            InvalidateTextLayout();
        }
    }

    /// <summary>Explicit line breaks need line assembly, which only the wrapping paths run.</summary>
    private TextWrapping ResolveWrapping()
        => TextWrapping == TextWrapping.NoWrap && HasExplicitLineBreaks ? TextWrapping.Wrap : TextWrapping;

    private ITextLayout GetOrCreateTextLayout(TextWrapping wrapping, double maxWidth, double maxHeight)
    {
        // Render runs every frame; rebuilding the request would copy the text and rebuild a cache
        // key each time, so the resolved layout is held until an input actually changes. The inputs
        // are compared rather than trusted to invalidation because inherited font values change
        // without notifying this element.
        // Built here rather than through Control's helper: a text block is a TextElement, not a Control.
        var style = new TextRunStyle(FontFamily, FontSize, FontWeight, FontStyle == FontStyle.Italic);
        uint dpi = GetDpi();
        if (_layout is not null &&
            _layoutMaxWidth.Equals(maxWidth) &&
            _layoutMaxHeight.Equals(maxHeight) &&
            _layoutWrapping == wrapping &&
            _layoutDpi == dpi &&
            _layoutStyle == style)
        {
            return _layout;
        }

        _layoutMaxWidth = maxWidth;
        _layoutMaxHeight = maxHeight;
        _layoutWrapping = wrapping;
        _layoutDpi = dpi;
        _layoutStyle = style;
        _geometryRuns.Clear();
        OnGetTextGeometryRuns(style, _geometryRuns);
        _layout = GetGraphicsFactory().TextEngine.GetOrCreateLayout(
            new TextLayoutRequest
            {
                Text = DisplayText.AsMemory(),
                Dpi = dpi,
                DefaultStyle = style,
                Runs = _geometryRuns.Count == 0 ? [] : _geometryRuns.ToArray(),
                Paragraph = new TextParagraphStyle
                {
                    MaxWidth = maxWidth,
                    MaxHeight = maxHeight,
                    Wrapping = wrapping,
                    Trimming = TextTrimming,
                    Alignment = TextAlignment,
                    LineSpacing = LineSpacing,
                    LineBoxTrim = LineBoxTrim,
                    Culture = System.Globalization.CultureInfo.CurrentUICulture
                },
                Revision = _textRevision
            },
            TextLayoutCachePolicy.Owner,
            this);
        return _layout;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (string.IsNullOrEmpty(DisplayText))
        {
            return Size.Empty;
        }

        var wrapping = ResolveWrapping();
        double maxWidth = double.PositiveInfinity;
        if (wrapping != TextWrapping.NoWrap)
        {
            maxWidth = availableSize.Width;
            if (double.IsNaN(maxWidth) || maxWidth <= 0 || double.IsPositiveInfinity(maxWidth))
            {
                maxWidth = 1_000_000;
            }
            _lastWrapMeasureWidth = maxWidth;
        }

        // Measuring against an unbounded height keeps trimming out of the desired size; it applies
        // at render time, where the arranged bounds are known.
        var measured = GetOrCreateTextLayout(wrapping, maxWidth, double.PositiveInfinity).MeasuredSize;

        // The desired width ceils to a device pixel, and wrapped text buys one more. Arrange snaps
        // the left and right edges independently and can land the box a device pixel under the
        // measured width; render then lays the text out again against that box, so the gap turns
        // one line into two, paints over what sits below, and leaves hit-testing on the boxes
        // layout handed out. Text that cannot wrap is clipped instead of re-flowed, so it takes no
        // slack and keeps measuring like the single-line inputs it sits next to.
        double dpiScale = GetDpi() / 96.0;
        double width = Math.Ceiling(measured.Width * dpiScale) / dpiScale;
        if (wrapping != TextWrapping.NoWrap)
        {
            width += 1 / dpiScale;
        }

        return new Size(width, measured.Height);
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

    /// <summary>
    /// Line count and height of the layout the render pass would use for the arranged bounds. The
    /// measure pass answers for an unbounded height, so only this reports the wrapping the user
    /// actually sees.
    /// </summary>
    internal (int LineCount, double ContentHeight) GetRenderLayoutMetrics()
    {
        if (string.IsNullOrEmpty(DisplayText))
        {
            return (0, 0);
        }

        var bounds = Bounds;
        var layout = GetOrCreateTextLayout(ResolveWrapping(), bounds.Width, bounds.Height);
        return (layout.Lines.Count, layout.ContentHeight);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        if (string.IsNullOrEmpty(DisplayText))
        {
            return;
        }

        var bounds = Bounds;
        ITextLayout layout;
        using (ProfilerMarkers.TextLayout.Auto())
        {
            layout = GetOrCreateTextLayout(ResolveWrapping(), bounds.Width, bounds.Height);
        }

        double y = VerticalTextAlignment switch
        {
            TextAlignment.Center => bounds.Y + Math.Max(0, (bounds.Height - layout.ContentHeight) * 0.5),
            TextAlignment.Bottom => bounds.Y + Math.Max(0, bounds.Height - layout.ContentHeight),
            _ => bounds.Y
        };

        // Recomputed per frame rather than cached with the layout: the access-key underline appears
        // and disappears with the Alt state while the layout itself is unchanged.
        _paintSpans.Clear();
        OnGetTextPaintSpans(_paintSpans);
        var spans = _paintSpans.Count == 0
            ? ReadOnlyMemory<TextPaintSpan>.Empty
            : _paintSpans.ToArray();

        using (ProfilerMarkers.TextDraw.Auto())
        {
            var options = new TextDrawOptions(Foreground, spans, Owner: this);
            var origin = new Point(bounds.X, y);
            // The engine draws from an origin with no extent of its own, so text longer than the
            // arranged box would paint over whatever sits next to it.
            if (layout.MeasuredSize.Width > bounds.Width + 0.5 || layout.ContentHeight > bounds.Height + 0.5)
            {
                context.Save();
                try
                {
                    context.SetClip(LayoutRounding.MakeClipRect(bounds, GetDpi() / 96.0));
                    context.Text.Draw(layout, origin, in options);
                }
                finally
                {
                    context.Restore();
                }
            }
            else
            {
                context.Text.Draw(layout, origin, in options);
            }
        }
    }

    /// <summary>
    /// Contributes paint spans applied to the rendered text, such as the access-key underline.
    /// Offsets index <see cref="DisplayText"/>.
    /// </summary>
    protected virtual void OnGetTextPaintSpans(IList<TextPaintSpan> output)
    {
    }

    /// <summary>
    /// Contributes per-range font overrides. Ranges index <see cref="DisplayText"/>, must not
    /// overlap, and <paramref name="defaultStyle"/> is the style unstyled text uses.
    /// </summary>
    protected virtual void OnGetTextGeometryRuns(in TextRunStyle defaultStyle, IList<GeometryStyleRun> output)
    {
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateTextLayout();
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        _layout = null;
    }
}
