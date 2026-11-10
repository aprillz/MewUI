// Ported from MarkView.Avalonia src/MarkView.Avalonia.Math/Parsing/BracketMathInlineParser.cs (MIT).

using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;

namespace Aprillz.MewUI.Markdown;

/// <summary>
/// Parses LaTeX's bracket delimiters into a <see cref="BracketMathInline"/>: <c>\(...\)</c> on one line, and
/// <c>\[...\]</c>, which may span the lines of its paragraph. An opening glued to a letter or digit
/// (<c>arr\[i\]</c>), an unclosed opening and an empty formula are left to the other parsers and keep their
/// CommonMark meaning of an escaped parenthesis or bracket.
/// </summary>
internal sealed class BracketMathInlineParser : InlineParser
{
    public BracketMathInlineParser()
    {
        OpeningCharacters = ['\\'];
    }

    /// <inheritdoc/>
    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        char opening = slice.PeekChar();
        if ((opening != '(' && opening != '[') || char.IsLetterOrDigit(slice.PeekCharExtra(-1)))
        {
            return false;
        }

        bool display = opening == '[';
        char closing = display ? ']' : ')';
        string text = slice.Text;
        // slice.Start is on the backslash; the paragraph's lines follow, joined by '\n'.
        for (int index = slice.Start + 2; index < slice.End; index++)
        {
            char current = text[index];
            if (current == '\n' && !display)
            {
                return false;
            }

            if (current != '\\')
            {
                continue;
            }

            if (text[index + 1] == closing)
            {
                return Accept(processor, ref slice, display, index);
            }

            if (text[index + 1] != '\n')
            {
                // A TeX control symbol such as "\\" or "\}" cannot close the formula.
                index++;
            }
        }
        return false;
    }

    private static bool Accept(InlineProcessor processor, ref StringSlice slice, bool display, int closingStart)
    {
        int start = slice.Start;
        StringSlice content = new(slice.Text, start + 2, closingStart - 1);
        content.Trim();
        if (content.IsEmpty)
        {
            // "\(\)" or "\[ \]": nothing to typeset.
            return false;
        }

        int end = closingStart + 1;
        processor.Inline = new BracketMathInline
        {
            Span = new SourceSpan(processor.GetSourcePosition(start, out int line, out int column), processor.GetSourcePosition(end)),
            Line = line,
            Column = column,
            Content = content,
            // Two delimiters mark a display formula, as Markdig counts the dollars of "$$...$$".
            DelimiterCount = display ? 2 : 1,
            IsDisplay = display,
            TextStart = start,
            TextEnd = closingStart,
        };
        slice.Start = end + 1;
        return true;
    }
}
