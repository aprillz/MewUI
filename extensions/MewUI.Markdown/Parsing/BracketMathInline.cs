// Ported from MarkView.Avalonia src/MarkView.Avalonia.Math/Parsing/BracketMathInline.cs (MIT).

using Markdig.Extensions.Mathematics;

namespace Aprillz.MewUI.Markdown;

/// <summary>
/// A formula this extension parsed inside a paragraph: <c>\(...\)</c>, <c>\[...\]</c>, or <c>$$...$$</c> spanning
/// lines. Being a <see cref="MathInline"/>, it is presented like Markdig's own <c>$...$</c>.
/// </summary>
internal sealed class BracketMathInline : MathInline
{
    /// <summary>Gets whether the formula was written <c>\[...\]</c> or <c>$$...$$</c> rather than <c>\(...\)</c>.</summary>
    public bool IsDisplay { get; init; }

    /// <summary>Gets the offset of the opening delimiter in the text <see cref="MathInline.Content"/> is a slice of: the paragraph's lines joined by <c>'\n'</c>.</summary>
    public int TextStart { get; init; }

    /// <summary>Gets the offset of the closing delimiter in the same text as <see cref="TextStart"/>.</summary>
    public int TextEnd { get; init; }
}
