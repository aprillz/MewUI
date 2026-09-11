using System.Runtime.CompilerServices;
using System.Text;
using Aprillz.MewUI;
using Markdig;
using Markdig.Extensions.DefinitionLists;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Footnotes;
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
    FootnoteGroup,
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
    Inline? Node = null,
    MarkdownHtmlStyle? HtmlStyle = null,
    string? Anchor = null);

internal readonly record struct MarkdownMappingProfile(
    bool PreserveHtmlInlineNodes = false,
    bool PreserveHtmlBlockNodes = false);

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
    public IReadOnlyList<string> Aliases { get; init; } = [];
    // The Markdig node this block was mapped from, consulted by user renderers.
    public Block? Node { get; set; }
}

/// <summary>A selectable leaf block: its display text, the separator that precedes it when text is joined, the top-level block it lives in and whether it carries links, and the list marker copied before it when its item starts inside the selection.</summary>
internal sealed record MarkdownTextUnit(MarkdownBlock Block, string Text, string Separator, int TopIndex, bool HasLinks, string ListMarkerPrefix = "");

/// <summary>Parse output: the mapped block tree plus the anchor and source bookkeeping renderers need.</summary>
internal sealed class ParsedMarkdown
{
    internal ParsedMarkdown(IReadOnlyList<MarkdownBlock> blocks, string source, MarkdownOptions options,
        MarkdownMappingProfile mappingProfile, HashSet<string> anchors)
    {
        Blocks = blocks;
        Source = source;
        Options = options;
        MappingProfile = mappingProfile;
        Anchors = anchors;
        Dictionary<string, int> anchorBlocks = new(StringComparer.OrdinalIgnoreCase);
        List<MarkdownTextUnit> units = [];
        Dictionary<MarkdownBlock, int> unitIndex = new(ReferenceEqualityComparer.Instance);
        for (int index = 0; index < blocks.Count; index++)
        {
            CollectAnchors(blocks[index], index, anchorBlocks);
            CollectTextUnits(blocks[index], "\n", index, units, unitIndex);
        }
        AnchorBlocks = anchorBlocks;
        TextUnits = units;
        _unitIndex = unitIndex;
    }

    private readonly Dictionary<MarkdownBlock, int> _unitIndex;

    public IReadOnlyList<MarkdownBlock> Blocks { get; }
    public string Source { get; }
    public MarkdownOptions Options { get; }
    public MarkdownMappingProfile MappingProfile { get; }
    public HashSet<string> Anchors { get; }
    /// <summary>Maps each anchor to the index of the top-level block containing it.</summary>
    public IReadOnlyDictionary<string, int> AnchorBlocks { get; }
    /// <summary>Selectable text blocks in document order; selection anchors and copied text address these.</summary>
    public IReadOnlyList<MarkdownTextUnit> TextUnits { get; }

    /// <summary>Returns the text unit index of a leaf block, or -1 for blocks without selectable text.</summary>
    public int GetTextUnit(MarkdownBlock block) => _unitIndex.TryGetValue(block, out int index) ? index : -1;

    // Walks the model in the same order the presenter renders it, so realized elements and copied
    // text agree on unit numbering. The separator is what precedes the unit when text is joined.
    private static void CollectTextUnits(MarkdownBlock block, string separator, int topIndex, List<MarkdownTextUnit> units, Dictionary<MarkdownBlock, int> unitIndex)
    {
        switch (block.Kind)
        {
            case MarkdownBlockKind.Paragraph:
            case MarkdownBlockKind.Heading:
            case MarkdownBlockKind.DefinitionTerm:
            case MarkdownBlockKind.Code:
                unitIndex[block] = units.Count;
                bool hasLinks = block.Spans.Any(static span => VisualText(span).Length > 0 && (span.Image ? span.LinkUrl : span.Url) != null);
                units.Add(new MarkdownTextUnit(block, string.Concat(block.Spans.Select(VisualText)), separator, topIndex, hasLinks));
                break;
            case MarkdownBlockKind.Table:
                for (int rowIndex = 0; rowIndex < block.Children.Count; rowIndex++)
                {
                    MarkdownBlock row = block.Children[rowIndex];
                    for (int cellIndex = 0; cellIndex < row.Children.Count; cellIndex++)
                    {
                        string cellSeparator = cellIndex > 0 ? "\t" : rowIndex > 0 ? "\n" : separator;
                        foreach (MarkdownBlock child in row.Children[cellIndex].Children)
                        {
                            CollectTextUnits(child, cellSeparator, topIndex, units, unitIndex);
                            cellSeparator = "\n";
                        }
                    }
                }
                break;
            case MarkdownBlockKind.ListItem:
                int firstUnit = units.Count;
                foreach (MarkdownBlock child in block.Children)
                {
                    CollectTextUnits(child, separator, topIndex, units, unitIndex);
                }
                string marker = GetListMarkerText(block);
                if (marker.Length > 0 && firstUnit < units.Count)
                {
                    // An item that opens with a nested list keeps the inner marker after its own.
                    units[firstUnit] = units[firstUnit] with { ListMarkerPrefix = marker + "\t" + units[firstUnit].ListMarkerPrefix };
                }
                break;
            default:
                foreach (MarkdownBlock child in block.Children)
                {
                    CollectTextUnits(child, separator, topIndex, units, unitIndex);
                }
                break;
        }
    }

