// Ported from Windows Terminal src/buffer/out/Row.hpp, Row.cpp, LineRendition.hpp and Marks.hpp (MIT).

using Aprillz.MewUI.Terminal.Unicode;

namespace Aprillz.MewUI.Terminal.Buffer;

internal enum LineRendition : byte
{
    SingleWidth,
    DoubleWidth,
    DoubleHeightTop,
    DoubleHeightBottom,
}

internal enum DelimiterClass
{
    ControlChar,
    DelimiterChar,
    RegularChar,
}

internal enum DbcsAttribute : byte
{
    Single,
    Leading,
    Trailing,
}

internal enum MarkCategory : byte
{
    Default = 0,
    Error = 1,
    Warning = 2,
    Success = 3,
    Prompt = 4,
}

/// <summary>The shell-integration mark a row starts: its category, an optional color and the command's exit code.</summary>
internal sealed class ScrollbarData
{
    public MarkCategory Category { get; set; }

    public uint? Color { get; set; }

    public uint? ExitCode { get; set; }

    public ScrollbarData Clone() => new() { Category = Category, Color = Color, ExitCode = ExitCode };
}

/// <summary>Text to write into a row and where; on return, what was written and which columns changed.</summary>
internal ref struct RowWriteState
{
    // In: the text to write. Out: the part that did not fit.
    public ReadOnlySpan<char> Text;

    public int ColumnBegin;

    public int ColumnLimit;

    public int ColumnEnd;

    public int ColumnBeginDirty;

    public int ColumnEndDirty;

    public RowWriteState(ReadOnlySpan<char> text, int columnBegin, int columnLimit = int.MaxValue)
    {
        Text = text;
        ColumnBegin = columnBegin;
        ColumnLimit = columnLimit;
        ColumnEnd = 0;
        ColumnBeginDirty = 0;
        ColumnEndDirty = 0;
    }
}

/// <summary>A column range of a source row to copy into another row; on return, what was copied and which columns changed.</summary>
internal ref struct RowCopyTextFromState
{
    public Row Source;

    public int ColumnBegin;

    public int ColumnLimit;

    public int SourceColumnBegin;

    public int SourceColumnLimit;

    public int ColumnEnd;

    public int ColumnBeginDirty;

    public int ColumnEndDirty;

    public int SourceColumnEnd;

    public RowCopyTextFromState(Row source)
    {
        Source = source;
        ColumnBegin = 0;
        ColumnLimit = int.MaxValue;
        SourceColumnBegin = 0;
        SourceColumnLimit = int.MaxValue;
        ColumnEnd = 0;
        ColumnBeginDirty = 0;
        ColumnEndDirty = 0;
        SourceColumnEnd = 0;
    }
}

/// <summary>Maps an offset into a row's text back to the column that shows it.</summary>
internal ref struct CharToColumnMapper
{
    private const ushort CHAR_OFFSETS_TRAILER = 0x8000;
    private const ushort CHAR_OFFSETS_MASK = 0x7FFF;

    private readonly ReadOnlySpan<ushort> _charOffsets;
    private readonly int _charsLength;
    private readonly int _columnCount;
    private int _currentColumn;

    public CharToColumnMapper(ReadOnlySpan<ushort> charOffsets, int charsLength, int currentColumn, int columnCount)
    {
        _charOffsets = charOffsets;
        _charsLength = charsLength;
        _currentColumn = currentColumn;
        _columnCount = columnCount;
    }

    /// <summary>The first column of the glyph that holds the text at <paramref name="targetOffset"/>.</summary>
    public int GetLeadingColumnAt(int targetOffset)
    {
        targetOffset = Math.Clamp(targetOffset, 0, _charsLength);

        // Search forward past the target, then back over trailer columns, which compare above any offset.
        int column = _currentColumn;
        int currentOffset = _charOffsets[column];
        while (targetOffset > (currentOffset & CHAR_OFFSETS_MASK))
        {
            currentOffset = _charOffsets[++column];
        }

        while (targetOffset < currentOffset)
        {
            currentOffset = _charOffsets[--column];
        }

        _currentColumn = column;
        return column;
    }

    /// <summary>The last column of the glyph that holds the text at <paramref name="offset"/>.</summary>
    public int GetTrailingColumnAt(int offset)
    {
        int column = GetLeadingColumnAt(offset);
        if (column < _columnCount)
        {
            while ((_charOffsets[column + 1] & CHAR_OFFSETS_TRAILER) != 0)
            {
                column++;
            }
        }

        return column;
    }
}

