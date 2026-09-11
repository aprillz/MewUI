using System.Globalization;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Aprillz.MewUI.Markdown;

internal sealed class MarkdownInlineBuilder
{
    private readonly MarkdownOptions _options;
    private readonly MarkdownMappingProfile _mappingProfile;
    private readonly MarkdownHtmlParser _html = new();
    private readonly List<MarkdownSpan> _spans = [];

    public MarkdownInlineBuilder(MarkdownOptions options, MarkdownMappingProfile mappingProfile)
    {
        _options = options;
        _mappingProfile = mappingProfile;
    }

    public IReadOnlyList<MarkdownSpan> Build(ContainerInline inline)
    {
        VisitSiblingChain(inline.FirstChild, default);
        return _spans;
    }

    private void VisitSiblingChain(Inline? inline, in MarkdownInlineState state)
    {
        for (Inline? current = inline; current != null; current = current.NextSibling)
        {
            Visit(current, state);
        }
    }

    private void Visit(Inline inline, in MarkdownInlineState state)
    {
        switch (inline)
        {
            case LiteralInline literal:
                Append(literal.Content.ToString(), state, literal);
                break;
            case CodeInline code:
                Append(code.Content.ToString(), state with { Code = true }, code);
                break;
            case LineBreakInline lineBreak:
                Append(_options.SoftBreakAsNewLine || lineBreak.IsHard ? "\n" : " ", state, lineBreak);
                break;
            case LinkInline link:
                VisitLink(link, state);
                break;
            case EmphasisInline emphasis:
                VisitEmphasis(emphasis, state);
                break;
            case AutolinkInline autolink:
                string url = autolink.Url?.ToString() ?? string.Empty;
                if (autolink.IsEmail && !url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    url = $"mailto:{url}";
                }
                Append(autolink.Url?.ToString() ?? string.Empty, state with { Url = url }, autolink);
                break;
            case HtmlEntityInline entity:
                Append(entity.Transcoded.ToString(), state, entity);
                break;
            case TaskList task:
                _spans.Add(CreateSpan(string.Empty, state, task, taskChecked: task.Checked));
                break;
            case FootnoteLink footnote:
                VisitFootnote(footnote, state);
                break;
            case HtmlInline html:
                VisitHtml(html, state);
                break;
            case ContainerInline container:
                VisitSiblingChain(container.FirstChild, state);
                break;
            default:
                Append(inline.ToString() ?? string.Empty, state, inline);
                break;
        }
    }

    private void VisitLink(LinkInline link, in MarkdownInlineState state)
    {
        string? url = link.Url?.ToString();
        string? title = link.Title?.ToString();
        int start = _spans.Count;
        VisitSiblingChain(link.FirstChild, state with { Url = url, Title = title, Image = link.IsImage });
        if (link.IsImage)
        {
            string text = string.Concat(_spans.Skip(start).Select(static span => span.Text));
            MarkdownHtmlStyle? htmlStyle = start < _spans.Count ? _spans[start].HtmlStyle : CurrentHtmlStyle;
            _spans.RemoveRange(start, _spans.Count - start);
            _spans.Add(CreateSpan(text, state with { Url = url, Title = title, Image = true }, link, htmlStyle));
            return;
        }

        for (int spanIndex = start; spanIndex < _spans.Count; spanIndex++)
        {
            MarkdownSpan span = _spans[spanIndex];
            if (span.Image)
            {
                _spans[spanIndex] = span with
                {
                    LinkUrl = url,
                    LinkTitle = title,
                    SourceStart = MarkdownParser.GetStart(link),
                    SourceLength = MarkdownParser.GetLength(link)
                };
            }
            else
            {
                _spans[spanIndex] = span with
                {
                    Url = url,
                    Title = title,
                    SourceStart = MarkdownParser.GetStart(link),
                    SourceLength = MarkdownParser.GetLength(link)
                };
            }
        }
    }

    private void VisitEmphasis(EmphasisInline emphasis, in MarkdownInlineState state)
    {
        bool asterisk = emphasis.DelimiterChar is '*' or '_';
        var nested = state with
        {
            Bold = state.Bold || asterisk && emphasis.DelimiterCount >= 2,
            Italic = state.Italic || asterisk && (emphasis.DelimiterCount == 1 || emphasis.DelimiterCount >= 3),
            Strike = state.Strike || emphasis.DelimiterChar == '~' && emphasis.DelimiterCount >= 2,
            Inserted = state.Inserted || emphasis.DelimiterChar == '+' && emphasis.DelimiterCount >= 2,
            Marked = state.Marked || emphasis.DelimiterChar == '=' && emphasis.DelimiterCount >= 2
        };
        VisitSiblingChain(emphasis.FirstChild, nested);
    }

    private void VisitHtml(HtmlInline html, in MarkdownInlineState state)
    {
        if (!_options.UseHtmlFormatting || _mappingProfile.PreserveHtmlInlineNodes ||
            !_html.TryApplyTag(html.Tag, out MarkdownHtmlToken token))
        {
            Append(html.Tag, state, html);
            return;
        }

        if (token.Kind == MarkdownHtmlTokenKind.Break)
        {
            Append("\n", state, html, token.Style);
        }
    }

    private void VisitFootnote(FootnoteLink footnote, in MarkdownInlineState state)
    {
        string order = footnote.Footnote.Order.ToString(CultureInfo.InvariantCulture);
        if (footnote.IsBackLink)
        {
            MarkdownSpan backlink = CreateSpan("↩", state with
            {
                Url = $"#fnref:{footnote.Index}",
                Title = $"Back to reference {footnote.Index}"
            }, footnote);
            _spans.Add(backlink with { SourceStart = -1, SourceLength = 0 });
            return;
        }

        MarkdownHtmlStyle current = _html.CurrentStyle;
        MarkdownHtmlStyle script = MarkdownHtmlParser.TryApplySuperscript(current, out var raised) ? raised : current;
        _spans.Add(CreateSpan(order, state with
        {
            Url = $"#fn:{order}",
            Title = $"Footnote {order}"
        }, footnote, script, anchor: $"fnref:{footnote.Index}"));
    }

    private MarkdownHtmlStyle? CurrentHtmlStyle => _html.CurrentStyle == MarkdownHtmlStyle.Empty
        ? null
        : _html.CurrentStyle;

    private void Append(string text, in MarkdownInlineState state, MarkdownObject source,
        MarkdownHtmlStyle? htmlStyle = null)
    {
        if (text.Length > 0)
        {
            _spans.Add(CreateSpan(text, state, source, htmlStyle ?? CurrentHtmlStyle));
        }
    }

    private static MarkdownSpan CreateSpan(
        string text,
        in MarkdownInlineState state,
        MarkdownObject source,
        MarkdownHtmlStyle? htmlStyle = null,
        bool? taskChecked = null,
        string? anchor = null) => new(
            text,
            state.Bold,
            state.Italic,
            state.Strike,
            state.Inserted,
            state.Marked,
            state.Code,
            state.Url,
            state.Title,
            MarkdownParser.GetStart(source),
            MarkdownParser.GetLength(source),
            state.Image,
            taskChecked,
            Node: source as Inline,
            HtmlStyle: htmlStyle,
            Anchor: anchor);
}

internal readonly record struct MarkdownInlineState(
    bool Bold,
    bool Italic,
    bool Strike,
    bool Inserted,
    bool Marked,
    bool Code,
    string? Url,
    string? Title,
    bool Image);
