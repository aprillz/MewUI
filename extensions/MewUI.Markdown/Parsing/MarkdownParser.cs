using System.Runtime.CompilerServices;
using System.Text;
using Aprillz.MewUI;
using Markdig;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Aprillz.MewUI.Markdown;

internal enum MarkdownBlockKind
{
    Paragraph,
    Heading,
    Quote,
    List,
    ListItem,
    Code,
    Rule,
    Table,
    TableRow,
    TableCell,
    DefinitionList,
    DefinitionItem,
    DefinitionTerm,
    Group
}

internal sealed record MarkdownSpan(
    string Text,
    bool Bold = false,
    bool Italic = false,
    bool Strike = false,
    bool Inserted = false,
    bool Marked = false,
    bool Code = false,
    string? Url = null,
    string? Title = null,
    int SourceStart = -1,
    int SourceLength = 0,
    bool Image = false,
    bool? TaskChecked = null,
    string? LinkUrl = null,
    string? LinkTitle = null,
    Inline? Node = null);

internal sealed class MarkdownBlock
{
    public MarkdownBlockKind Kind { get; init; }
    public IReadOnlyList<MarkdownSpan> Spans { get; init; } = [];
    public IReadOnlyList<MarkdownBlock> Children { get; init; } = [];
    public int Level { get; init; }
    public string? Info { get; init; }
    public string? Marker { get; init; }
    public bool Header { get; init; }
    public bool Loose { get; init; }
    public TextAlignment Alignment { get; init; }
    public string? Anchor { get; init; }
    // The Markdig node this block was mapped from, consulted by user renderers.
    public Block? Node { get; set; }
}

/// <summary>Parse output: the mapped block tree plus the anchor and source bookkeeping renderers need.</summary>
internal sealed class ParsedMarkdown
{
    internal ParsedMarkdown(IReadOnlyList<MarkdownBlock> blocks, string source, MarkdownOptions options, HashSet<string> anchors)
    {
        Blocks = blocks;
        Source = source;
        Options = options;
        Anchors = anchors;
        Dictionary<string, int> anchorBlocks = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < blocks.Count; index++)
        {
            CollectAnchors(blocks[index], index, anchorBlocks);
        }
        AnchorBlocks = anchorBlocks;
    }

    public IReadOnlyList<MarkdownBlock> Blocks { get; }
    public string Source { get; }
    public MarkdownOptions Options { get; }
    public HashSet<string> Anchors { get; }
    /// <summary>Maps each anchor to the index of the top-level block containing it.</summary>
    public IReadOnlyDictionary<string, int> AnchorBlocks { get; }

    private static void CollectAnchors(MarkdownBlock block, int topIndex, Dictionary<string, int> anchorBlocks)
    {
        if (!string.IsNullOrEmpty(block.Anchor))
        {
            anchorBlocks.TryAdd(block.Anchor, topIndex);
        }
        foreach (MarkdownBlock child in block.Children)
        {
            CollectAnchors(child, topIndex, anchorBlocks);
        }
    }
}

internal static class MarkdownParser
{
    // Weak keys: options carrying a ConfigurePipeline delegate are rarely equal across instances,
    // so a strong cache would grow with every assignment.
    private static readonly ConditionalWeakTable<MarkdownOptions, MarkdownPipeline> _pipelines = [];

    internal static IReadOnlyList<MarkdownBlock> Parse(string markdown, MarkdownOptions options) => ParseDocument(markdown, options).Blocks;

    internal static ParsedMarkdown ParseDocument(string markdown, MarkdownOptions options)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(options);

