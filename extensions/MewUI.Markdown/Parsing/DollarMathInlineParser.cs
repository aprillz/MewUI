using Markdig.Helpers;
using Markdig.Parsers;
using Markdig.Syntax;

namespace Aprillz.MewUI.Markdown;

/// <summary>
/// Parses a <c>$$...$$</c> formula whose closing dollars are on a later line of the paragraph, which Markdig's
/// own parsers leave as text: its inline parser stops at the line end, and its block parser wants the dollars
/// on lines of their own. A formula closed on the same line is left to Markdig.
/// </summary>
internal sealed class DollarMathInlineParser : InlineParser
{
    public DollarMathInlineParser()
    {
        OpeningCharacters = ['$'];
    }

    /// <inheritdoc/>
    public override bool Match(InlineProcessor processor, ref StringSlice slice)
    {
        if (slice.PeekChar() != '$' || char.IsLetterOrDigit(slice.PeekCharExtra(-1)))
        {
            return false;
        }

        string text = slice.Text;
        bool spansLines = false;
        // slice.Start is on the first dollar and slice.End on the paragraph's last character; its lines are joined by '\n'.
        for (int index = slice.Start + 2; index + 1 <= slice.End; index++)
        {
            char current = text[index];
            if (current == '\n')
            {
                spansLines = true;
            }
            else if (current == '\\' && text[index + 1] != '\n')
            {
                // An escaped dollar or any other control symbol cannot close the formula.
                index++;
            }
            else if (current == '$' && text[index + 1] == '$')
            {
                return spansLines && Accept(processor, ref slice, index);
            }
        }
        return false;
    }

    private static bool Accept(InlineProcessor processor, ref StringSlice slice, int closingStart)
    {
        int start = slice.Start;
        StringSlice content = new(slice.Text, start + 2, closingStart - 1);
        content.Trim();
        if (content.IsEmpty)
        {
            return false;
        }

        int end = closingStart + 1;
        processor.Inline = new BracketMathInline
        {
            Span = new SourceSpan(processor.GetSourcePosition(start, out int line, out int column), processor.GetSourcePosition(end)),
            Line = line,
            Column = column,
            Content = content,
            Delimiter = '$',
            DelimiterCount = 2,
            IsDisplay = true,
            TextStart = start,
            TextEnd = closingStart,
        };
        slice.Start = end + 1;
        return true;
    }
}