/// <summary>
/// One row of the buffer: its text, a column-to-text offset per column (with a flag on the trailing columns of wide
/// glyphs), run-length attributes, and the wrap, padding and line rendition flags.
/// </summary>
/// <remarks>
/// <c>_charOffsets</c> has one entry per column plus one past the end. For "a猫d" in 4 columns it is 0,1,1|T,2,3: the
/// cat's second column carries its first column's offset and the trailer bit.
/// </remarks>
internal sealed class Row
{
    private const ushort CHAR_OFFSETS_TRAILER = 0x8000;
    private const ushort CHAR_OFFSETS_MASK = 0x7FFF;

    private readonly int _columnCount;
    private readonly ushort[] _charOffsets;
    private readonly AttributeRuns _attr;
    private char[] _chars;
    private LineRendition _lineRendition = LineRendition.SingleWidth;
    private bool _wrapForced;
    private bool _doubleBytePadded;
    private ScrollbarData? _promptData;

    public Row(int columnCount, TextAttribute fillAttribute)
    {
        if (columnCount is < 1 or > CHAR_OFFSETS_MASK)
        {
            throw new ArgumentOutOfRangeException(nameof(columnCount));
        }

        _columnCount = columnCount;
        _chars = new char[columnCount];
        _charOffsets = new ushort[columnCount + 1];
        _attr = new AttributeRuns(columnCount, fillAttribute);
        Init();
    }

    public int Size => _columnCount;

    public AttributeRuns Attributes => _attr;

    public bool WasWrapForced => _wrapForced;

    public bool WasDoubleBytePadded => _doubleBytePadded;

    public LineRendition LineRendition
    {
        get => _lineRendition;
        set => _lineRendition = value;
    }

    public ScrollbarData? ScrollbarData
    {
        get => _promptData;
        set => _promptData = value;
    }

    public void SetWrapForced(bool wrap) => _wrapForced = wrap;

    public void SetDoubleBytePadded(bool doubleBytePadded) => _doubleBytePadded = doubleBytePadded;

    /// <summary>One past the last column that holds text; a double-width row has half as many.</summary>
    public int GetReadableColumnCount()
    {
        int padded = _doubleBytePadded ? 1 : 0;
        if (_lineRendition == LineRendition.SingleWidth)
        {
            return _columnCount - padded;
        }

        return (_columnCount - (padded << 1)) >> 1;
    }

    /// <summary>Blanks the row with <paramref name="attr"/> and clears its flags.</summary>
    public void Reset(TextAttribute attr)
    {
        if (_chars.Length != _columnCount)
        {
            _chars = new char[_columnCount];
        }

        _attr.Reset(_columnCount, attr);
        _lineRendition = LineRendition.SingleWidth;
        _wrapForced = false;
        _doubleBytePadded = false;
        _promptData = null;
        Init();
    }

    private void Init()
    {
        _chars.AsSpan(0, _columnCount).Fill(' ');
        for (int column = 0; column <= _columnCount; column++)
        {
            _charOffsets[column] = (ushort)column;
        }
    }

    public void CopyFrom(Row source)
    {
        _lineRendition = source._lineRendition;
        _wrapForced = source._wrapForced;

        var state = new RowCopyTextFromState(source)
        {
            SourceColumnLimit = source.GetReadableColumnCount(),
        };
        CopyTextFrom(ref state);

        _attr.CopyFrom(source._attr);
        _attr.ResizeTrailingExtent(_columnCount);
    }

