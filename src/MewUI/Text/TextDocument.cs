using System.Buffers;

namespace Aprillz.MewUI.Text;

internal sealed class TextDocument
{
    private char[] _buffer;
    private int _gapStart;
    private int _gapEnd;

    public TextDocument(int initialCapacity = 256)
    {
        initialCapacity = Math.Max(16, initialCapacity);
        _buffer = ArrayPool<char>.Shared.Rent(initialCapacity);
        _gapStart = 0;
        _gapEnd = _buffer.Length;
    }

    public int Length => _buffer.Length - (_gapEnd - _gapStart);

    public char this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            int gapSize = _gapEnd - _gapStart;
            return index < _gapStart ? _buffer[index] : _buffer[index + gapSize];
        }
    }

    public void SetText(string text)
    {
        Clear();
        if (string.IsNullOrEmpty(text))
            return;

        Insert(0, text.AsSpan());
    }

    public void Clear()
    {
        _gapStart = 0;
        _gapEnd = _buffer.Length;
    }

    public void Insert(int index, ReadOnlySpan<char> text)
    {
        if ((uint)index > (uint)Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (text.Length == 0)
            return;

        MoveGap(index);
        EnsureGap(text.Length);

        text.CopyTo(_buffer.AsSpan(_gapStart, text.Length));
        _gapStart += text.Length;
    }

    public void Remove(int index, int length)
    {
        if (length <= 0)
            return;

        if ((uint)index > (uint)Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        if (index + length > Length)
            length = Length - index;

        if (length <= 0)
            return;

        MoveGap(index);
        _gapEnd += length;
    }

    public void CopyTo(Span<char> destination, int start, int length)
    {
        if (length == 0)
            return;

        if ((uint)start > (uint)Length || start + length > Length)
            throw new ArgumentOutOfRangeException(nameof(start));

        int gapSize = _gapEnd - _gapStart;
        if (start + length <= _gapStart)
        {
            _buffer.AsSpan(start, length).CopyTo(destination);
            return;
        }

        if (start >= _gapStart)
        {
            _buffer.AsSpan(start + gapSize, length).CopyTo(destination);
            return;
        }

        int leftLen = _gapStart - start;
        _buffer.AsSpan(start, leftLen).CopyTo(destination);
        int rightLen = length - leftLen;
        _buffer.AsSpan(_gapEnd, rightLen).CopyTo(destination.Slice(leftLen, rightLen));
    }

    public string GetText()
    {
        int length = Length;
        if (length == 0)
            return string.Empty;

        return string.Create(length, this, static (span, doc) => doc.CopyTo(span, 0, span.Length));
    }

    public string GetText(int start, int length)
    {
        if (length <= 0)
            return string.Empty;

        if ((uint)start > (uint)Length || start + length > Length)
            throw new ArgumentOutOfRangeException(nameof(start));

        return string.Create(length, (doc: this, start), static (span, state) =>
            state.doc.CopyTo(span, state.start, span.Length));
    }

    public void Dispose()
    {
        ArrayPool<char>.Shared.Return(_buffer);
        _buffer = Array.Empty<char>();
        _gapStart = 0;
        _gapEnd = 0;
    }

    private void EnsureGap(int required)
    {
        int gapSize = _gapEnd - _gapStart;
        if (gapSize >= required)
            return;

        int needed = required - gapSize;
        int newSize = Math.Max(_buffer.Length * 2, _buffer.Length + needed + 16);
        var newBuffer = ArrayPool<char>.Shared.Rent(newSize);

        int leftLen = _gapStart;
        _buffer.AsSpan(0, leftLen).CopyTo(newBuffer);

        int rightLen = _buffer.Length - _gapEnd;
        int newGapEnd = newSize - rightLen;
        _buffer.AsSpan(_gapEnd, rightLen).CopyTo(newBuffer.AsSpan(newGapEnd, rightLen));

        ArrayPool<char>.Shared.Return(_buffer);
        _buffer = newBuffer;
        _gapEnd = newGapEnd;
    }

    private void MoveGap(int index)
    {
        if (index == _gapStart)
            return;

        if (index < _gapStart)
        {
            int move = _gapStart - index;
            _buffer.AsSpan(index, move).CopyTo(_buffer.AsSpan(_gapEnd - move, move));
            _gapStart -= move;
            _gapEnd -= move;
            return;
        }

        int gapSize = _gapEnd - _gapStart;
        int target = index + gapSize;
        int moveForward = target - _gapEnd;
        _buffer.AsSpan(_gapEnd, moveForward).CopyTo(_buffer.AsSpan(_gapStart, moveForward));
        _gapStart += moveForward;
        _gapEnd += moveForward;
    }
}
