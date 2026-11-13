using System.Globalization;

using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Automation;

/// <summary>
/// Text units, bounds, and hit testing for the text pattern, computed from the text and a control's character
/// rectangles. Lines are the text's hard lines; a line the control wraps counts as one.
/// </summary>
internal static class AutomationTextUnits
{
    private enum CharacterKind
    {
        Word,
        Space,
        LineBreak,
        Punctuation,
    }

    internal static TextRange UnitAt(string text, int offset, TextUnit unit)
    {
        int length = text.Length;
        if (length == 0)
        {
            return new TextRange(0, 0);
        }

        int at = Math.Clamp(offset, 0, length - 1);
        switch (unit)
        {
            case TextUnit.Character:
            {
                if (offset >= length)
                {
                    return new TextRange(length, 0);
                }

                int size = char.IsHighSurrogate(text[at]) && at + 1 < length && char.IsLowSurrogate(text[at + 1]) ? 2 : 1;
                return new TextRange(at, size);
            }
            case TextUnit.Word:
                return WordAt(text, at);
            case TextUnit.Line:
            case TextUnit.Paragraph:
            {
                int start = at == 0 ? 0 : text.LastIndexOf('\n', at - 1) + 1;
                int end = text.IndexOf('\n', at);
                end = end < 0 ? length : end + 1;
                return new TextRange(start, end - start);
            }
            default:
                return new TextRange(0, length);
        }
    }

    private static TextRange WordAt(string text, int at)
    {
        var kind = KindOf(text[at]);
        int start = at;
        while (start > 0 && KindOf(text[start - 1]) == kind)
        {
            start--;
        }

        int end = at + 1;
        while (end < text.Length && KindOf(text[end]) == kind)
        {
            end++;
        }

        // A word carries the spaces after it, as readers move from word start to word start.
        if (kind is CharacterKind.Word or CharacterKind.Punctuation)
        {
            while (end < text.Length && KindOf(text[end]) == CharacterKind.Space)
            {
                end++;
            }
        }

        return new TextRange(start, end - start);
    }

    private static CharacterKind KindOf(char character)
    {
        if (character == '\n' || character == '\r')
        {
            return CharacterKind.LineBreak;
        }
        else if (char.IsWhiteSpace(character))
        {
            return CharacterKind.Space;
        }
        else if (char.IsLetterOrDigit(character) || character == '_' || char.IsSurrogate(character)
            || CharUnicodeInfo.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark)
        {
            return CharacterKind.Word;
        }
        else
        {
            return CharacterKind.Punctuation;
        }
    }

    /// <summary>One rectangle per visual line the characters cover; a caret-sized one for an empty range.</summary>
    internal static IReadOnlyList<Rect> Bounds(string text, int start, int length, Func<int, Rect> characterRect)
    {
        int first = Math.Clamp(start, 0, text.Length);
        int end = Math.Clamp(start + Math.Max(0, length), first, text.Length);
        var bounds = new List<Rect>();
        if (end == first)
        {
            if (text.Length > 0)
            {
                var caret = characterRect(Math.Min(first, text.Length - 1));
                double x = first >= text.Length ? caret.Right : caret.Left;
                bounds.Add(new Rect(x, caret.Y, 0, caret.Height));
            }

            return bounds;
        }

        Rect line = Rect.Empty;
        for (int index = first; index < end; index++)
        {
            if (text[index] == '\n' || text[index] == '\r')
            {
                continue;
            }

            var rect = characterRect(index);
            if (line.IsEmpty)
            {
                line = rect;
            }
            else if (rect.Top >= line.Bottom - 0.5)
            {
                // A wrapped or hard line below: close the line so far.
                bounds.Add(line);
                line = rect;
            }
            else
            {
                line = line.Union(rect);
            }
        }

        if (!line.IsEmpty)
        {
            bounds.Add(line);
        }

        return bounds;
    }

    /// <summary>The offset of the character nearest <paramref name="point"/>.</summary>
    internal static int OffsetAt(string text, Point point, Func<int, Rect> characterRect)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        // The hard line the point is on, from the top of each line's first character.
        int lineStart = 0;
        for (int start = 0; start < text.Length;)
        {
            int next = text.IndexOf('\n', start);
            int nextStart = next < 0 ? text.Length : next + 1;
            if (nextStart >= text.Length || characterRect(nextStart).Top > point.Y)
            {
                lineStart = start;
                break;
            }

            start = nextStart;
        }

        int lineEnd = text.IndexOf('\n', lineStart);
        lineEnd = lineEnd < 0 ? text.Length : lineEnd;
        int low = lineStart;
        int high = lineEnd;
        while (low < high)
        {
            int middle = (low + high) / 2;
            var rect = characterRect(middle);
            if (rect.X + rect.Width / 2 < point.X)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }
}
