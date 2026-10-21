// Ported from Windows Terminal src/buffer/out/textBuffer.hpp and textBuffer.cpp (MIT): storage, writing, scrolling
// and resizing. Text extraction, reflow, hyperlinks and marks are in the other TextBuffer.*.cs files.

using Aprillz.MewUI.Terminal.Core;
using Aprillz.MewUI.Terminal.Types;
using Aprillz.MewUI.Terminal.Unicode;

namespace Aprillz.MewUI.Terminal.Buffer;

/// <summary>
/// The rows of a screen buffer and its scrollback, stored as a circular array: row 0 is the oldest line and
/// <see cref="FirstRowIndex"/> tells where it sits in storage. Rows are created when first touched.
/// </summary>
internal sealed partial class TextBuffer
{
    // Rows created ahead of the one asked for, as the original commits memory ahead.
    private const int COMMIT_READ_AHEAD_ROW_COUNT = 128;

    private static long _lastMutationIdInitialValue;

    private readonly Cursor _cursor;
    private Renderer? _renderer;

    // Storage index 0 is the scratchpad row; regular rows start at 1.
    private Row?[] _rows = [];
    private int _committedCount;
    private TextAttribute _initialAttributes;
    private int _width;
    private int _height;
    private TextAttribute _currentAttributes;
    private int _firstRow;
    private ulong _lastMutationId;
    private bool _isActiveBuffer;

    public TextBuffer(TilSize screenBufferSize, TextAttribute defaultAttributes, int cursorSize, bool isActiveBuffer, Renderer? renderer)
    {
        _renderer = renderer;
        _currentAttributes = defaultAttributes;

        // Each buffer starts at a different mutation id, so ids of two buffers never compare equal.
        _lastMutationId = (ulong)Interlocked.Add(ref _lastMutationIdInitialValue, 0x100000000) - 0x100000000;
        _cursor = new Cursor(cursorSize, this);
        _isActiveBuffer = isActiveBuffer;

        // A buffer of 0 columns or rows could not take text.
        screenBufferSize = new TilSize(Math.Max(screenBufferSize.Width, 1), Math.Max(screenBufferSize.Height, 1));
        Reserve(screenBufferSize, defaultAttributes);
    }

    public Cursor Cursor => _cursor;

    public ulong LastMutationId => _lastMutationId;

    public int FirstRowIndex => _firstRow;

    public int TotalRowCount => _height;

    public Viewport Size => Viewport.FromDimensions(default, new TilSize(_width, _height));

    public TextAttribute CurrentAttributes
    {
        get => _currentAttributes;
        set => _currentAttributes = value;
    }

    public Renderer? Renderer => _renderer;

    public bool IsActiveBuffer => _isActiveBuffer;

    public void SetAsActiveBuffer(bool isActiveBuffer) => _isActiveBuffer = isActiveBuffer;

    public void SetRenderer(Renderer? renderer) => _renderer = renderer;

    private void Reserve(TilSize screenBufferSize, TextAttribute defaultAttributes)
    {
        if (screenBufferSize.Width > ushort.MaxValue || screenBufferSize.Height > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(screenBufferSize));
        }

