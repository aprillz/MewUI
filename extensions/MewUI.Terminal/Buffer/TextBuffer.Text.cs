// Ported from Windows Terminal src/buffer/out/textBuffer.cpp (MIT): word boundaries, text spans and plain text copy.

using System.Text;

using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Buffer;

/// <summary>What part of the buffer a copy takes and how its rows are joined.</summary>
internal readonly record struct CopyRequest(
    TilPoint Beg,
    TilPoint End,
    int MinX,
    int MaxX,
    bool BlockSelection,
    bool TrimTrailingWhitespace,
    bool IncludeLineBreak,
    bool FormatWrappedRows,
    bool BufferCoordinates)
{
    public static CopyRequest Create(TextBuffer buffer, TilPoint beg, TilPoint end, bool blockSelection, bool includeLineBreak,
        bool trimTrailingWhitespace, bool formatWrappedRows, bool bufferCoordinates = false)
    {
        var size = buffer.Size;
        var clampedBeg = TilPoint.Max(beg, new TilPoint(0, 0));
        var clampedEnd = TilPoint.Min(end, new TilPoint(size.Width - 1, size.Height - 1));
        return new CopyRequest(
            clampedBeg,
            clampedEnd,
            Math.Min(clampedBeg.X, clampedEnd.X),
            Math.Max(clampedBeg.X, clampedEnd.X),
            blockSelection,
            trimTrailingWhitespace,
            includeLineBreak,
            formatWrappedRows,
            bufferCoordinates);
    }

    /// <summary>A copy as the selection settings ask for it: one line, a block, and whether a block is trimmed.</summary>
    public static CopyRequest FromConfig(TextBuffer buffer, TilPoint beg, TilPoint end, bool singleLine, bool blockSelection,
        bool trimBlockSelection, bool bufferCoordinates = false)
        => Create(
            buffer,
            beg,
            end,
            blockSelection,
            includeLineBreak: !singleLine || blockSelection,
            trimTrailingWhitespace: !singleLine && (!blockSelection || trimBlockSelection),
            formatWrappedRows: blockSelection,
            bufferCoordinates);
}

internal sealed partial class TextBuffer
{
    private DelimiterClass GetDelimiterClassAt(TilPoint position, ReadOnlySpan<char> wordDelimiters)
    {
        var realPosition = ScreenToBufferPosition(position);
        return GetRowByOffset(realPosition.Y).DelimiterClassAt(realPosition.X, wordDelimiters);
    }

    /// <summary>
    /// The start of the word at or before <paramref name="position"/>. With <paramref name="includeWhitespace"/> a
    /// position on whitespace moves on to the word before it, crossing rows freely.
    /// </summary>
    public TilPoint GetWordStart(TilPoint position, ReadOnlySpan<char> wordDelimiters, bool includeWhitespace, TilPoint? limitOptional = null)
    {
        var bufferSize = Size;
        var limit = limitOptional ?? bufferSize.BottomInclusiveRightExclusive;

        if (position < bufferSize.Origin)
        {
            return bufferSize.Origin;
        }

        if (position >= limit)
        {
            position = limit;
        }

        var initialDelimiter = bufferSize.IsInBounds(position) ? GetDelimiterClassAt(position, wordDelimiters) : DelimiterClass.ControlChar;
        position = GetDelimiterClassRunStart(position, wordDelimiters, includeWhitespace);
        if (!includeWhitespace || position.X == bufferSize.Left)
        {
            return position;
        }

        if (initialDelimiter == DelimiterClass.ControlChar)
        {
            bufferSize.DecrementInExclusiveBounds(ref position);
            position = GetDelimiterClassRunStart(position, wordDelimiters, includeWhitespace);
        }

        return position;
    }

