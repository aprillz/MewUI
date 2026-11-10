// Ported from MarkView.Avalonia src/MarkView.Avalonia.Math/Parsing/BracketMathBlock.cs (MIT).

using Markdig.Extensions.Mathematics;

namespace Aprillz.MewUI.Markdown;

/// <summary>
/// A <c>\[...\]</c> formula on lines of its own. Being a <see cref="MathBlock"/>, it is presented like <c>$$...$$</c>.
/// </summary>
internal sealed class BracketMathBlock() : MathBlock(null!)
{
}