        MarkdownPipeline pipeline = _pipelines.GetValue(options, static value => BuildPipeline(value));
        MarkdownDocument document = global::Markdig.Markdown.Parse(markdown, pipeline);
        HashSet<string> anchors = new(StringComparer.Ordinal);
        List<MarkdownBlock> blocks = MapChildren(document, markdown, options, anchors);
        return new ParsedMarkdown(blocks, markdown, options, anchors);
    }

    private static MarkdownPipeline BuildPipeline(MarkdownOptions options)
    {
        MarkdownPipelineBuilder builder = new();
        options.ConfigurePipeline?.Invoke(builder);
        if (options.UsePipeTables)
        {
            builder.UsePipeTables();
        }

        if (options.UseTaskLists)
        {
            builder.UseTaskLists();
        }

        if (options.UseAutoLinks)
        {
            builder.UseAutoLinks();
        }

        if (options.UseDefinitionLists)
        {
            builder.UseDefinitionLists();
        }

        EmphasisExtraOptions emphasisOptions = 0;
        if (options.UseStrikethrough)
        {
            emphasisOptions |= EmphasisExtraOptions.Strikethrough;
        }
        if (options.UseInserted)
        {
            emphasisOptions |= EmphasisExtraOptions.Inserted;
        }
        if (options.UseMarked)
        {
            emphasisOptions |= EmphasisExtraOptions.Marked;
        }
        if (emphasisOptions != 0)
        {
            builder.UseEmphasisExtras(emphasisOptions);
        }

        return builder.Build();
    }

    private static MarkdownBlock? MapBlock(Block block, string source, MarkdownOptions options, HashSet<string> anchors, TextAlignment alignment = TextAlignment.Left)
    {
        MarkdownBlock? mapped = MapBlockCore(block, source, options, anchors, alignment);
        if (mapped is not null)
        {
            mapped.Node = block;
        }
        return mapped;
    }

    private static MarkdownBlock? MapBlockCore(Block block, string source, MarkdownOptions options, HashSet<string> anchors, TextAlignment alignment)
    {
        switch (block)
        {
            case HeadingBlock heading:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Heading,
                    Level = heading.Level,
                    Header = true,
                    Alignment = alignment,
                    Spans = Flatten(heading.Inline, options),
                    Anchor = GetAnchor(heading, options, anchors)
                };
            case QuoteBlock quote:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Quote,
                    Children = MapChildren(quote, source, options, anchors)
                };
            case ListBlock list:
                IReadOnlyList<MarkdownBlock> listChildren = MapChildren(list, source, options, anchors);
                string orderedStartText = list.OrderedStart ?? string.Empty;
                int orderedStartValue = int.TryParse(orderedStartText, out int parsedOrderedStart) ? parsedOrderedStart : 0;
                if (list.IsOrdered)
                {
                    List<MarkdownBlock> numberedChildren = [];
                    for (int childIndex = 0; childIndex < listChildren.Count; childIndex++)
                    {
                        MarkdownBlock child = listChildren[childIndex];
                        numberedChildren.Add(new MarkdownBlock
                        {
                            Kind = child.Kind,
                            Spans = child.Spans,
                            Children = child.Children,
                            Level = child.Level,
                            Info = child.Info,
                            Marker = $"{orderedStartValue + childIndex}{list.OrderedDelimiter}",
                            Header = child.Header,
                            Loose = child.Loose,
                            Alignment = child.Alignment,
                            Anchor = child.Anchor,
                            Node = child.Node
                        });
                    }
                    listChildren = numberedChildren;
                }
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.List,
                    Level = list.IsOrdered ? orderedStartValue : 0,
                    Marker = list.IsOrdered ? list.OrderedDelimiter.ToString() : list.BulletType.ToString(),
                    Loose = list.IsLoose,
                    Children = listChildren
                };
            case ListItemBlock item:
                int itemOrder = int.TryParse(item.Order.ToString(), out int parsedOrder) ? parsedOrder : 0;
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.ListItem,
                    Level = itemOrder,
                    Marker = item.SourceBullet.ToString(),
                    Children = MapChildren(item, source, options, anchors)
                };
            case FencedCodeBlock fenced:
                return CreateCodeBlock(fenced, source, fenced.Info);
            case CodeBlock code:
                return CreateCodeBlock(code, source, null);
            case ThematicBreakBlock:
                return new MarkdownBlock { Kind = MarkdownBlockKind.Rule };
            case Table table:
                return MapTable(table, source, options, anchors);
            case TableRow row:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.TableRow,
                    Header = row.IsHeader,
                    Children = MapChildren(row, source, options, anchors)
                };
            case TableCell cell:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.TableCell,
                    Children = MapChildren(cell, source, options, anchors, alignment),
                    Alignment = alignment
                };
            case DefinitionList definitionList:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.DefinitionList,
                    Children = MapChildren(definitionList, source, options, anchors)
                };
            case DefinitionItem definitionItem:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.DefinitionItem,
                    Children = MapChildren(definitionItem, source, options, anchors)
                };
            case DefinitionTerm definitionTerm:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.DefinitionTerm,
                    Spans = Flatten(definitionTerm.Inline, options)
                };
            case HtmlBlock html:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Paragraph,
                    Spans = [RawSpan(html, source)]
                };
            case LeafBlock leaf when leaf.Inline is not null:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Paragraph,
                    Alignment = alignment,
                    Spans = Flatten(leaf.Inline, options)
                };
            case ContainerBlock container:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Group,
                    Children = MapChildren(container, source, options, anchors)
                };
            default:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Paragraph,
                    Spans = [RawSpan(block, source)]
                };
        }
    }

    private static MarkdownBlock CreateCodeBlock(CodeBlock block, string source, string? info)
    {
        string text = GetCodeText(block, source);
        return new MarkdownBlock
        {
            Kind = MarkdownBlockKind.Code,
            Info = info,
            Spans = [new MarkdownSpan(text, Code: true, SourceStart: GetStart(block), SourceLength: GetLength(block))]
        };
    }

    internal static List<MarkdownBlock> MapChildren(ContainerBlock container, string source, MarkdownOptions options, HashSet<string> anchors, TextAlignment alignment = TextAlignment.Left)
    {
        List<MarkdownBlock> children = [];
        foreach (Block child in container)
        {
            MarkdownBlock? mapped = MapBlock(child, source, options, anchors, alignment);
            if (mapped is not null)
            {
                children.Add(mapped);
            }
        }

        return children;
    }

    private static MarkdownBlock MapTable(Table table, string source, MarkdownOptions options, HashSet<string> anchors)
    {
        List<MarkdownBlock> rows = [];
        foreach (Block child in table)
        {
            if (child is not TableRow row)
            {
                MarkdownBlock? mappedChild = MapBlock(child, source, options, anchors);
                if (mappedChild is not null)
                {
                    rows.Add(mappedChild);
                }
                continue;
            }

            List<MarkdownBlock> cells = [];
            foreach (Block rowChild in row)
            {
                if (rowChild is TableCell cell)
                {
                    TextAlignment alignment = GetTableAlignment(table, cells.Count);
                    MarkdownBlock? mappedCell = MapBlock(cell, source, options, anchors, alignment);
                    if (mappedCell is not null)
                    {
                        cells.Add(mappedCell);
                    }
                }
            }

            rows.Add(new MarkdownBlock
            {
                Kind = MarkdownBlockKind.TableRow,
                Header = row.IsHeader,
                Children = cells
            });
        }

        return new MarkdownBlock
        {
            Kind = MarkdownBlockKind.Table,
            Children = rows
        };
    }

    private static TextAlignment GetTableAlignment(Table table, int columnIndex)
    {
        if (columnIndex < 0 || columnIndex >= table.ColumnDefinitions.Count)
        {
            return TextAlignment.Left;
        }

        return table.ColumnDefinitions[columnIndex].Alignment switch
        {
            TableColumnAlign.Center => TextAlignment.Center,
            TableColumnAlign.Right => TextAlignment.Right,
            _ => TextAlignment.Left
        };
    }

    internal static IReadOnlyList<MarkdownSpan> Flatten(ContainerInline? inline, MarkdownOptions options)
    {
        if (inline is null)
        {
            return [];
        }

        List<MarkdownSpan> spans = [];
        AppendInline(inline.FirstChild, spans, false, false, false, false, false, false, null, null, false, options);
        return spans;
    }

    private static void AppendInline(
        Inline? inline,
        List<MarkdownSpan> spans,
        bool bold,
        bool italic,
        bool strike,
        bool inserted,
        bool marked,
        bool code,
        string? url,
        string? title,
        bool image,
        MarkdownOptions options)
    {
        Inline? current = inline;
        while (current is not null)
        {
            switch (current)
            {
                case LiteralInline literal:
                    AddSpan(spans, literal.Content.ToString(), bold, italic, strike, inserted, marked, code, url, title, image, literal);
                    break;
                case CodeInline codeInline:
                    AddSpan(spans, codeInline.Content.ToString(), bold, italic, strike, inserted, marked, true, url, title, image, codeInline);
                    break;
                case LineBreakInline lineBreak:
                    AddSpan(spans, options.SoftBreakAsNewLine || lineBreak.IsHard ? "\n" : " ", bold, italic, strike, inserted, marked, code, url, title, image, lineBreak);
                    break;
                case LinkInline link:
                    string? linkUrl = link.Url?.ToString();
                    string? linkTitle = link.Title?.ToString();
                    int linkStart = spans.Count;
                    AppendInline(link.FirstChild, spans, bold, italic, strike, inserted, marked, code, linkUrl, linkTitle, link.IsImage, options);
                    if (link.IsImage)
                    {
                        string imageText = string.Concat(spans.Skip(linkStart).Select(static span => span.Text));
                        spans.RemoveRange(linkStart, spans.Count - linkStart);
                        spans.Add(new MarkdownSpan(
                            imageText,
                            bold,
                            italic,
                            strike,
                            inserted,
                            marked,
                            code,
                            linkUrl,
                            linkTitle,
                            GetStart(link),
                            GetLength(link),
                            true));
                    }
                    else
                    {
                        for (int spanIndex = linkStart; spanIndex < spans.Count; spanIndex++)
                        {
                            MarkdownSpan span = spans[spanIndex];
                            if (span.Image)
                            {
                                spans[spanIndex] = span with
                                {
                                    LinkUrl = linkUrl,
                                    LinkTitle = linkTitle,
                                    SourceStart = GetStart(link),
                                    SourceLength = GetLength(link)
                                };
                            }
                            else
                            {
                                spans[spanIndex] = span with
                                {
                                    Url = linkUrl,
                                    Title = linkTitle,
                                    SourceStart = GetStart(link),
                                    SourceLength = GetLength(link)
                                };
                            }
                        }
                    }
                    break;
                case EmphasisInline emphasis:
                    bool emphasisBold = bold || ((emphasis.DelimiterChar == '*' || emphasis.DelimiterChar == '_') && emphasis.DelimiterCount >= 2);
                    bool emphasisItalic = italic || ((emphasis.DelimiterChar == '*' || emphasis.DelimiterChar == '_') && (emphasis.DelimiterCount == 1 || emphasis.DelimiterCount >= 3));
                    bool emphasisStrike = strike || (emphasis.DelimiterChar == '~' && emphasis.DelimiterCount >= 2);
                    bool emphasisInserted = inserted || (emphasis.DelimiterChar == '+' && emphasis.DelimiterCount >= 2);
                    bool emphasisMarked = marked || (emphasis.DelimiterChar == '=' && emphasis.DelimiterCount >= 2);
                    AppendInline(emphasis.FirstChild, spans, emphasisBold, emphasisItalic, emphasisStrike, emphasisInserted, emphasisMarked, code, url, title, image, options);
                    break;
                case AutolinkInline autolink:
                    string autoLinkUrl = autolink.Url?.ToString() ?? string.Empty;
                    if (autolink.IsEmail && !autoLinkUrl.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                    {
                        autoLinkUrl = $"mailto:{autoLinkUrl}";
                    }
                    AddSpan(spans, autolink.Url?.ToString() ?? string.Empty, bold, italic, strike, inserted, marked, code, autoLinkUrl, title, image, autolink);
                    break;
                case HtmlEntityInline entity:
                    AddSpan(spans, entity.Transcoded.ToString(), bold, italic, strike, inserted, marked, code, url, title, image, entity);
                    break;
                case TaskList task:
                    spans.Add(new MarkdownSpan(string.Empty, bold, italic, strike, inserted, marked, code,
                        url, title, GetStart(task), GetLength(task), image, task.Checked));
                    break;
                case HtmlInline html:
                    AddSpan(spans, html.Tag, bold, italic, strike, inserted, marked, code, url, title, image, html);
                    break;
                case ContainerInline container:
                    AppendInline(container.FirstChild, spans, bold, italic, strike, inserted, marked, code, url, title, image, options);
                    break;
                default:
                    AddSpan(spans, current.ToString() ?? string.Empty, bold, italic, strike, inserted, marked, code, url, title, image, current);
                    break;
            }

            current = current.NextSibling;
        }
    }

    private static void AddSpan(
        List<MarkdownSpan> spans,
        string text,
        bool bold,
        bool italic,
        bool strike,
        bool inserted,
        bool marked,
        bool code,
        string? url,
        string? title,
        bool image,
        MarkdownObject source)
    {
        if (text.Length == 0)
        {
            return;
        }

        spans.Add(new MarkdownSpan(text, bold, italic, strike, inserted, marked, code, url, title,
            GetStart(source), GetLength(source), image, Node: source as Inline));
    }

    private static MarkdownSpan RawSpan(MarkdownObject block, string source)
    {
        int start = GetStart(block);
        int length = GetLength(block);
        return new MarkdownSpan(GetSourceText(block, source), SourceStart: start, SourceLength: length);
    }

    /// <summary>Returns the source slice a node was parsed from, or its string form when the span is unavailable.</summary>
    internal static string GetSourceText(MarkdownObject node, string source)
    {
        int start = GetStart(node);
        int length = GetLength(node);
        return start >= 0 && length > 0 && start + length <= source.Length
            ? source.Substring(start, length)
            : node.ToString() ?? string.Empty;
    }

    private static string GetCodeText(CodeBlock block, string source)
    {
        return block.Lines.ToString();
    }

    private static string? GetAnchor(HeadingBlock heading, MarkdownOptions options, HashSet<string> anchors)
    {
        IReadOnlyList<MarkdownSpan> spans = Flatten(heading.Inline, options);
        string text = string.Concat(spans.Select(static span => span.Text));
        StringBuilder slugBuilder = new();
        bool pendingSeparator = false;
        foreach (char character in text.Normalize(NormalizationForm.FormKC).ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                if (pendingSeparator && slugBuilder.Length > 0)
                {
                    slugBuilder.Append('-');
                }
                slugBuilder.Append(character);
                pendingSeparator = false;
            }
            else
            {
                pendingSeparator = true;
            }
        }

        string baseAnchor = slugBuilder.Length == 0 ? "section" : slugBuilder.ToString();
        string anchor = baseAnchor;
        int suffix = 1;
        while (!anchors.Add(anchor))
        {
            suffix++;
            anchor = $"{baseAnchor}-{suffix}";
        }

        return anchor;
    }

    private static int GetStart(MarkdownObject source)
    {
        return source.Span.Start;
    }

    private static int GetLength(MarkdownObject source)
    {
        return source.Span.End >= source.Span.Start ? source.Span.End - source.Span.Start + 1 : 0;
    }
}