    /// <summary>
    /// The exclusive end of the word at or after <paramref name="position"/>. With <paramref name="includeWhitespace"/>
    /// the whitespace after the word is taken too.
    /// </summary>
    public TilPoint GetWordEnd(TilPoint position, ReadOnlySpan<char> wordDelimiters, bool includeWhitespace, TilPoint? limitOptional = null)
    {
        var bufferSize = Size;
        var limit = limitOptional ?? bufferSize.BottomInclusiveRightExclusive;

        if (position >= limit)
        {
            return limit;
        }

        if (position < bufferSize.Origin)
        {
            position = bufferSize.Origin;
        }

        position = GetDelimiterClassRunEnd(position, wordDelimiters, includeWhitespace);
        if (position >= limit)
        {
            return limit;
        }

        if (!includeWhitespace || position.X == bufferSize.RightExclusive)
        {
            return position;
        }

        var nextDelimiterClass = bufferSize.IsInBounds(position) ? GetDelimiterClassAt(position, wordDelimiters) : DelimiterClass.ControlChar;
        if (nextDelimiterClass == DelimiterClass.ControlChar)
        {
            position = GetDelimiterClassRunEnd(position, wordDelimiters, includeWhitespace);
            return TilPoint.Min(position, limit);
        }

        return position;
    }

    public bool IsWordBoundary(TilPoint position, ReadOnlySpan<char> wordDelimiters)
    {
        var bufferSize = Size;
        if (!bufferSize.IsInExclusiveBounds(position))
        {
            return false;
        }

        if (position == bufferSize.Origin || position == bufferSize.BottomInclusiveRightExclusive)
        {
            return true;
        }

        // The start or end of a row that did not wrap is a boundary.
        if (position.X == bufferSize.Left && !GetRowByOffset(position.Y - 1).WasWrapForced)
        {
            return true;
        }

        if (position.X == bufferSize.RightExclusive && !GetRowByOffset(position.Y).WasWrapForced)
        {
            return true;
        }

        var previous = position;
        bufferSize.DecrementInBounds(ref previous);
        var previousClass = GetDelimiterClassAt(previous, wordDelimiters);
        var currentClass = GetDelimiterClassAt(position, wordDelimiters);
        return previousClass != currentClass && currentClass != DelimiterClass.ControlChar;
    }

    /// <summary>
    /// The first cell of the run of one delimiter class that holds <paramref name="position"/>; it crosses only
    /// wrapped row ends unless <paramref name="accessibilityMode"/> is set.
    /// </summary>
    private TilPoint GetDelimiterClassRunStart(TilPoint position, ReadOnlySpan<char> wordDelimiters, bool accessibilityMode)
    {
        var bufferSize = Size;
        var initialClass = bufferSize.IsInBounds(position) ? GetDelimiterClassAt(position, wordDelimiters) : DelimiterClass.ControlChar;
        var next = position;
        while (next != bufferSize.Origin)
        {
            bufferSize.DecrementInExclusiveBounds(ref next);

            if (next.X == bufferSize.RightExclusive)
            {
                if (!GetRowByOffset(next.Y).WasWrapForced && !accessibilityMode)
                {
                    return position;
                }
            }
            else if (GetDelimiterClassAt(next, wordDelimiters) != initialClass)
            {
                return position;
            }
            else
            {
                position = next;
            }
        }

        return position;
    }

    /// <summary>The exclusive end of the run of one delimiter class that holds <paramref name="position"/>.</summary>
    private TilPoint GetDelimiterClassRunEnd(TilPoint position, ReadOnlySpan<char> wordDelimiters, bool accessibilityMode)
    {
        var bufferSize = Size;
        var initialClass = bufferSize.IsInBounds(position) ? GetDelimiterClassAt(position, wordDelimiters) : DelimiterClass.ControlChar;
        for (var next = position; next != bufferSize.BottomInclusiveRightExclusive; position = next)
        {
            bufferSize.IncrementInExclusiveBounds(ref next);

            if (next.X == bufferSize.Left)
            {
                var row = GetRowByOffset(position.Y);
                if (accessibilityMode)
                {
                    if (GetDelimiterClassAt(next, wordDelimiters) != initialClass)
                    {
                        return next;
                    }
                }
                else if (!row.WasWrapForced || GetDelimiterClassAt(next, wordDelimiters) != initialClass)
                {
                    return position;
                }
            }
            else if (bufferSize.IsInBounds(next) && GetDelimiterClassAt(next, wordDelimiters) != initialClass)
            {
                return next;
            }
        }

        return position;
    }

