// Ported from MarkView.Avalonia src/MarkView.Avalonia.Math/Parsing/BracketMathExtension.cs (MIT).

using Markdig;
using Markdig.Extensions.Mathematics;
using Markdig.Helpers;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Aprillz.MewUI.Markdown;

/// <summary>
/// Adds <see cref="BracketMathInlineParser"/> and <see cref="DollarMathInlineParser"/>, then turns each display
/// formula that has its lines to itself, <c>\[...\]</c> or <c>$$...$$</c>, into a <see cref="MathBlock"/>, splitting
/// its paragraph around it.
/// </summary>
/// <remarks>
/// A block parser could not do this: an unclosed <c>\[</c> must stay paragraph text, which is only known once the
/// paragraph has been read, and LaTeX output often puts the formula right under a line of text, without the blank
/// line a block would need.
/// </remarks>
internal sealed class BracketMathExtension : IMarkdownExtension
{
    /// <inheritdoc/>
    public void Setup(MarkdownPipelineBuilder pipeline)
    {
        // Ahead of EscapeInlineParser, which would read "\(" as an escaped parenthesis, and of Markdig's
        // dollar parser, which stops at the line end.
        pipeline.InlineParsers.Insert(0, new BracketMathInlineParser());
        pipeline.InlineParsers.Insert(0, new DollarMathInlineParser());
        pipeline.DocumentProcessed += PromoteDisplayFormulas;
    }

    /// <inheritdoc/>
    public void Setup(MarkdownPipeline pipeline, IMarkdownRenderer renderer)
    {
    }

    private static void PromoteDisplayFormulas(MarkdownDocument document)
    {
        foreach (ParagraphBlock paragraph in document.Descendants<ParagraphBlock>().ToList())
        {
            if (paragraph.Inline == null)
            {
                continue;
            }

            List<MathInline> formulas = paragraph.Inline.OfType<MathInline>().Where(HasItsLinesToItself).ToList();
            if (formulas.Count > 0)
            {
                Split(paragraph, paragraph.Inline, formulas);
            }
        }
    }

    private static bool HasItsLinesToItself(MathInline formula) =>
        formula.DelimiterCount == 2
        && (formula.PreviousSibling == null || formula.PreviousSibling is LineBreakInline)
        && (formula.NextSibling == null || formula.NextSibling is LineBreakInline);

    /// <summary>
    /// Replaces <paramref name="paragraph"/> with a block for each of <paramref name="formulas"/> and a paragraph for
    /// each run of lines around them; the lines before the first formula stay in <paramref name="paragraph"/>, which
    /// keeps its source position.
    /// </summary>
    private static void Split(ParagraphBlock paragraph, ContainerInline paragraphInline, List<MathInline> formulas)
    {
        ContainerBlock parent = paragraph.Parent!;
        int index = parent.IndexOf(paragraph);
        parent.RemoveAt(index);
        string text = formulas[0].Content.Text;
        List<Inline> inlines = paragraphInline.ToList();
        foreach (Inline inline in inlines)
        {
            inline.Remove();
        }

        ContainerInline? lines = paragraphInline;
        int line = paragraph.Line;
        for (int inlineIndex = 0; inlineIndex < inlines.Count; inlineIndex++)
        {
            if (inlines[inlineIndex] is MathInline formula && formulas.Contains(formula))
            {
                AddLines();
                (int textStart, int textEnd) = GetDelimiterOffsets(formula);
                parent.Insert(index++, ToBlock(formula, paragraph.Line + LineCount(textStart)));
                line = paragraph.Line + LineCount(textEnd) + 1;
                // Past the line break after the formula.
                inlineIndex++;
            }
            else
            {
                lines ??= new ContainerInline();
                lines.AppendChild(inlines[inlineIndex]);
            }
        }
        AddLines();

        void AddLines()
        {
            if (lines?.LastChild is LineBreakInline lineBreak)
            {
                // The line break before the formula.
                lineBreak.Remove();
            }

            Inline? first = lines?.FirstChild;
            if (lines != null && first != null)
            {
                ParagraphBlock block = lines == paragraphInline
                    ? paragraph
                    : new ParagraphBlock(paragraph.Parser)
                    {
                        Inline = lines,
                        Span = new SourceSpan(first.Span.Start, lines.LastChild!.Span.End),
                        Line = line,
                        Column = first.Column,
                    };
                parent.Insert(index++, block);
            }
            lines = null;
        }

        int LineCount(int offset) => text.AsSpan(0, offset).Count('\n');
    }

    /// <summary>Returns the offsets of the opening and closing delimiter in the paragraph text the formula's content is a slice of.</summary>
    private static (int Start, int End) GetDelimiterOffsets(MathInline formula)
    {
        if (formula is BracketMathInline bracket)
        {
            return (bracket.TextStart, bracket.TextEnd);
        }

        // Markdig's content excludes the dollars and the blank it trims inside them.
        return (Math.Max(0, formula.Content.Start - formula.DelimiterCount), formula.Content.End + 1);
    }

    private static MathBlock ToBlock(MathInline formula, int line)
    {
        MathBlock block = formula is BracketMathInline && formula.Delimiter != '$' ? new BracketMathBlock() : new MathBlock(null!);
        block.Span = formula.Span;
        block.Line = line;
        block.Column = formula.Column;
        block.Lines = new StringLineGroup(1);

        // One line per source line without its line ending, as Markdig stores the lines of a $$ block.
        StringSlice content = formula.Content;
        string text = content.Text;
        int lineStart = content.Start;
        for (int index = content.Start; index < content.End; index++)
        {
            if (text[index] != '\n')
            {
                continue;
            }

            int lineEnd = text[index - 1] == '\r' ? index - 2 : index - 1;
            block.Lines.Add(new StringSlice(text, lineStart, lineEnd, NewLine.LineFeed));
            lineStart = index + 1;
        }
        block.Lines.Add(new StringSlice(text, lineStart, content.End));
        return block;
    }
}