    /// <summary>The cursor position before <paramref name="column"/>, never inside a wide glyph.</summary>
    public int NavigateToPrevious(int column) => AdjustBackward(ClampedColumn(column - 1));

    /// <summary>The cursor position after <paramref name="column"/>, never inside a wide glyph.</summary>
    public int NavigateToNext(int column) => AdjustForward(ClampedColumnInclusive(column + 1));

    /// <summary>The first column of the glyph at <paramref name="column"/>.</summary>
    public int AdjustToGlyphStart(int column) => AdjustBackward(ClampedColumn(column));

    /// <summary>The exclusive end column of the glyph at <paramref name="column"/>.</summary>
    public int AdjustToGlyphEnd(int column) => AdjustForward(ClampedColumnInclusive(column));

    public void ClearCell(int column) => ReplaceCharacters(column, 1, " ");

    public void SetAttrToEnd(int columnBegin, TextAttribute attr) => _attr.Replace(ClampedColumnInclusive(columnBegin), _attr.Size, attr);

    public void ReplaceAttributes(int beginIndex, int endIndex, TextAttribute newAttr)
        => _attr.Replace(ClampedColumnInclusive(beginIndex), ClampedColumnInclusive(endIndex), newAttr);

    /// <summary>Writes one glyph of <paramref name="width"/> columns (1 or 2) at <paramref name="columnBegin"/>.</summary>
    public void ReplaceCharacters(int columnBegin, int width, ReadOnlySpan<char> chars)
    {
        var helper = new WriteHelper(this, columnBegin, _columnCount, chars);
        if (!helper.IsValid)
        {
            return;
        }

        helper.ReplaceCharacters(width);
        helper.Finish();
    }

    /// <summary>
    /// Writes as much of <see cref="RowWriteState.Text"/> as fits from <see cref="RowWriteState.ColumnBegin"/>, joining
    /// it with the glyph before it when they form one grapheme cluster.
    /// </summary>
    public void ReplaceText(ref RowWriteState state)
    {
        var helper = new WriteHelper(this, state.ColumnBegin, state.ColumnLimit, state.Text);
        if (!helper.IsValid)
        {
            state.ColumnEnd = helper.ColBeg;
            state.ColumnBeginDirty = helper.ColBeg;
            state.ColumnEndDirty = helper.ColBeg;
            return;
        }

        helper.ReplaceText();
        helper.Finish();

        state.Text = state.Text[helper.CharsConsumed..];

        // With text left over the end is the limit, so the padding column of an unplaced wide glyph gets the same
        // attributes and callers can detect wrapping by ColumnEnd >= ColumnLimit.
        state.ColumnEnd = state.Text.IsEmpty ? helper.ColEnd : helper.ColLimit;
        state.ColumnBeginDirty = helper.ColBegDirty;
        state.ColumnEndDirty = helper.ColEndDirty;
    }