    /// <summary>The first cell of the glyph at <paramref name="position"/>.</summary>
    public TilPoint GetGlyphStart(TilPoint position, TilPoint? limitOptional = null)
    {
        var bufferSize = Size;
        var limit = limitOptional ?? bufferSize.EndExclusive;
        if (position > limit)
        {
            return limit;
        }

        if (bufferSize.IsInBounds(position) && GetRowByOffset(position.Y).DbcsAttrAt(position.X) == DbcsAttribute.Trailing)
        {
            bufferSize.DecrementInBounds(ref position, true);
        }

        return position;
    }

    /// <summary>The last cell of the glyph at <paramref name="position"/>, or the one after it with <paramref name="accessibilityMode"/>.</summary>
    public TilPoint GetGlyphEnd(TilPoint position, bool accessibilityMode = false, TilPoint? limitOptional = null)
    {
        var bufferSize = Size;
        var limit = limitOptional ?? bufferSize.EndExclusive;
        if (position > limit)
        {
            return limit;
        }

        if (bufferSize.IsInBounds(position) && GetRowByOffset(position.Y).DbcsAttrAt(position.X) == DbcsAttribute.Leading)
        {
            bufferSize.IncrementInBounds(ref position, true);
        }

        if (accessibilityMode)
        {
            bufferSize.IncrementInBounds(ref position, true);
        }

        return position;
    }

    /// <summary>One inclusive rectangle per row of the text from <paramref name="start"/> to <paramref name="end"/>.</summary>
    public List<TilInclusiveRect> GetTextRects(TilPoint start, TilPoint end, bool blockSelection, bool bufferCoordinates)
    {
        var (higher, lower) = start <= end ? (start, end) : (end, start);
        var textRects = new List<TilInclusiveRect>(1 + lower.Y - higher.Y);
        for (int row = higher.Y; row <= lower.Y; row++)
        {
            int left;
            int right;
            if (blockSelection || higher.Y == lower.Y)
            {
                left = Math.Min(higher.X, lower.X);
                right = Math.Max(higher.X, lower.X);
            }
            else
            {
                var bufferSize = Size;
                left = row == higher.Y ? higher.X : bufferSize.Left;
                right = row == lower.Y ? lower.X : bufferSize.RightInclusive;
            }

            var textRow = new TilInclusiveRect(left, row, right, row);
            if (!bufferCoordinates)
            {
                textRow = ScreenToBufferLine(textRow, GetLineRendition(row));
            }

            textRects.Add(ExpandTextRow(textRow));
        }

        return textRects;
    }

    /// <summary>The spans of a selection: one span, or one per row of a block selection.</summary>
    public List<TilPointSpan> GetTextSpans(TilPoint start, TilPoint end, bool blockSelection, bool bufferCoordinates)
    {
        var textSpans = new List<TilPointSpan>();
        if (blockSelection)
        {
            foreach (var rect in GetTextRects(start, end, true, bufferCoordinates))
            {
                textSpans.Add(new TilPointSpan(new TilPoint(rect.Left, rect.Top), new TilPoint(rect.Right, rect.Bottom)));
            }

            return textSpans;
        }

        var (higher, lower) = start <= end ? (start, end) : (end, start);
        if (!bufferCoordinates)
        {
            higher = ScreenToBufferLineInclusive(higher, GetLineRendition(higher.Y));
            lower = ScreenToBufferLineInclusive(lower, GetLineRendition(lower.Y));
        }

        var expanded = ExpandTextRow(new TilInclusiveRect(higher.X, higher.Y, lower.X, lower.Y));
        textSpans.Add(new TilPointSpan(new TilPoint(expanded.Left, expanded.Top), new TilPoint(expanded.Right, expanded.Bottom)));
        return textSpans;
    }