        _rows = new Row?[screenBufferSize.Height + 1];
        _committedCount = 0;
        _initialAttributes = defaultAttributes;
        _width = screenBufferSize.Width;
        _height = screenBufferSize.Height;
    }

    /// <summary>Creates the rows of storage up to and including <paramref name="offset"/>, with some read-ahead.</summary>
    private void Commit(int offset)
    {
        int target = Math.Min(_rows.Length, offset + 1 + COMMIT_READ_AHEAD_ROW_COUNT);
        for (; _committedCount < target; _committedCount++)
        {
            _rows[_committedCount] = new Row(_width, _initialAttributes);
        }
    }

    /// <summary>Drops every row; rows are recreated blank with the initial attributes when touched.</summary>
    private void Decommit()
    {
        Array.Clear(_rows);
        _committedCount = 0;
    }

    private Row GetRowByOffsetDirect(int offset)
    {
        if (offset < 0 || offset >= _rows.Length)
        {
            throw new InvalidOperationException("Row offset outside the buffer.");
        }

        if (offset >= _committedCount)
        {
            Commit(offset);
        }

        return _rows[offset]!;
    }

    private Row GetRow(int y)
    {
        // Rows are stored circularly from _firstRow; a negative index wraps to the end.
        int offset = (_firstRow + y) % _height;
        if (offset < 0)
        {
            offset += _height;
        }

        return GetRowByOffsetDirect(offset + 1);
    }

    /// <summary>The highest row index that has storage, to stop scans of the whole buffer early.</summary>
    private int EstimateOffsetOfLastCommittedRow() => Math.Max(0, _committedCount - 2);

    private bool IsRowCommitted(int y)
    {
        int offset = (_firstRow + y + 1) % _height;
        if (offset < 0)
        {
            offset += _height;
        }

        return offset < _committedCount;
    }

    /// <summary>The row at <paramref name="index"/>, counted from the first (oldest) row of the buffer.</summary>
    public Row GetRowByOffset(int index) => GetRow(index);

    public Row GetMutableRowByOffset(int index)
    {
        _lastMutationId++;
        return GetRow(index);
    }

    /// <summary>A blank row with the current attributes, for temporary use.</summary>
    public Row GetScratchpadRow() => GetScratchpadRow(_currentAttributes);

    public Row GetScratchpadRow(TextAttribute attributes)
    {
        var row = GetRowByOffsetDirect(0);
        row.Reset(attributes);
        return row;
    }

    public void CopyProperties(TextBuffer other) => _cursor.CopyProperties(other._cursor);

    /// <summary>The start of the grapheme after the one at <paramref name="position"/>.</summary>
    public static int GraphemeNext(ReadOnlySpan<char> chars, int position)
    {
        var state = new GraphemeState(position);
        CodepointWidthDetector.Singleton.GraphemeNext(ref state, chars);
        return position + state.Len;
    }

    /// <summary>The start of the grapheme before <paramref name="position"/>.</summary>
    public static int GraphemePrev(ReadOnlySpan<char> chars, int position)
    {
        var state = new GraphemeState(position);
        CodepointWidthDetector.Singleton.GraphemePrev(ref state, chars);
        return position - state.Len;
    }

    /// <summary>
    /// How many characters of <paramref name="chars"/> fit into <paramref name="columnLimit"/> columns, and in
    /// <paramref name="columns"/> how many columns they take; text that does not fit uses up exactly the limit.
    /// </summary>
    public static int FitTextIntoColumns(ReadOnlySpan<char> chars, int columnLimit, out int columns)
    {
        columnLimit = Math.Max(0, columnLimit);

        int asciiEnd = Math.Min(chars.Length, columnLimit);
        int position = 0;
        while (position != asciiEnd && chars[position] < 0x80)
        {
            position++;
        }

        int distance = position;
        int column = distance;
        if (position == asciiEnd)
        {
            columns = column;
            return distance;
        }

        // A combining mark joins the ASCII char before it, so step back one and measure from there.
        if (distance != 0)
        {
            distance--;
            column--;
        }

        var detector = CodepointWidthDetector.Singleton;
        var state = new GraphemeState(distance);
        while (distance < chars.Length)
        {
            detector.GraphemeNext(ref state, chars);
            column += state.Width;
            if (column > columnLimit)
            {
                break;
            }

            distance += state.Len;
        }

        columns = column;
        return distance;
    }

    /// <summary>Where the cursor would end up after <paramref name="distance"/> left (negative) or right arrow presses.</summary>
    public TilPoint NavigateCursor(TilPoint position, int distance)
    {
        int maxX = _width - 1;
        int maxY = _height - 1;
        int x = Math.Clamp(position.X, 0, maxX);
        int y = Math.Clamp(position.Y, 0, maxY);
        var row = GetRowByOffset(y);

        if (distance < 0)
        {
            do
            {
                if (x > 0)
                {
                    x = row.NavigateToPrevious(x);
                }
                else if (y <= 0)
                {
                    break;
                }
                else
                {
                    y--;
                    row = GetRowByOffset(y);
                    x = row.GetReadableColumnCount() - 1;
                }
            }
            while (++distance != 0);
        }
        else if (distance > 0)
        {
            int rowWidth = row.GetReadableColumnCount();
            do
            {
                if (x < rowWidth)
                {
                    x = row.NavigateToNext(x);
                }
                else if (y >= maxY)
                {
                    break;
                }
                else
                {
                    y++;
                    row = GetRowByOffset(y);
                    rowWidth = row.GetReadableColumnCount();
                    x = 0;
                }
            }
            while (--distance != 0);
        }

        return new TilPoint(x, y);
    }

    /// <summary>Writes text over a row; call again on the same row while ColumnEnd is below ColumnLimit.</summary>
    public void Replace(int row, TextAttribute attributes, ref RowWriteState state)
    {
        var target = GetMutableRowByOffset(row);
        target.ReplaceText(ref state);
        target.ReplaceAttributes(state.ColumnBegin, state.ColumnEnd, attributes);
        TriggerRedraw(Viewport.FromExclusive(new TilRect(state.ColumnBeginDirty, row, state.ColumnEndDirty, row + 1)));
    }

    /// <summary>Writes text into a row, moving what was there to the right (insert mode).</summary>
    public void Insert(int row, TextAttribute attributes, ref RowWriteState state)
    {
        var target = GetMutableRowByOffset(row);
        var scratch = GetScratchpadRow();
        scratch.CopyFrom(target);

        target.ReplaceText(ref state);
        target.ReplaceAttributes(state.ColumnBegin, state.ColumnEnd, attributes);

        // Put back the text that was pushed to the right, then its attributes.
        var restoreState = new RowWriteState(scratch.GetText(state.ColumnBegin, state.ColumnLimit), state.ColumnEnd, state.ColumnLimit);
        target.ReplaceText(ref restoreState);

        int copyAmount = restoreState.ColumnEnd - restoreState.ColumnBegin;
        if (copyAmount > 0)
        {
            var restoreAttr = scratch.Attributes.Slice(state.ColumnBegin, state.ColumnBegin + copyAmount);
            target.Attributes.Replace(restoreState.ColumnBegin, restoreState.ColumnEnd, restoreAttr);
        }

        TriggerRedraw(Viewport.FromExclusive(new TilRect(state.ColumnBeginDirty, row, restoreState.ColumnEndDirty, row + 1)));
    }

    /// <summary>Fills a rectangle with copies of <paramref name="fill"/> and <paramref name="attributes"/>.</summary>
    public void FillRect(TilRect rect, ReadOnlySpan<char> fill, TextAttribute attributes)
    {
        if (!(rect.Left >= 0 && rect.Top >= 0 && rect.Right > rect.Left && rect.Bottom > rect.Top) || fill.IsEmpty)
        {
            return;
        }

        var scratchpad = GetScratchpadRow(attributes);

        // The scratchpad is blank already; other fills are written one copy at a time, so neighboring copies of a
        // combining mark cannot join.
        if (!(fill.Length == 1 && fill[0] == ' '))
        {
            var state = new RowWriteState(default, 0, rect.Right)
            {
                ColumnEnd = rect.Left,
            };

            while (state.ColumnEnd < rect.Right)
            {
                state.ColumnBegin = state.ColumnEnd;
                state.Text = fill;
                scratchpad.ReplaceText(ref state);
            }
        }

        var copyState = new RowCopyTextFromState(scratchpad)
        {
            ColumnBegin = rect.Left,
            ColumnLimit = rect.Right,
            SourceColumnBegin = rect.Left,
        };

        for (int y = rect.Top; y < rect.Bottom; y++)
        {
            var row = GetMutableRowByOffset(y);
            row.CopyTextFrom(ref copyState);
            row.ReplaceAttributes(rect.Left, rect.Right, attributes);
            TriggerRedraw(Viewport.FromExclusive(new TilRect(copyState.ColumnBeginDirty, y, copyState.ColumnEndDirty, y + 1)));
        }
    }

    /// <summary>The glyph, wide-glyph half and attributes of one cell, as <see cref="WriteCell"/> takes them.</summary>
    public (string Glyph, DbcsAttribute DbcsAttribute, TextAttribute Attribute) ReadCell(TilPoint at)
    {
        var row = GetRowByOffset(at.Y);
        return (row.GlyphAt(at.X).ToString(), row.DbcsAttrAt(at.X), row.GetAttrByColumn(at.X));
    }

    /// <summary>
    /// Writes one cell read by <see cref="ReadCell"/>: a leading half writes the whole wide glyph, a trailing half
    /// writes it one column to the left, and either is blanked where the glyph would not fit.
    /// </summary>
    public void WriteCell(TilPoint target, ReadOnlySpan<char> glyph, DbcsAttribute dbcsAttribute, TextAttribute attribute)
    {
        if (!Size.IsInBounds(target))
        {
            return;
        }

        var row = GetMutableRowByOffset(target.Y);
        if (dbcsAttribute == DbcsAttribute.Leading)
        {
            if (target.X == _width - 1)
            {
                row.ClearCell(target.X);
                row.SetDoubleBytePadded(true);
            }
            else
            {
                row.ReplaceCharacters(target.X, 2, glyph);
            }
        }
        else if (dbcsAttribute == DbcsAttribute.Trailing)
        {
            if (target.X == 0)
            {
                row.ClearCell(target.X);
            }
            else
            {
                row.ReplaceCharacters(target.X - 1, 2, glyph);
            }
        }
        else
        {
            row.ReplaceCharacters(target.X, 1, glyph);
        }

        row.ReplaceAttributes(target.X, target.X + 1, attribute);
        TriggerRedraw(Viewport.FromDimensions(target, new TilSize(1, 1)));
    }

    /// <summary>Scrolls the circular buffer by one row: the oldest row is blanked and becomes the newest.</summary>
    public void IncrementCircularBuffer(TextAttribute fillAttributes = default)
    {
        PruneHyperlinks();

        GetMutableRowByOffset(0).Reset(fillAttributes);
        _firstRow++;
        if (_firstRow >= _height)
        {
            _firstRow = 0;
        }
    }

    /// <summary>The last cell that is not a space, searching from the bottom of <paramref name="viewOptional"/> (or the buffer).</summary>
    public TilPoint GetLastNonSpaceCharacter(Viewport? viewOptional = null)
    {
        var viewport = viewOptional ?? Size;

        int y = Math.Min(viewport.BottomInclusive, EstimateOffsetOfLastCommittedRow());
        int x = GetRowByOffset(y).MeasureRight() - 1;

        int viewportTop = viewport.Top;
        while (x < 0 && y > viewportTop)
        {
            y--;
            x = GetRowByOffset(y).MeasureRight() - 1;
        }

        return new TilPoint(Math.Max(x, 0), Math.Max(y, 0));
    }

    /// <summary>Moves <paramref name="size"/> rows starting at <paramref name="firstRow"/> by <paramref name="delta"/> rows.</summary>
    public void ScrollRows(int firstRow, int size, int delta)
    {
        if (delta == 0)
        {
            return;
        }

        size = Math.Max(0, size);

        int y;
        int end;
        int step;
        if (delta < 0)
        {
            // Moving up: copy from the top of the range down.
            y = firstRow;
            end = firstRow + size;
            step = 1;
        }
        else
        {
            // Moving down: copy from the bottom of the range up.
            y = firstRow + size - 1;
            end = firstRow - 1;
            step = -1;
        }

        for (; y != end; y += step)
        {
            CopyRow(y, y + delta, this);
        }
    }

    public void CopyRow(int srcRowIndex, int dstRowIndex, TextBuffer dstBuffer)
    {
        var dstRow = dstBuffer.GetMutableRowByOffset(dstRowIndex);
        var srcRow = GetRowByOffset(srcRowIndex);
        dstRow.CopyFrom(srcRow);
    }

    public void SetWrapForced(int y, bool wrap) => GetMutableRowByOffset(y).SetWrapForced(wrap);

    /// <summary>Sets the rendition of the cursor's row; a double-width row loses its right half.</summary>
    public void SetCurrentLineRendition(LineRendition lineRendition, TextAttribute fillAttributes)
    {
        var cursorPosition = _cursor.Position;
        int rowIndex = cursorPosition.Y;
        var row = GetMutableRowByOffset(rowIndex);
        if (row.LineRendition == lineRendition)
        {
            return;
        }

        row.LineRendition = lineRendition;
        row.SetWrapForced(false);
        if (lineRendition != LineRendition.SingleWidth)
        {
            int fillOffset = GetLineWidth(rowIndex);
            FillRect(new TilRect(fillOffset, rowIndex, int.MaxValue, rowIndex + 1), " ", fillAttributes);
            _cursor.SetPosition(ClampPositionWithinLine(cursorPosition));
        }

        TriggerRedraw(Viewport.FromDimensions(new TilPoint(0, rowIndex), new TilSize(_width, 1)));
    }

    public void ResetLineRenditionRange(int startRow, int endRow)
    {
        for (int row = startRow; row < endRow; row++)
        {
            GetMutableRowByOffset(row).LineRendition = LineRendition.SingleWidth;
        }
    }

    public LineRendition GetLineRendition(int row)
    {
        if (!IsRowCommitted(row))
        {
            return LineRendition.SingleWidth;
        }

        return GetRowByOffset(row).LineRendition;
    }

    public bool IsDoubleWidthLine(int row) => GetLineRendition(row) != LineRendition.SingleWidth;

    public int GetLineWidth(int row) => _width >> (IsDoubleWidthLine(row) ? 1 : 0);

    public TilPoint ClampPositionWithinLine(TilPoint position) => new(Math.Min(position.X, GetLineWidth(position.Y) - 1), position.Y);

    public TilPoint ScreenToBufferPosition(TilPoint position) => new(position.X >> (IsDoubleWidthLine(position.Y) ? 1 : 0), position.Y);

    public TilPoint BufferToScreenPosition(TilPoint position) => new(position.X << (IsDoubleWidthLine(position.Y) ? 1 : 0), position.Y);

    /// <summary>Blanks the whole buffer; new rows take the current attributes.</summary>
    public void Reset()
    {
        Decommit();
        _initialAttributes = _currentAttributes;
    }

    /// <summary>Drops the scrollback above <paramref name="newFirstRow"/>, keeping <paramref name="rowsToKeep"/> rows from there.</summary>
    public void ClearScrollback(int newFirstRow, int rowsToKeep)
    {
        if (newFirstRow <= 0)
        {
            return;
        }

        if (rowsToKeep <= 0)
        {
            Decommit();
            return;
        }

        ClearMarksInRange(new TilPoint(0, 0), new TilPoint(_width, Math.Max(0, newFirstRow - 1)));

        // Move the kept rows to the start of storage: reading relative to the old first row while writing from 0.
        int startAbsolute = _firstRow + newFirstRow;
        _firstRow = 0;
        ScrollRows(startAbsolute, rowsToKeep, -startAbsolute);

        int end = EstimateOffsetOfLastCommittedRow();
        for (int y = rowsToKeep; y <= end; y++)
        {
            GetMutableRowByOffset(y).Reset(_initialAttributes);
        }
    }

    /// <summary>Resizes without reflow: rows are cut or padded, keeping the cursor's row on screen.</summary>
    public void ResizeTraditional(TilSize newSize)
    {
        newSize = new TilSize(Math.Max(newSize.Width, 1), Math.Max(newSize.Height, 1));

        var newBuffer = new TextBuffer(newSize, _currentAttributes, 0, false, _renderer);
        int cursorRow = _cursor.Position.Y;
        int copyableRows = Math.Min(_height, newSize.Height);
        int srcRow = 0;

        if (cursorRow >= newSize.Height)
        {
            srcRow = cursorRow - newSize.Height + 1;
        }

        for (int dstRow = 0; dstRow < copyableRows; dstRow++, srcRow++)
        {
            CopyRow(srcRow, dstRow, newBuffer);
        }

        _rows = newBuffer._rows;
        _committedCount = newBuffer._committedCount;
        _initialAttributes = newBuffer._initialAttributes;
        _width = newBuffer._width;
        _height = newBuffer._height;
        _firstRow = 0;
    }

    public void NotifyPaintFrame()
    {
        if (_isActiveBuffer)
        {
            _renderer?.NotifyPaintFrame();
        }
    }

    public void TriggerRedraw(Viewport viewport)
    {
        if (_isActiveBuffer)
        {
            _renderer?.TriggerRedraw(viewport);
        }
    }

    public void TriggerRedrawAll()
    {
        if (_isActiveBuffer)
        {
            _renderer?.TriggerRedrawAll();
        }
    }

    public void TriggerScroll()
    {
        if (_isActiveBuffer)
        {
            _renderer?.TriggerScroll();
        }
    }

    public void TriggerScroll(TilPoint delta)
    {
        if (_isActiveBuffer)
        {
            _renderer?.TriggerScroll(delta);
        }
    }

    public void TriggerNewTextNotification(ReadOnlySpan<char> newText)
    {
        if (_isActiveBuffer)
        {
            _renderer?.TriggerNewTextNotification(newText);
        }
    }

    public void TriggerSelection()
    {
        if (_isActiveBuffer)
        {
            _renderer?.TriggerSelection();
        }
    }

    /// <summary>Whether any blinking text lies in the region, so the blink timer can stay off otherwise.</summary>
    public bool ContainsBlinkAttributeInRegion(Viewport region)
    {
        int bottom = Math.Min(region.BottomInclusive, EstimateOffsetOfLastCommittedRow());
        for (int y = region.Top; y < bottom; y++)
        {
            foreach (var run in GetRowByOffset(y).Attributes.Runs)
            {
                if (run.Value.IsBlinking())
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool IsGlyphDoubleWidthAt(TilPoint at) => IsRowCommitted(at.Y) && GetRow(at.Y).DbcsAttrAt(at.X) != DbcsAttribute.Single;
}