    /// <summary>Returns the marker a list item displays: nothing for a task item, a bullet when the item carries no marker.</summary>
    internal static string GetListMarkerText(MarkdownBlock item)
        => item.Children.FirstOrDefault()?.Spans.Any(static span => span.TaskChecked.HasValue) == true
            ? string.Empty
            : string.IsNullOrWhiteSpace(item.Marker) ? "•" : item.Marker;

    // Mirrors MarkdownParagraph's visual text so offsets match the laid-out text.
    private static string VisualText(MarkdownSpan span) => span.Image && span.Text.Length == 0 ? ((char)0xFFFC).ToString() : span.Text;

    private static void CollectAnchors(MarkdownBlock block, int topIndex, Dictionary<string, int> anchorBlocks)
    {
        if (!string.IsNullOrEmpty(block.Anchor))
        {
            anchorBlocks.TryAdd(block.Anchor, topIndex);
        }
        foreach (string alias in block.Aliases)
        {
            anchorBlocks.TryAdd(alias, topIndex);
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

    internal static IReadOnlyList<MarkdownBlock> Parse(string markdown, MarkdownOptions options) =>
        ParseDocument(markdown, options, default).Blocks;

    internal static ParsedMarkdown ParseDocument(
        string markdown,
        MarkdownOptions options,
        MarkdownMappingProfile mappingProfile = default)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(options);

        MarkdownPipeline pipeline = _pipelines.GetValue(options, static value => BuildPipeline(value));
        MarkdownDocument document = global::Markdig.Markdown.Parse(markdown, pipeline);
        HashSet<string> anchors = new(StringComparer.Ordinal);
        List<MarkdownBlock> blocks = MapChildren(document, markdown, options, mappingProfile, anchors);
        return new ParsedMarkdown(blocks, markdown, options, mappingProfile, anchors);
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

        if (options.UseFootnotes)
        {
            builder.UseFootnotes().UsePreciseSourceLocation();
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

    private static MarkdownBlock? MapBlock(Block block, string source, MarkdownOptions options,
        MarkdownMappingProfile mappingProfile, HashSet<string> anchors, TextAlignment alignment = TextAlignment.Left)
    {
        MarkdownBlock? mapped = MapBlockCore(block, source, options, mappingProfile, anchors, alignment);
        if (mapped is not null)
        {
            mapped.Node = block;
        }
        return mapped;
    }

    private static MarkdownBlock? MapBlockCore(Block block, string source, MarkdownOptions options,
        MarkdownMappingProfile mappingProfile, HashSet<string> anchors, TextAlignment alignment)
    {
        switch (block)
        {
            case HeadingBlock heading:
                IReadOnlyList<MarkdownSpan> headingSpans = Flatten(heading.Inline, options, mappingProfile);
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Heading,
                    Level = heading.Level,
                    Header = true,
                    Alignment = alignment,
                    Spans = headingSpans,
                    Aliases = GetInlineAnchors(headingSpans),
                    Anchor = GetAnchor(heading, options, mappingProfile, anchors)
                };
            case QuoteBlock quote:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Quote,
                    Children = MapChildren(quote, source, options, mappingProfile, anchors)
                };
            case ListBlock list:
                IReadOnlyList<MarkdownBlock> listChildren = MapChildren(list, source, options, mappingProfile, anchors);
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
                            Aliases = child.Aliases,
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
                    Children = MapChildren(item, source, options, mappingProfile, anchors)
                };
            case FencedCodeBlock fenced:
                return CreateCodeBlock(fenced, source, fenced.Info);
            case CodeBlock code:
                return CreateCodeBlock(code, source, null);
            case ThematicBreakBlock:
                return new MarkdownBlock { Kind = MarkdownBlockKind.Rule };
            case Table table:
                return MapTable(table, source, options, mappingProfile, anchors);
            case TableRow row:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.TableRow,
                    Header = row.IsHeader,
                    Children = MapChildren(row, source, options, mappingProfile, anchors)
                };
            case TableCell cell:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.TableCell,
                    Children = MapChildren(cell, source, options, mappingProfile, anchors, alignment),
                    Alignment = alignment
                };
            case DefinitionList definitionList:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.DefinitionList,
                    Children = MapChildren(definitionList, source, options, mappingProfile, anchors)
                };
            case DefinitionItem definitionItem:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.DefinitionItem,
                    Children = MapChildren(definitionItem, source, options, mappingProfile, anchors)
                };
            case DefinitionTerm definitionTerm:
                IReadOnlyList<MarkdownSpan> termSpans = Flatten(definitionTerm.Inline, options, mappingProfile);
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.DefinitionTerm,
                    Spans = termSpans,
                    Aliases = GetInlineAnchors(termSpans)
                };
            case FootnoteGroup footnoteGroup:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.FootnoteGroup,
                    Children = MapChildren(footnoteGroup, source, options, mappingProfile, anchors)
                };
            case Footnote footnote:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.ListItem,
                    Marker = $"{footnote.Order}.",
                    Anchor = $"fn:{footnote.Order}",
                    Children = MapChildren(footnote, source, options, mappingProfile, anchors)
                };
            case LinkReferenceDefinitionGroup:
                return null;
            case HtmlBlock html:
                if (options.UseHtmlFormatting &&
                    MarkdownHtmlParser.TryParseBlock(GetSourceText(html, source), out MarkdownHtmlBlock htmlBlock))
                {
                    if (htmlBlock.Text.Length == 0 &&
                        htmlBlock.Spans.Length == 0 &&
                        !mappingProfile.PreserveHtmlBlockNodes)
                    {
                        return null;
                    }

                    int sourceStart = GetStart(html);
                    int sourceLength = GetLength(html);
                    return new MarkdownBlock
                    {
                        Kind = MarkdownBlockKind.Paragraph,
                        Spans = htmlBlock.Spans.Select(span => new MarkdownSpan(
                            htmlBlock.Text.Substring(span.Start, span.Length),
                            SourceStart: sourceStart,
                            SourceLength: sourceLength,
                            HtmlStyle: span.Style == MarkdownHtmlStyle.Empty ? null : span.Style)).ToArray()
                    };
                }
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Paragraph,
                    Spans = [RawSpan(html, source)]
                };
            case LeafBlock leaf when leaf.Inline is not null:
                IReadOnlyList<MarkdownSpan> leafSpans = Flatten(leaf.Inline, options, mappingProfile);
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Paragraph,
                    Alignment = alignment,
                    Spans = leafSpans,
                    Aliases = GetInlineAnchors(leafSpans)
                };
            case ContainerBlock container:
                return new MarkdownBlock
                {
                    Kind = MarkdownBlockKind.Group,
                    Children = MapChildren(container, source, options, mappingProfile, anchors)
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

    internal static List<MarkdownBlock> MapChildren(ContainerBlock container, string source, MarkdownOptions options,
        MarkdownMappingProfile mappingProfile, HashSet<string> anchors, TextAlignment alignment = TextAlignment.Left)
    {
        List<MarkdownBlock> children = [];
        foreach (Block child in container)
        {
            MarkdownBlock? mapped = MapBlock(child, source, options, mappingProfile, anchors, alignment);
            if (mapped is not null)
            {
                children.Add(mapped);
            }
        }

        return children;
    }

    private static MarkdownBlock MapTable(Table table, string source, MarkdownOptions options,
        MarkdownMappingProfile mappingProfile, HashSet<string> anchors)
    {
        List<MarkdownBlock> rows = [];
        foreach (Block child in table)
        {
            if (child is not TableRow row)
            {
                MarkdownBlock? mappedChild = MapBlock(child, source, options, mappingProfile, anchors);
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
                    MarkdownBlock? mappedCell = MapBlock(cell, source, options, mappingProfile, anchors, alignment);
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

    internal static IReadOnlyList<MarkdownSpan> Flatten(
        ContainerInline? inline,
        MarkdownOptions options,
        MarkdownMappingProfile mappingProfile = default)
    {
        if (inline is null)
        {
            return [];
        }

        return new MarkdownInlineBuilder(options, mappingProfile).Build(inline);
    }

    private static MarkdownSpan RawSpan(MarkdownObject block, string source)
    {
        int start = GetStart(block);
        int length = GetLength(block);
        return new MarkdownSpan(GetSourceText(block, source), SourceStart: start, SourceLength: length);
    }

    private static IReadOnlyList<string> GetInlineAnchors(IReadOnlyList<MarkdownSpan> spans)
        => spans.Where(static span => span.Anchor != null)
            .Select(static span => span.Anchor!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

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

    private static string? GetAnchor(HeadingBlock heading, MarkdownOptions options,
        MarkdownMappingProfile mappingProfile, HashSet<string> anchors)
    {
        IReadOnlyList<MarkdownSpan> spans = Flatten(heading.Inline, options, mappingProfile);
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

    internal static int GetStart(MarkdownObject source)
    {
        return source.Span.Start;
    }

    internal static int GetLength(MarkdownObject source)
    {
        return source.Span.End >= source.Span.Start ? source.Span.End - source.Span.Start + 1 : 0;
    }
}
