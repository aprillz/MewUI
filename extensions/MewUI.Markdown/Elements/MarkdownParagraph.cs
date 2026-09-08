using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Markdown;

internal sealed class MarkdownParagraph : TextElement
{
    private readonly IReadOnlyList<MarkdownSpan> _spans;
    private readonly Action<MarkdownSpan> _activate;
    private readonly MarkdownTheme _theme;
    private readonly List<(int Start, int Length, MarkdownSpan Span)> _links = [];
    private readonly List<Rect> _rangeBounds = [];
    private ITextEngine? _engine;
    private ITextLayout? _layout;
    private double _width = double.NaN;
    private uint _dpi;
    private TextRunStyle _style;
    private int _focusedLink;
    private int _pressedLink = -1;

    internal MarkdownParagraph(IReadOnlyList<MarkdownSpan> spans, MarkdownTheme theme, Action<MarkdownSpan> activate)
    {
        _spans = spans;
        _theme = theme;
        _activate = activate;
        Text = string.Concat(spans.Select(span => span.Text));
        int offset = 0;
        foreach (var span in spans)
        {
            if (span.Url != null && !span.Image && span.Text.Length > 0)
            {
                if (_links.Count > 0 && _links[^1].Start + _links[^1].Length == offset &&
                    _links[^1].Span.Url == span.Url && _links[^1].Span.SourceStart == span.SourceStart)
                {
                    var previous = _links[^1];
                    _links[^1] = (previous.Start, previous.Length + span.Text.Length, previous.Span);
                }
                else
                {
                    _links.Add((offset, span.Text.Length, span));
                }
            }
            offset += span.Text.Length;
        }
        Focusable = _links.Count > 0;
    }

    internal string Text { get; }
    internal TextAlignment Alignment { get; init; }
    internal double FontScale { get; init; } = 1;
    internal bool Heading { get; init; }

    internal ITextLayout GetLayout(double width)
    {
        width = double.IsFinite(width) ? Math.Max(1, width) : 1_000_000;
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
        _engine = engine;
        _width = width;
        _dpi = dpi;
        _style = style;
        var runs = new List<GeometryStyleRun>();
        int offset = 0;
        foreach (var span in _spans)
        {
            if (span.Text.Length > 0)
            {
                runs.Add(new GeometryStyleRun(offset, span.Text.Length, style with
                {
                    FontFamily = span.Code ? _theme.CodeFontFamily : style.FontFamily,
                    Weight = span.Bold ? FontWeight.Bold : style.Weight,
                    Italic = span.Italic || style.Italic,
                    Decoration = (span.Strike ? TextDecoration.Strikethrough : TextDecoration.None) |
                        (span.Inserted ? TextDecoration.Underline : TextDecoration.None)
                }));
            }
            offset += span.Text.Length;
        }
        _layout = engine.GetOrCreateLayout(new TextLayoutRequest
        {
            Text = Text.AsMemory(), DefaultStyle = style, Dpi = dpi, Runs = runs,
            Paragraph = new TextParagraphStyle { MaxWidth = width, Wrapping = TextWrapping.Wrap, Alignment = Alignment }
        }, TextLayoutCachePolicy.Owner, this);
        return _layout;
    }

    internal double GetFirstLineHeight(double width)
    {
        var lines = GetLayout(width).Lines;
        return lines.Count == 0 ? 0 : lines[0].Bounds.Height;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var measured = GetLayout(availableSize.Width).MeasuredSize;
        double scale = GetDpi() / 96.0;
        return new Size(Math.Ceiling(measured.Width * scale) / scale + 1 / scale, measured.Height);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        if (double.IsFinite(bounds.Width) && Math.Abs(bounds.Width - _width) > 0.01)
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
            paints.Add(new TextPaintSpan(new TextRange(offset, span.Text.Length),
                link ? _theme.LinkForeground ?? Theme.Palette.Accent : null,
                span.Code ? _theme.CodeBackground ?? Theme.Palette.ControlBackground :
                    span.Marked ? _theme.MarkedBackground ?? Theme.Palette.Accent.WithAlpha(64) : null,
                link ? TextDecoration.Underline : TextDecoration.None));
            offset += span.Text.Length;
        }
        var options = new TextDrawOptions(Foreground, paints.ToArray(), Owner: this);
        context.Save();
        try
        {
            context.SetClip(Bounds);
            context.Text.Draw(layout, new Point(Bounds.X, Bounds.Y), in options);
            if (IsFocused && _links.Count > 0)
            {
                var link = _links[_focusedLink];
                _rangeBounds.Clear();
                layout.GetRangeBounds(link.Start, link.Length, _rangeBounds);
                foreach (var rectangle in _rangeBounds)
                {
                    context.DrawRectangle(new Rect(Bounds.X + rectangle.X, Bounds.Y + rectangle.Y,
                        rectangle.Width, rectangle.Height), Theme.Palette.Focus, 1);
                }
            }
        }
        finally
        {
            context.Restore();
        }
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
        base.OnDispose();
    }
}
