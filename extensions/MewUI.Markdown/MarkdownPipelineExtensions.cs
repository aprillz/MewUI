using Markdig;

namespace Aprillz.MewUI.Markdown;

/// <summary>Provides Markdig pipeline extensions for <see cref="MarkdownOptions.ConfigurePipeline"/>.</summary>
public static class MarkdownPipelineExtensions
{
    /// <summary>
    /// Recognizes LaTeX's bracket delimiters as math: <c>\(...\)</c> as a <c>MathInline</c>, and <c>\[...\]</c> as a
    /// <c>MathBlock</c> when it has its lines to itself and a <c>MathInline</c> otherwise; an opening glued to a letter
    /// or digit, or left unclosed, still escapes its parenthesis or bracket.
    /// </summary>
    /// <remarks>Combines with <c>UseMathematics()</c> in either order, or keeps <c>$</c> a plain character when used alone.</remarks>
    public static MarkdownPipelineBuilder UseMathBracketDelimiters(this MarkdownPipelineBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Extensions.AddIfNotAlready<BracketMathExtension>();
        return builder;
    }
}