    /// <summary>Copies the text of a column range of another row, keeping wide glyphs whole.</summary>
    public void CopyTextFrom(ref RowCopyTextFromState state)
    {
        var source = state.Source;
        int sourceColBeg = source.ClampedColumnInclusive(state.SourceColumnBegin);
        int sourceColLimit = source.ClampedColumnInclusive(state.SourceColumnLimit);
        ReadOnlySpan<ushort> charOffsets = default;
        ReadOnlySpan<char> chars = default;

        if (sourceColBeg < sourceColLimit)
        {
            charOffsets = source._charOffsets.AsSpan(sourceColBeg, sourceColLimit - sourceColBeg + 1);
            int begin = charOffsets[0] & CHAR_OFFSETS_MASK;
            int end = charOffsets[^1] & CHAR_OFFSETS_MASK;
            chars = source._chars.AsSpan(begin, end - begin);
        }

        var helper = new WriteHelper(this, state.ColumnBegin, state.ColumnLimit, chars);

        // Copying from itself would overwrite the offsets Finish reads; a valid range is at least two offsets long and
        // starts on a glyph's first column.
        if (!helper.IsValid ||
            ReferenceEquals(this, source) ||
            charOffsets.Length < 2 || (charOffsets[0] & CHAR_OFFSETS_TRAILER) != 0)
        {
            state.ColumnEnd = helper.ColBeg;
            state.ColumnBeginDirty = helper.ColBeg;
            state.ColumnEndDirty = helper.ColBeg;
            state.SourceColumnEnd = source._columnCount;
            return;
        }

        helper.CopyTextFrom(charOffsets);
        helper.Finish();

        state.ColumnEnd = helper.CharsConsumed == chars.Length ? helper.ColEnd : helper.ColLimit;
        state.ColumnBeginDirty = helper.ColBegDirty;
        state.ColumnEndDirty = helper.ColEndDirty;
        state.SourceColumnEnd = sourceColBeg + helper.ColEnd - helper.ColBeg;
    }

    public TextAttribute GetAttrByColumn(int column) => _attr.At(ClampedColumn(column));

    public List<ushort> GetHyperlinks()
    {
        var ids = new List<ushort>();
        foreach (var run in _attr.Runs)
        {
            if (run.Value.IsHyperlink())
            {
                ids.Add(run.Value.HyperlinkId);
            }
        }

        return ids;
    }

    /// <summary>The column after the last character that is not a space.</summary>
    public int GetLastNonSpaceColumn()
    {
        var text = GetText();
        int end = text.Length;
        int position = end;
        while (position != 0 && text[position - 1] == ' ')
        {
            position--;
        }

        // Spaces are one column each, so counting back from the readable end gives the column.
        return GetReadableColumnCount() - (end - position);
    }

    /// <summary>The number of leading spaces.</summary>
    public int MeasureLeft()
    {
        var text = GetText();
        int position = 0;
        while (position < text.Length && text[position] == ' ')
        {
            position++;
        }

        return position;
    }

    /// <summary>The column after the last character; a wrapped row counts as full, less its padding column.</summary>
    public int MeasureRight()
    {
        if (_wrapForced)
        {
            int width = _columnCount;
            if (_doubleBytePadded)
            {
                width--;
            }

            return width;
        }

        return GetLastNonSpaceColumn();
    }

