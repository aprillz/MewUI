using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Markdown;

internal sealed class MarkdownParagraph : TextElement, ISelectableText
{
    // Unwrapped layouts (code blocks) measure at this width so the viewer can scroll them sideways.
    private const double UNWRAPPED_WIDTH = 1_000_000;

    private readonly IReadOnlyList<MarkdownSpan> _spans;
    private int _selectionStart;
    private int _selectionEnd;
    private readonly Action<MarkdownSpan> _activate;
    private readonly MarkdownTheme _theme;
    private readonly IReadOnlyList<MarkdownInlineImage> _images;
    // Custom inline objects aligned with the span list; null entries are plain text.
    private readonly IReadOnlyList<IInlineTextObject?>? _objects;
    private readonly List<(int Start, int Length, MarkdownSpan Span)> _links = [];
    private readonly List<Rect> _rangeBounds = [];
    private readonly Dictionary<TextRunStyle, IFont> _metricFonts = [];
    private ITextEngine? _engine;
    private ITextLayout? _layout;
    private double _width = double.NaN;
    private uint _dpi;
    private TextRunStyle _style;
    private int _focusedLink;
    private int _pressedLink = -1;

    internal MarkdownParagraph(
        IReadOnlyList<MarkdownSpan> spans,
        MarkdownTheme theme,
        Action<MarkdownSpan> activate,
        Uri? baseUri = null,
        IMarkdownImageResolver? resolver = null,
        IReadOnlyList<IInlineTextObject?>? objects = null)
    {
        _spans = spans;
        _theme = theme;
        _activate = activate;
        _objects = objects;
        _images = spans.Where(static span => span.Image)
            .Select(span => new MarkdownInlineImage(span, baseUri, resolver, InvalidateImage))
            .ToArray();
        Text = string.Concat(spans.Select(GetVisualText));
        int offset = 0;
        foreach (var span in spans)
        {
            int length = GetVisualText(span).Length;
            string? linkUrl = GetLinkUrl(span);
            if (linkUrl != null && length > 0)
            {
                if (_links.Count > 0 && _links[^1].Start + _links[^1].Length == offset &&
                    GetLinkUrl(_links[^1].Span) == linkUrl &&
                    _links[^1].Span.SourceStart == span.SourceStart)
                {
                    var previous = _links[^1];
                    _links[^1] = (previous.Start, previous.Length + length, previous.Span);
                }
                else
                {
                    _links.Add((offset, length, span));
                }
            }
            offset += length;
        }
        Focusable = _links.Count > 0;
    }

    internal string Text { get; }
    internal TextAlignment Alignment { get; init; }
    internal double FontScale { get; init; } = 1;
    internal bool Heading { get; init; }
    /// <summary>False lays the text out on one line per source line, for code blocks that scroll sideways.</summary>
    internal bool Wrap { get; init; } = true;
    /// <summary>True skips the inline code background; the block draws its own.</summary>
    internal bool PlainCode { get; init; }

    public int TextUnit { get; set; } = -1;
    public int TextLength => Text.Length;
    internal int LinkCount => _links.Count;
    internal int FocusedLink => _focusedLink;

    /// <summary>Moves keyboard focus to this paragraph with the given link current; used by the presenter's Tab traversal.</summary>
    internal void FocusLink(int index)
    {
        if (_links.Count == 0)
        {
            return;
        }
        _focusedLink = Math.Clamp(index, 0, _links.Count - 1);
        Focus();
        InvalidateVisual();
    }
    internal bool IsFullySelected => Text.Length > 0 && _selectionStart == 0 && _selectionEnd == Text.Length;

    public int OffsetAt(Point windowPoint)
    {
        var layout = GetLayout(Bounds.Width);
        double localY = windowPoint.Y - Bounds.Y;
        if (localY < 0 || layout.Lines.Count == 0)
        {
            return 0;
        }
        var lastLine = layout.Lines[^1];
        if (localY >= lastLine.Bounds.Bottom)
        {
            return Text.Length;
        }
        var hit = layout.HitTestPoint(new Point(windowPoint.X - Bounds.X, localY));
        return Math.Clamp(hit.InsertionIndex, 0, Text.Length);
    }

