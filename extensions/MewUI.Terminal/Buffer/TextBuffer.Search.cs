// Ported from Windows Terminal src/buffer/out/textBuffer.cpp, search.h and UTextAdapter.cpp (MIT).

using System.Text;
using System.Text.RegularExpressions;

using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Buffer;

[Flags]
internal enum SearchFlag
{
    None = 0,
    CaseInsensitive = 1 << 0,
    RegularExpression = 1 << 1,
}

/// <summary>
/// The text of a range of rows as one string, for regular expressions: rows follow each other, with a line feed after
/// each row that did not wrap, so text that wraps reads as one line.
/// </summary>
internal sealed class BufferTextRange
{
    private readonly TextBuffer _buffer;
    private readonly int _rowBegin;
    private readonly int[] _rowStarts;

    public BufferTextRange(TextBuffer buffer, int rowBegin, int rowEnd)
    {
        _buffer = buffer;
        _rowBegin = rowBegin;
        var text = new StringBuilder();
        int rowCount = Math.Max(0, rowEnd - rowBegin);
        _rowStarts = new int[rowCount + 1];
        for (int row = rowBegin; row < rowBegin + rowCount; row++)
        {
            _rowStarts[row - rowBegin] = text.Length;
            var rowBuffer = buffer.GetRowByOffset(row);
            text.Append(rowBuffer.GetText());
            if (!rowBuffer.WasWrapForced)
            {
                text.Append('\n');
            }
        }

        _rowStarts[rowCount] = text.Length;
        Text = text.ToString();
    }

    public string Text { get; }

    /// <summary>The cell where the character at <paramref name="index"/> starts, or null past the end of the text.</summary>
    public TilPoint? PositionAt(int index)
    {
        if (index < 0 || index >= Text.Length)
        {
            return null;
        }

        int found = Array.BinarySearch(_rowStarts, 0, _rowStarts.Length - 1, index);
        int rowOffset = found >= 0 ? found : ~found - 1;
        int row = _rowBegin + rowOffset;
        return new TilPoint(_buffer.GetRowByOffset(row).GetLeadingColumnAtCharOffset(index - _rowStarts[rowOffset]), row);
    }

    /// <summary>The cells [start, end) of a match.</summary>
    public TilPointSpan RangeFromMatch(Match match)
    {
        var start = PositionAt(match.Index) ?? new TilPoint(0, _rowBegin);
        var end = PositionAt(match.Index + match.Length) ?? start;
        return new TilPointSpan(start, end);
    }
}

internal sealed partial class TextBuffer
{
    /// <summary>Finds <paramref name="needle"/> in all rows; null when the regular expression is invalid.</summary>
    public List<TilPointSpan>? SearchText(string needle, SearchFlag flags) => SearchText(needle, flags, 0, int.MaxValue);

    /// <summary>Finds <paramref name="needle"/> in rows [<paramref name="rowBegin"/>, <paramref name="rowEnd"/>).</summary>
    public List<TilPointSpan>? SearchText(string needle, SearchFlag flags, int rowBegin, int rowEnd)
    {
        rowEnd = Math.Min(rowEnd, EstimateOffsetOfLastCommittedRow() + 1);

        var results = new List<TilPointSpan>();

        // A needle of only whitespace would match the whole empty screen.
        if (string.IsNullOrWhiteSpace(needle) || rowBegin >= rowEnd)
        {
            return results;
        }

        var options = RegexOptions.CultureInvariant;
        if ((flags & SearchFlag.CaseInsensitive) != 0)
        {
            options |= RegexOptions.IgnoreCase;
        }

        string pattern;
        if ((flags & SearchFlag.RegularExpression) != 0)
        {
            options |= RegexOptions.Multiline;
            pattern = needle;
        }
        else
        {
            pattern = Regex.Escape(needle);
        }

        Regex regex;
        try
        {
            regex = new Regex(pattern, options);
        }
        catch (ArgumentException)
        {
            return null;
        }

        var range = new BufferTextRange(this, rowBegin, rowEnd);
        foreach (Match match in regex.Matches(range.Text))
        {
            results.Add(range.RangeFromMatch(match));
        }

        return results;
    }

    /// <summary>Moves to the start of the next glyph, stopping at <paramref name="limit"/>; false when it could not move.</summary>
    public bool MoveToNextGlyph2(ref TilPoint pos, TilPoint? limit = null)
    {
        var bufferSize = Size;
        var limitPos = limit ?? bufferSize.BottomInclusiveRightExclusive;

        if (pos >= limitPos)
        {
            pos = limitPos;
            return false;
        }

        bool success = bufferSize.IncrementInExclusiveBounds(ref pos);

        // The trailing half of a wide glyph is part of the glyph just passed.
        if (success && bufferSize.IsInBounds(pos) && GetRowByOffset(pos.Y).DbcsAttrAt(pos.X) == DbcsAttribute.Trailing)
        {
            bufferSize.IncrementInExclusiveBounds(ref pos);
        }

        return success;
    }

    /// <summary>Moves to the start of the previous glyph; false when it could not move.</summary>
    public bool MoveToPreviousGlyph2(ref TilPoint pos, TilPoint? limit = null)
    {
        var bufferSize = Size;
        var limitPos = limit ?? bufferSize.BottomInclusiveRightExclusive;

        if (pos >= limitPos)
        {
            pos = limitPos;
            return false;
        }

        bool success = bufferSize.DecrementInExclusiveBounds(ref pos);

        if (success && bufferSize.IsInBounds(pos) && GetRowByOffset(pos.Y).DbcsAttrAt(pos.X) == DbcsAttribute.Trailing)
        {
            bufferSize.DecrementInExclusiveBounds(ref pos);
        }

        return success;
    }
}