    public bool ContainsText()
    {
        foreach (char character in GetText())
        {
            if (character != ' ')
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The text of the glyph at <paramref name="column"/>.</summary>
    public ReadOnlySpan<char> GlyphAt(int column)
    {
        int col = ClampedColumn(column);
        int begin = UncheckedCharOffset(col);
        while (UncheckedIsTrailer(++col))
        {
        }

        int end = UncheckedCharOffset(col);
        return _chars.AsSpan(begin, end - begin);
    }

    public DbcsAttribute DbcsAttrAt(int column)
    {
        int col = ClampedColumn(column);
        if (UncheckedIsTrailer(col))
        {
            return DbcsAttribute.Trailing;
        }

        if (UncheckedIsTrailer(col + 1))
        {
            return DbcsAttribute.Leading;
        }

        return DbcsAttribute.Single;
    }

    /// <summary>The text of the readable columns.</summary>
    public ReadOnlySpan<char> GetText()
    {
        int width = _charOffsets[GetReadableColumnCount()] & CHAR_OFFSETS_MASK;
        return _chars.AsSpan(0, width);
    }

    /// <summary>The text of the columns [<paramref name="columnBegin"/>, <paramref name="columnEnd"/>).</summary>
    public ReadOnlySpan<char> GetText(int columnBegin, int columnEnd)
    {
        int columns = GetReadableColumnCount();
        int colBeg = Math.Clamp(columnBegin, 0, columns);
        int colEnd = Math.Clamp(columnEnd, colBeg, columns);
        int chBeg = UncheckedCharOffset(colBeg);
        int chEnd = UncheckedCharOffset(colEnd);
        return _chars.AsSpan(chBeg, chEnd - chBeg);
    }

    public int GetLeadingColumnAtCharOffset(int offset) => CreateCharToColumnMapper(offset).GetLeadingColumnAt(offset);

    public int GetTrailingColumnAtCharOffset(int offset) => CreateCharToColumnMapper(offset).GetTrailingColumnAt(offset);

    public int GetCharOffset(int column)
    {
        int columns = GetReadableColumnCount();
        return UncheckedCharOffset(Math.Clamp(column, 0, columns));
    }

    public DelimiterClass DelimiterClassAt(int column, ReadOnlySpan<char> wordDelimiters)
    {
        int col = ClampedColumn(column);
        char glyph = _chars[UncheckedCharOffset(col)];

        if (glyph <= ' ')
        {
            return DelimiterClass.ControlChar;
        }

        if (wordDelimiters.IndexOf(glyph) >= 0)
        {
            return DelimiterClass.DelimiterChar;
        }

        return DelimiterClass.RegularChar;
    }

    /// <summary>Marks the row as the start of a prompt, unless it already starts a mark.</summary>
    public void StartPrompt()
    {
        _promptData ??= new ScrollbarData { Category = MarkCategory.Prompt };
    }

    /// <summary>Records the exit code of the command whose output ends on this row's mark.</summary>
    public void EndOutput(uint? error)
    {
        if (_promptData == null)
        {
            return;
        }

        _promptData.ExitCode = error;
        if (error.HasValue)
        {
            _promptData.Category = error.Value == 0 ? MarkCategory.Success : MarkCategory.Error;
        }
    }

    private int ClampedColumn(int value) => Math.Clamp(value, 0, _columnCount - 1);

    private int ClampedColumnInclusive(int value) => Math.Clamp(value, 0, _columnCount);

    private int CharSize() => _charOffsets[_columnCount];

    private int UncheckedCharOffset(int column) => _charOffsets[column] & CHAR_OFFSETS_MASK;

    private bool UncheckedIsTrailer(int column) => (_charOffsets[column] & CHAR_OFFSETS_TRAILER) != 0;

    private int AdjustBackward(int column)
    {
        while (UncheckedIsTrailer(column))
        {
            column--;
        }

        return column;
    }

    private int AdjustForward(int column)
    {
        while (UncheckedIsTrailer(column))
        {
            column++;
        }

        return column;
    }

    private CharToColumnMapper CreateCharToColumnMapper(int offset)
    {
        int lastChar = CharSize();

        // BMP glyphs are one char each, so the offset is a good first guess for the column.
        int guessedColumn = Math.Clamp(offset, 0, _columnCount);
        return new CharToColumnMapper(_charOffsets, lastChar, guessedColumn, _columnCount);
    }

    /// <summary>Moves the text after a replaced range when the replacement has a different length.</summary>
    private void ResizeChars(int colEndDirty, int chBegDirty, int chEndDirty, int chEndDirtyOld)
    {
        int diff = chEndDirty - chEndDirtyOld;
        int currentLength = CharSize();
        int newLength = currentLength + diff;

        if (newLength <= _chars.Length)
        {
            Array.Copy(_chars, chEndDirtyOld, _chars, chEndDirty, currentLength - chEndDirtyOld);
        }
        else
        {
            int minCapacity = Math.Min(ushort.MaxValue, _chars.Length + (_chars.Length >> 1));
            int newCapacity = Math.Max(newLength, minCapacity);
            if (newCapacity > ushort.MaxValue)
            {
                throw new InvalidOperationException("A row cannot hold more than 65535 characters.");
            }

            var chars = new char[newCapacity];
            Array.Copy(_chars, 0, chars, 0, chBegDirty);
            Array.Copy(_chars, chEndDirtyOld, chars, chEndDirty, currentLength - chEndDirtyOld);
            _chars = chars;
        }

        for (int column = colEndDirty; column <= _columnCount; column++)
        {
            _charOffsets[column] = (ushort)(_charOffsets[column] + diff);
        }
    }

    /// <summary>The shared steps of writing text into a range of columns.</summary>
    private ref struct WriteHelper
    {
        private readonly Row _row;
        private readonly ReadOnlySpan<char> _chars;

        public int ColBeg;
        public int ColLimit;
        public int ColEnd;
        public int ColBegDirty;
        public int ColEndDirty;
        public int ChBeg;
        public int ChBegDirty;
        public int LeadingSpaces;
        public int CharsConsumed;

        public WriteHelper(Row row, int columnBegin, int columnLimit, ReadOnlySpan<char> chars)
        {
            _row = row;
            _chars = chars;
            ColBeg = row.ClampedColumnInclusive(columnBegin);
            ColLimit = row.ClampedColumnInclusive(columnLimit);
            ChBegDirty = row.UncheckedCharOffset(ColBeg);
            ColBegDirty = row.AdjustBackward(ColBeg);
            LeadingSpaces = ColBeg - ColBegDirty;
            ChBeg = ChBegDirty + LeadingSpaces;
            ColEnd = ColBeg;
            ColEndDirty = 0;
            CharsConsumed = 0;
        }

        public readonly bool IsValid => ColBeg < ColLimit && !_chars.IsEmpty;

        public void ReplaceCharacters(int width)
        {
            int colEndNew = ColEnd + width;
            if (colEndNew > ColLimit)
            {
                ColEndDirty = ColLimit;
            }
            else
            {
                _row._charOffsets[ColEnd++] = (ushort)ChBeg;
                for (; ColEnd < colEndNew; ColEnd++)
                {
                    _row._charOffsets[ColEnd] = (ushort)(ChBeg | CHAR_OFFSETS_TRAILER);
                }

                ColEndDirty = ColEnd;
                CharsConsumed = _chars.Length;
            }
        }

        public void ReplaceText()
        {
            // ASCII is one column per char, so the columns left bound how much text can fit.
            int position = 0;
            int end = Math.Min(_chars.Length, ColLimit - ColBeg);
            int ch = ChBeg;

            while (position != end)
            {
                if (_chars[position] >= 0x80)
                {
                    ReplaceTextUnicode(ch, position);
                    return;
                }

                _row._charOffsets[ColEnd] = (ushort)ch;
                ColEnd++;
                ch++;
                position++;
            }

            ColEndDirty = ColEnd;
            CharsConsumed = ch - ChBeg;
        }

        private void ReplaceTextUnicode(int ch, int position)
        {
            var detector = CodepointWidthDetector.Singleton;

            if (position == 0)
            {
                // The new text may join the glyph before it into one grapheme cluster.
                int colPrev = ColBeg;
                while (colPrev > 0 && _row.UncheckedIsTrailer(--colPrev))
                {
                }

                int chPrev = _row.UncheckedCharOffset(colPrev);
                ReadOnlySpan<char> charsPrev = _row._chars.AsSpan(chPrev, ch - chPrev);

                var state = new GraphemeState();
                detector.GraphemeNext(ref state, charsPrev);
                state.Detach();
                detector.GraphemeNext(ref state, _chars);

                if (state.Len > 0)
                {
                    ColBegDirty = colPrev;
                    ColEnd = colPrev;

                    int width = Math.Max(1, state.Width);
                    int colEndNew = ColEnd + width;
                    if (colEndNew > ColLimit)
                    {
                        ColEndDirty = ColLimit;
                        CharsConsumed = ch - ChBeg;
                        return;
                    }

                    _row._charOffsets[ColEnd++] = (ushort)chPrev;
                    while (ColEnd < colEndNew)
                    {
                        _row._charOffsets[ColEnd++] = (ushort)(chPrev | CHAR_OFFSETS_TRAILER);
                    }

                    ch += state.Len;
                    position += state.Len;
                }
            }
            else
            {
                // A combining mark joins the ASCII char before it, so step back one and measure from there.
                ColEnd--;
                ch--;
                position--;
            }

            if (position != _chars.Length)
            {
                var state = new GraphemeState(position);
                state.Len = 0;
                do
                {
                    detector.GraphemeNext(ref state, _chars);

                    int width = Math.Max(1, state.Width);
                    int colEndNew = ColEnd + width;
                    if (colEndNew > ColLimit)
                    {
                        ColEndDirty = ColLimit;
                        CharsConsumed = ch - ChBeg;
                        return;
                    }

                    _row._charOffsets[ColEnd++] = (ushort)ch;
                    while (ColEnd < colEndNew)
                    {
                        _row._charOffsets[ColEnd++] = (ushort)(ch | CHAR_OFFSETS_TRAILER);
                    }

                    ch += state.Len;
                    position += state.Len;
                }
                while (position != _chars.Length);
            }

            ColEndDirty = ColEnd;
            CharsConsumed = ch - ChBeg;
        }

        public void CopyTextFrom(ReadOnlySpan<ushort> charOffsets)
        {
            // The input offsets are per column too, so the end offset is a lookup, moved off a wide glyph's trailer.
            int colEndDirtyInput = Math.Min(ColLimit - ColBeg, charOffsets.Length - 1);
            int colEndInput = colEndDirtyInput;
            while ((charOffsets[colEndInput] & CHAR_OFFSETS_TRAILER) != 0)
            {
                colEndInput--;
            }

            int baseOffset = charOffsets[0];
            int endOffset = charOffsets[colEndInput];
            int inToOutOffset = ChBeg - baseOffset;

            for (int index = 0; index < colEndInput; index++)
            {
                int value = charOffsets[index];
                int offset = (value & CHAR_OFFSETS_MASK) + inToOutOffset;
                _row._charOffsets[ColEnd + index] = (ushort)(offset | (value & CHAR_OFFSETS_TRAILER));
            }

            ColEnd += colEndInput;
            ColEndDirty = ColBeg + colEndDirtyInput;
            CharsConsumed = endOffset - baseOffset;
        }

        public void Finish()
        {
            ColEndDirty = _row.AdjustForward(ColEndDirty);

            int trailingSpaces = ColEndDirty - ColEnd;
            int chEndDirtyOld = _row.UncheckedCharOffset(ColEndDirty);
            int chEndDirty = ChBegDirty + CharsConsumed + LeadingSpaces + trailingSpaces;

            if (chEndDirty != chEndDirtyOld)
            {
                _row.ResizeChars(ColEndDirty, ChBegDirty, chEndDirty, chEndDirtyOld);
            }

            _chars[..CharsConsumed].CopyTo(_row._chars.AsSpan(ChBeg));

            if (LeadingSpaces != 0)
            {
                _row._chars.AsSpan(ChBegDirty, LeadingSpaces).Fill(' ');
                for (int index = 0; index < LeadingSpaces; index++)
                {
                    _row._charOffsets[ColBegDirty + index] = (ushort)(ChBegDirty + index);
                }
            }

            if (trailingSpaces != 0)
            {
                _row._chars.AsSpan(ChBeg + CharsConsumed, trailingSpaces).Fill(' ');
                for (int index = 0; index < trailingSpaces; index++)
                {
                    _row._charOffsets[ColEnd + index] = (ushort)(ChBeg + CharsConsumed + index);
                }
            }

            // Writing the last column decides whether it holds padding for a wide glyph that did not fit, which reflow
            // then ignores; a narrow glyph written over the left half of a wide one leaves padding too.
            if (ColEndDirty == _row._columnCount)
            {
                _row.SetDoubleBytePadded(ColEnd < _row._columnCount);
            }
        }
    }
}