    /// <summary>Widens a row range so it holds whole wide glyphs.</summary>
    private TilInclusiveRect ExpandTextRow(TilInclusiveRect textRow)
    {
        var bufferSize = Size;

        var target = new TilPoint(textRow.Left, textRow.Top);
        if (bufferSize.IsInBounds(target) && GetRowByOffset(target.Y).DbcsAttrAt(target.X) == DbcsAttribute.Trailing)
        {
            bufferSize.DecrementInExclusiveBounds(ref target);
            textRow = textRow with { Left = target.X };
        }

        target = new TilPoint(textRow.Right, textRow.Bottom);
        if (bufferSize.IsInBounds(target) && GetRowByOffset(target.Y).DbcsAttrAt(target.X) == DbcsAttribute.Trailing)
        {
            bufferSize.IncrementInExclusiveBounds(ref target);
            textRow = textRow with { Right = target.X };
        }

        return textRow;
    }

    private static TilInclusiveRect ScreenToBufferLine(TilInclusiveRect line, LineRendition lineRendition)
    {
        int scale = lineRendition == LineRendition.SingleWidth ? 0 : 1;
        return new TilInclusiveRect(line.Left >> scale, line.Top, line.Right >> scale, line.Bottom);
    }

    private static TilPoint ScreenToBufferLineInclusive(TilPoint line, LineRendition lineRendition)
    {
        int scale = lineRendition == LineRendition.SingleWidth ? 0 : 1;
        return new TilPoint(line.X >> scale, line.Y);
    }

    /// <summary>The text from <paramref name="start"/> (inclusive) to <paramref name="end"/> (exclusive) on one logical line.</summary>
    public string GetPlainText(TilPoint start, TilPoint end)
        => GetPlainText(CopyRequest.FromConfig(this, start, end, singleLine: true, blockSelection: false, trimBlockSelection: false));

    /// <summary>The columns [begin, end) a copy takes from a row, and whether a line break follows it.</summary>
    private (int Begin, int End, bool AddLineBreak) RowCopyHelper(in CopyRequest request, int rowIndex, Row row)
    {
        int rowBegin;
        int rowEnd;
        var lineRendition = row.LineRendition;
        if (request.BlockSelection)
        {
            rowBegin = request.BufferCoordinates ? request.MinX : ScreenToBufferLineInclusive(new TilPoint(request.MinX, rowIndex), lineRendition).X;
            rowEnd = request.BufferCoordinates ? request.MaxX : ScreenToBufferLineInclusive(new TilPoint(request.MaxX, rowIndex), lineRendition).X;
        }
        else
        {
            var beg = request.BufferCoordinates ? request.Beg : ScreenToBufferLineInclusive(request.Beg, lineRendition);
            var end = request.BufferCoordinates ? request.End : ScreenToBufferLineInclusive(request.End, lineRendition);
            rowBegin = rowIndex != beg.Y ? 0 : beg.X;
            rowEnd = rowIndex != end.Y ? row.GetReadableColumnCount() : end.X;
        }

        // Selections do not stick to glyph boundaries, so a partly selected glyph is taken whole.
        rowBegin = row.AdjustToGlyphStart(rowBegin);
        rowEnd = row.AdjustToGlyphEnd(rowEnd);

        bool shouldFormatRow = request.FormatWrappedRows || !row.WasWrapForced;
        if (shouldFormatRow && request.TrimTrailingWhitespace)
        {
            rowEnd = Math.Min(rowEnd, row.GetLastNonSpaceColumn());
        }

        return (rowBegin, rowEnd, shouldFormatRow && request.IncludeLineBreak);
    }

    /// <summary>The selected text, ready for the clipboard; rows are joined with CRLF where the request asks for breaks.</summary>
    public string GetPlainText(in CopyRequest request)
    {
        if (request.Beg > request.End)
        {
            return string.Empty;
        }

        var selectedText = new StringBuilder();
        for (int rowIndex = request.Beg.Y; rowIndex <= request.End.Y; rowIndex++)
        {
            var row = GetRowByOffset(rowIndex);
            var (rowBegin, rowEnd, addLineBreak) = RowCopyHelper(request, rowIndex, row);
            selectedText.Append(row.GetText(rowBegin, rowEnd));

            if (addLineBreak && rowIndex != request.End.Y)
            {
                selectedText.Append("\r\n");
            }
        }

        return selectedText.ToString();
    }
}
