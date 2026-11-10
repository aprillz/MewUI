using System.Text;
using Aprillz.MewUI.Controls;
using Markdig;
using Markdig.Extensions.Mathematics;

namespace Aprillz.MewUI.Markdown;

/// <summary>Turns on LaTeX math for the Markdown controls: the syntax on the pipeline, the drawing on the renderers.</summary>
/// <example>
/// <code>
/// viewer.Options = new MarkdownOptions { ConfigurePipeline = static builder => builder.UseMath() };
/// viewer.Renderers = new MarkdownRenderers().UseMath();
/// </code>
/// </example>
public static class MarkdownMathExtensions
{
    /// <summary>
    /// Recognizes <c>$...$</c> and <c>$$...$$</c>, and with <paramref name="bracketDelimiters"/> also <c>\(...\)</c>
    /// and <c>\[...\]</c>, as math.
    /// </summary>
    public static MarkdownPipelineBuilder UseMath(this MarkdownPipelineBuilder builder, bool bracketDelimiters = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseMathematics();
        if (bracketDelimiters)
        {
            builder.UseMathBracketDelimiters();
        }
        return builder;
    }

    /// <summary>
    /// Draws <c>MathInline</c> nodes in the paragraph text and <c>MathBlock</c> nodes as centered display formulas, in
    /// the presenter's font size and foreground; a formula past CSharpMath's safety limits stays source text.
    /// Typeset formulas are kept with the renderers, so rebuilding a document does not typeset its formulas again.
    /// </summary>
    public static MarkdownRenderers UseMath(this MarkdownRenderers renderers)
    {
        ArgumentNullException.ThrowIfNull(renderers);
        MathFormulaCache formulas = new();
        renderers.RegisterInline<MathInline>((inline, context) =>
        {
            // A formula spanning lines inside a paragraph keeps the document's line endings.
            string source = inline.Content.ToString().ReplaceLineEndings("\n");
            // Two delimiters ("$$" or "\[") ask for display style even when the formula shares its line with text.
            bool display = inline.DelimiterCount == 2;
            return MathFormula.IsSafe(source) ? new MathInlineObject(formulas.Get(source, display), context.Presenter) : null;
        });
        renderers.RegisterBlock<MathBlock>((block, _) =>
        {
            string source = GetSource(block);
            return MathFormula.IsSafe(source) ? new MathBlockElement(formulas.Get(source, display: true)) { Source = source } : null;
        });
        return renderers;
    }

    /// <summary>Joins the block's lines with <c>'\n'</c>, as Markdig stores them without their line endings.</summary>
    private static string GetSource(MathBlock block)
    {
        if (block.Lines.Lines == null)
        {
            return string.Empty;
        }

        StringBuilder builder = new();
        for (int index = 0; index < block.Lines.Count; index++)
        {
            if (index > 0)
            {
                builder.Append('\n');
            }
            builder.Append(block.Lines.Lines[index].Slice.AsSpan());
        }
        return builder.ToString();
    }
}