    public (int Start, int End) WordAt(int offset)
    {
        offset = Math.Clamp(offset, 0, Text.Length);
        if (Text.Length == 0)
        {
            return (0, 0);
        }
        // Same rule as the core text editor: letters, digits and underscores form a word; any
        // other character selects just itself.
        int probe = Math.Min(offset, Text.Length - 1);
        if (!IsWordCharacter(Text[probe]))
        {
            int elementEnd = probe + 1;
            if (elementEnd < Text.Length && char.IsSurrogatePair(Text[probe], Text[elementEnd]))
            {
                elementEnd++;
            }
            return (probe, elementEnd);
        }
        int start = probe;
        while (start > 0 && IsWordCharacter(Text[start - 1]))
        {
            start--;
        }
        int end = probe + 1;
        while (end < Text.Length && IsWordCharacter(Text[end]))
        {
            end++;
        }
        return (start, end);
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    public void SetSelection(int start, int end)
    {
        start = Math.Clamp(start, 0, Text.Length);
        end = Math.Clamp(end, 0, Text.Length);
        if (end <= start)
        {
            start = 0;
            end = 0;
        }
        if (start == _selectionStart && end == _selectionEnd)
        {
            return;
        }
        _selectionStart = start;
        _selectionEnd = end;
        InvalidateVisual();
    }

    internal ITextLayout GetLayout(double width)
    {
        width = Wrap && double.IsFinite(width) ? Math.Max(1, width) : UNWRAPPED_WIDTH;
        var style = new TextRunStyle(FontFamily, FontSize * FontScale, Heading ? FontWeight.Bold : FontWeight,
            FontStyle == FontStyle.Italic);
        uint dpi = GetDpi();
        var engine = GetGraphicsFactory().TextEngine;
        if (_layout != null && _width == width && _dpi == dpi && _style == style && ReferenceEquals(_engine, engine))
        {
            return _layout;
        }

        if (_engine != null && !ReferenceEquals(_engine, engine))
        {
            _engine.ManagedCache.ReleaseOwner(this);
        }
        if (_dpi != dpi || _style != style || !ReferenceEquals(_engine, engine))
        {
            ClearMetricFonts();
        }
        _engine = engine;
        _width = width;
        _dpi = dpi;
        _style = style;
        int imageIndex = 0;
        var runs = new List<GeometryStyleRun>();
        var inlines = new List<InlineRun>();
        int offset = 0;
        for (int spanIndex = 0; spanIndex < _spans.Count; spanIndex++)
        {
            var span = _spans[spanIndex];
            string visualText = GetVisualText(span);
            if (visualText.Length > 0)
            {
                runs.Add(new GeometryStyleRun(offset, visualText.Length, ResolveSpanStyle(style, span)));
                IInlineTextObject? inlineObject = _objects?[spanIndex];
                if (inlineObject != null)
                {
                    inlines.Add(new InlineRun(offset, visualText.Length, inlineObject));
                }
                else if (span.Image)
                {
                    MarkdownInlineImage image = _images[imageIndex++];
                    image.Start(Application.IsRunning ? Application.Current.Dispatcher : null, SynchronizationContext.Current);
                    if (image.IsReady && image.TryPrepare(GetGraphicsFactory(), width))
                    {
                        inlines.Add(new InlineRun(offset, visualText.Length, image));
                    }
                }
            }
            else if (span.Image)
            {
                MarkdownInlineImage image = _images[imageIndex++];
                image.Start(Application.IsRunning ? Application.Current.Dispatcher : null, SynchronizationContext.Current);
            }
            offset += visualText.Length;
        }
        _layout = engine.GetOrCreateLayout(new TextLayoutRequest
        {
            Text = Text.AsMemory(), DefaultStyle = style, Dpi = dpi, Runs = runs, Inlines = inlines,
            Paragraph = new TextParagraphStyle { MaxWidth = width, Wrapping = Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap, Alignment = Alignment }
        }, TextLayoutCachePolicy.Owner, this);
        return _layout;
    }

    internal double GetFirstLineVisualCenter(double width)
    {
        var lines = GetLayout(width).Lines;
        if (lines.Count == 0)
        {
            return 0;
        }

        var line = lines[0];
        var font = GetMetricFont(_style);
        return line.Bounds.Y + line.Baseline - font.XHeight * 0.5;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var measured = GetLayout(availableSize.Width).MeasuredSize;
        double scale = GetDpi() / 96.0;
        return new Size(Math.Ceiling(measured.Width * scale) / scale + 1 / scale, measured.Height);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        if (Wrap && double.IsFinite(bounds.Width) && Math.Abs(bounds.Width - _width) > 0.01)
        {
            GetLayout(bounds.Width);
            InvalidateMeasure();
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var layout = GetLayout(Bounds.Width);
        var paints = new List<TextPaintSpan>();
        int offset = 0;
        foreach (var span in _spans)
        {
            bool link = span.Url != null && !span.Image;
            int length = GetVisualText(span).Length;
            paints.Add(new TextPaintSpan(new TextRange(offset, length),
                link ? _theme.LinkForeground ?? Theme.Palette.Accent : null,
                span.Marked && !span.Code ? _theme.MarkedBackground ?? Theme.Palette.Accent.WithAlpha(64) : null,
                link ? TextDecoration.Underline : TextDecoration.None));
            offset += length;
        }
        if (_selectionEnd > _selectionStart)
        {
            // Background only: the selection paints behind the glyphs like the core text boxes,
            // and a null foreground leaves link colors to their own spans.
            paints.Add(new TextPaintSpan(new TextRange(_selectionStart, _selectionEnd - _selectionStart),
                null, Theme.Palette.SelectionBackground));
        }
        var options = new TextDrawOptions(Foreground, paints.ToArray(), Owner: this);
        context.Save();
        try
        {
            context.SetClip(Bounds);
            if (!PlainCode)
            {
                DrawInlineCodeBackgrounds(context, layout);
            }
            context.Text.Draw(layout, new Point(Bounds.X, Bounds.Y), in options);
            if (IsFocused && _links.Count > 0)
            {
                var link = _links[_focusedLink];
                _rangeBounds.Clear();
                layout.GetRangeBounds(link.Start, link.Length, _rangeBounds);
                double focusThickness = LayoutRounding.SnapThicknessToPixels(1, context.DpiScale, 1);
                foreach (var rectangle in _rangeBounds)
                {
                    var focusBounds = LayoutRounding.SnapBoundsRectToPixels(
                        new Rect(Bounds.X + rectangle.X, Bounds.Y + rectangle.Y,
                            rectangle.Width, rectangle.Height),
                        context.DpiScale);
                    context.DrawRectangle(focusBounds, Theme.Palette.Focus, focusThickness, strokeInset: true);
                }
            }
        }
        finally
        {
            context.Restore();
        }
    }

    private void DrawInlineCodeBackgrounds(IGraphicsContext context, ITextLayout layout)
    {
        Color background = _theme.CodeBackground ?? Theme.Palette.ControlBackground;
        int offset = 0;
        foreach (var span in _spans)
        {
            int length = GetVisualText(span).Length;
            if (!span.Code || length == 0)
            {
                offset += length;
                continue;
            }

            var font = GetMetricFont(ResolveSpanStyle(_style, span));
            _rangeBounds.Clear();
            layout.GetRangeBounds(offset, length, _rangeBounds);
            foreach (var rangeBounds in _rangeBounds)
            {
                foreach (var line in layout.Lines)
                {
                    if (Math.Abs(line.Bounds.Y - rangeBounds.Y) > 0.01)
                    {
                        continue;
                    }

                    double baseline = line.Bounds.Y + line.Baseline;
                    context.FillRectangle(new Rect(
                        Bounds.X + rangeBounds.X,
                        Bounds.Y + baseline - font.Ascent,
                        rangeBounds.Width,
                        font.Ascent + font.Descent), background);
                    break;
                }
            }
            offset += length;
        }
    }

    private TextRunStyle ResolveSpanStyle(TextRunStyle style, MarkdownSpan span) => style with
    {
        FontFamily = span.Code ? _theme.CodeFontFamily : style.FontFamily,
        Weight = span.Bold ? FontWeight.Bold : style.Weight,
        Italic = span.Italic || style.Italic,
        Decoration = (span.Strike ? TextDecoration.Strikethrough : TextDecoration.None) |
            (span.Inserted ? TextDecoration.Underline : TextDecoration.None)
    };

    private IFont GetMetricFont(TextRunStyle style)
    {
        if (!_metricFonts.TryGetValue(style, out var font))
        {
            font = GetGraphicsFactory().CreateFont(style.FontFamily, style.FontSize, _dpi,
                style.Weight, style.Italic);
            _metricFonts.Add(style, font);
        }
        return font;
    }

    private void ClearMetricFonts()
    {
        foreach (var font in _metricFonts.Values)
        {
            font.Dispose();
        }
        _metricFonts.Clear();
    }

    private static string GetVisualText(MarkdownSpan span) => span.Image && span.Text.Length == 0
        ? "\uFFFC"
        : span.Text;

    private static string? GetLinkUrl(MarkdownSpan span) => span.Image ? span.LinkUrl : span.Url;

    private void InvalidateImage()
    {
        _engine?.ManagedCache.ReleaseOwner(this);
        _layout = null;
        InvalidateMeasure();
        InvalidateVisual();
    }

    internal int HitLink(Point position)
    {
        if (!Bounds.Contains(position))
        {
            return -1;
        }
        var local = new Point(position.X - Bounds.X, position.Y - Bounds.Y);
        var layout = GetLayout(Bounds.Width);
        for (int index = 0; index < _links.Count; index++)
        {
            var link = _links[index];
            _rangeBounds.Clear();
            layout.GetRangeBounds(link.Start, link.Length, _rangeBounds);
            if (_rangeBounds.Any(rectangle => rectangle.Contains(local)))
            {
                return index;
            }
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs args)
    {
        base.OnMouseMove(args);
        Cursor = IsEffectivelyEnabled && HitLink(WindowPosition(args)) >= 0 ? CursorType.Hand : null;
    }

    protected override void OnMouseDown(MouseEventArgs args)
    {
        base.OnMouseDown(args);
        if (args.Handled || !IsEffectivelyEnabled || args.Button != MouseButton.Left)
        {
            return;
        }
        _pressedLink = HitLink(WindowPosition(args));
        if (_pressedLink >= 0)
        {
            _focusedLink = _pressedLink;
            Focus();
            InvalidateVisual();
            args.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseEventArgs args)
    {
        base.OnMouseUp(args);
        int pressed = _pressedLink;
        _pressedLink = -1;
        if (!args.Handled && IsEffectivelyEnabled && args.Button == MouseButton.Left && pressed >= 0 && HitLink(WindowPosition(args)) == pressed)
        {
            args.Handled = true;
            _activate(_links[pressed].Span);
        }
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        _pressedLink = -1;
        Cursor = null;
    }

    private Point WindowPosition(MouseEventArgs args)
    {
        var local = args.GetPosition(this);
        return new Point(Bounds.X + local.X, Bounds.Y + local.Y);
    }

    protected override void OnKeyDown(KeyEventArgs args)
    {
        base.OnKeyDown(args);
        HandleKey(args);
    }

    internal void HandleKey(KeyEventArgs args)
    {
        if (args.Handled || !IsEffectivelyEnabled || _links.Count == 0)
        {
            return;
        }
        if (args.Key == Key.Enter || args.Key == Key.Space)
        {
            args.Handled = true;
            if (!args.IsRepeat)
            {
                _activate(_links[_focusedLink].Span);
            }
        }
        else if (args.Key == Key.Tab)
        {
            int next = _focusedLink + (args.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1);
            if (next >= 0 && next < _links.Count)
            {
                _focusedLink = next;
                args.Handled = true;
                InvalidateVisual();
            }
        }
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();
        _focusedLink = 0;
        _pressedLink = -1;
    }

    protected override void OnDispose()
    {
        _engine?.ManagedCache.ReleaseOwner(this);
        _layout = null;
        ClearMetricFonts();
        foreach (var image in _images)
        {
            image.Dispose();
        }
        if (_objects != null)
        {
            foreach (var inlineObject in _objects)
            {
                (inlineObject as IDisposable)?.Dispose();
            }
        }
        base.OnDispose();
    }
}
