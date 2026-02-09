using System.Buffers;
using System.Runtime.CompilerServices;
using System.Text;

namespace Aprillz.MewUI.Controls.Text;

internal sealed class TextDocument : IDisposable
{
    private string _original = string.Empty;
    private readonly StringBuilder _added = new();
    private readonly List<Piece> _pieces = new();
    private int[]? _lineStarts;
    private int _lineStartsVersion = -1;

    private readonly struct Piece
    {
        public readonly bool IsOriginal;
        public readonly int Start;
        public readonly int Length;

        public Piece(bool isOriginal, int start, int length)
        {
            IsOriginal = isOriginal;
            Start = start;
            Length = length;
        }
    }

    public TextDocument(int initialCapacity = 256)
    {
        // initialCapacity hint ignored for piece table
    }

    public int Length { get; private set; }

    public bool IsEmpty => Length == 0;

    public int Version { get; private set; }

    public char this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            int offset = 0;
            for (int i = 0; i < _pieces.Count; i++)
            {
                var p = _pieces[i];
                if (index < offset + p.Length)
                {
                    int localIndex = index - offset + p.Start;
                    return p.IsOriginal ? _original[localIndex] : _added[localIndex];
                }
                offset += p.Length;
            }

            throw new InvalidOperationException("Index out of range in piece table");
        }
    }

    public void SetText(string text)
    {
        _original = text ?? string.Empty;
        _added.Clear();
        _pieces.Clear();

        if (_original.Length > 0)
        {
            _pieces.Add(new Piece(true, 0, _original.Length));
        }

        Length = _original.Length;
        Version++;
    }

    public void Clear()
    {
        _original = string.Empty;
        _added.Clear();
        _pieces.Clear();
        Length = 0;
        Version++;
    }

    public void Insert(int index, ReadOnlySpan<char> text)
    {
        if ((uint)index > (uint)Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (text.Length == 0)
        {
            return;
        }

        int addedStart = _added.Length;
        _added.Append(text);
        var newPiece = new Piece(false, addedStart, text.Length);

        if (_pieces.Count == 0)
        {
            _pieces.Add(newPiece);
            Length += text.Length;
            Version++;
            return;
        }

        int pieceIndex = FindPieceIndex(index, out int localOffset);

        if (localOffset == 0)
        {
            _pieces.Insert(pieceIndex, newPiece);
        }
        else
        {
            var oldPiece = _pieces[pieceIndex];
            if (localOffset == oldPiece.Length)
            {
                _pieces.Insert(pieceIndex + 1, newPiece);
            }
            else
            {
                var left = new Piece(oldPiece.IsOriginal, oldPiece.Start, localOffset);
                var right = new Piece(oldPiece.IsOriginal, oldPiece.Start + localOffset, oldPiece.Length - localOffset);

                _pieces[pieceIndex] = left;
                _pieces.Insert(pieceIndex + 1, newPiece);
                _pieces.Insert(pieceIndex + 2, right);
            }
        }

        Length += text.Length;
        Version++;
    }

    public void Remove(int index, int length)
    {
        if (length <= 0)
        {
            return;
        }

        if ((uint)index > (uint)Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        if (index + length > Length)
        {
            length = Length - index;
        }

        if (length <= 0)
        {
            return;
        }

        int startPiece = FindPieceIndex(index, out int startOffset);
        int endPiece = FindPieceIndex(index + length, out int endOffset);

        if (startPiece == endPiece)
        {
            var p = _pieces[startPiece];
            if (startOffset == 0 && endOffset == p.Length)
            {
                _pieces.RemoveAt(startPiece);
            }
            else if (startOffset == 0)
            {
                _pieces[startPiece] = new Piece(p.IsOriginal, p.Start + length, p.Length - length);
            }
            else if (endOffset == p.Length)
            {
                _pieces[startPiece] = new Piece(p.IsOriginal, p.Start, startOffset);
            }
            else
            {
                var left = new Piece(p.IsOriginal, p.Start, startOffset);
                var right = new Piece(p.IsOriginal, p.Start + endOffset, p.Length - endOffset);
                _pieces[startPiece] = left;
                _pieces.Insert(startPiece + 1, right);
            }
        }
        else
        {
            var modifications = new List<(int index, Piece? replacement)>();

            var startP = _pieces[startPiece];
            if (startOffset == 0)
            {
                modifications.Add((startPiece, null));
            }
            else
            {
                modifications.Add((startPiece, new Piece(startP.IsOriginal, startP.Start, startOffset)));
            }

            for (int i = startPiece + 1; i < endPiece; i++)
            {
                modifications.Add((i, null));
            }

            if (endPiece < _pieces.Count)
            {
                var endP = _pieces[endPiece];
                if (endOffset == endP.Length)
                {
                    modifications.Add((endPiece, null));
                }
                else if (endOffset > 0)
                {
                    modifications.Add((endPiece, new Piece(endP.IsOriginal, endP.Start + endOffset, endP.Length - endOffset)));
                }
            }

            for (int i = modifications.Count - 1; i >= 0; i--)
            {
                var (idx, replacement) = modifications[i];
                if (replacement == null)
                {
                    _pieces.RemoveAt(idx);
                }
                else
                {
                    _pieces[idx] = replacement.Value;
                }
            }
        }

        Length -= length;
        Version++;
    }

    public void CopyTo(Span<char> destination, int start, int length)
    {
        if (length == 0)
        {
            return;
        }

        if ((uint)start > (uint)Length || start + length > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        int remaining = length;
        int destOffset = 0;
        int docOffset = 0;

        for (int i = 0; i < _pieces.Count && remaining > 0; i++)
        {
            var p = _pieces[i];
            int pieceEnd = docOffset + p.Length;

            if (pieceEnd <= start)
            {
                docOffset = pieceEnd;
                continue;
            }

            int copyStart = Math.Max(0, start - docOffset);
            int copyEnd = Math.Min(p.Length, start + length - docOffset);
            int copyLen = copyEnd - copyStart;

            if (copyLen > 0)
            {
                if (p.IsOriginal)
                {
                    _original.AsSpan(p.Start + copyStart, copyLen).CopyTo(destination.Slice(destOffset));
                }
                else
                {
                    for (int j = 0; j < copyLen; j++)
                    {
                        destination[destOffset + j] = _added[p.Start + copyStart + j];
                    }
                }

                destOffset += copyLen;
                remaining -= copyLen;
            }

            docOffset = pieceEnd;
        }
    }

    public string GetText()
    {
        if (Length == 0)
        {
            return string.Empty;
        }

        return string.Create(Length, this, static (span, doc) => doc.CopyTo(span, 0, span.Length));
    }

    public string GetText(int start, int length)
    {
        if (length <= 0)
        {
            return string.Empty;
        }

        if ((uint)start > (uint)Length || start + length > Length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        return string.Create(length, (doc: this, start), static (span, state) =>
            state.doc.CopyTo(span, state.start, span.Length));
    }

    #region Line Operations

    public int LineCount
    {
        get
        {
            EnsureLineStarts();
            return _lineStarts!.Length;
        }
    }

    public int GetLineFromIndex(int charIndex)
    {
        if (charIndex < 0)
        {
            return 0;
        }

        if (charIndex >= Length)
        {
            return Math.Max(0, LineCount - 1);
        }

        EnsureLineStarts();
        return BinarySearchLine(_lineStarts!, charIndex);
    }

    public int GetLineStartIndex(int lineNumber)
    {
        EnsureLineStarts();
        if ((uint)lineNumber >= (uint)_lineStarts!.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumber));
        }

        return _lineStarts[lineNumber];
    }

    public int GetLineLength(int lineNumber)
    {
        EnsureLineStarts();
        if ((uint)lineNumber >= (uint)_lineStarts!.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(lineNumber));
        }

        int start = _lineStarts[lineNumber];
        int end = lineNumber + 1 < _lineStarts.Length ? _lineStarts[lineNumber + 1] : Length;

        int len = end - start;
        if (len > 0 && this[end - 1] == '\n')
        {
            len--;
        }

        return len;
    }

    public ReadOnlySpan<char> GetLineSpan(int lineNumber, Span<char> buffer)
    {
        int start = GetLineStartIndex(lineNumber);
        int len = GetLineLength(lineNumber);

        if (len == 0)
        {
            return ReadOnlySpan<char>.Empty;
        }

        if (buffer.Length < len)
        {
            throw new ArgumentException("Buffer too small", nameof(buffer));
        }

        CopyTo(buffer, start, len);
        return buffer.Slice(0, len);
    }

    private void EnsureLineStarts()
    {
        if (_lineStarts != null && _lineStartsVersion == Version)
        {
            return;
        }

        var starts = new List<int> { 0 };

        int offset = 0;
        for (int i = 0; i < _pieces.Count; i++)
        {
            var p = _pieces[i];
            ReadOnlySpan<char> span = p.IsOriginal
                ? _original.AsSpan(p.Start, p.Length)
                : GetAddedSpan(p.Start, p.Length);

            for (int j = 0; j < span.Length; j++)
            {
                if (span[j] == '\n')
                {
                    starts.Add(offset + j + 1);
                }
            }

            offset += p.Length;
        }

        _lineStarts = starts.ToArray();
        _lineStartsVersion = Version;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ReadOnlySpan<char> GetAddedSpan(int start, int length)
    {
        char[] buffer = ArrayPool<char>.Shared.Rent(length);
        try
        {
            for (int i = 0; i < length; i++)
            {
                buffer[i] = _added[start + i];
            }
            return buffer.AsSpan(0, length);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
        }
    }

    private static int BinarySearchLine(int[] lineStarts, int charIndex)
    {
        int lo = 0;
        int hi = lineStarts.Length - 1;

        while (lo < hi)
        {
            int mid = lo + (hi - lo + 1) / 2;
            if (lineStarts[mid] <= charIndex)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return lo;
    }

    #endregion

    public void Dispose()
    {
        _original = string.Empty;
        _added.Clear();
        _pieces.Clear();
        _lineStarts = null;
        Length = 0;
    }

    private int FindPieceIndex(int index, out int localOffset)
    {
        int offset = 0;
        for (int i = 0; i < _pieces.Count; i++)
        {
            var p = _pieces[i];
            if (index <= offset + p.Length)
            {
                localOffset = index - offset;
                return i;
            }
            offset += p.Length;
        }

        localOffset = 0;
        return _pieces.Count;
    }
}
