// Ported from Windows Terminal src/inc/til/rle.h (MIT): the operations ROW and TextBuffer use, without the optimized
// in-place replace.

namespace Aprillz.MewUI.Terminal.Buffer;

internal struct AttributeRun
{
    public TextAttribute Value;
    public int Length;

    public AttributeRun(TextAttribute value, int length)
    {
        Value = value;
        Length = length;
    }

    public override readonly string ToString() => $"{Value} x {Length}";
}

/// <summary>
/// One <see cref="TextAttribute"/> per column of a row, stored as runs of equal values.
/// </summary>
internal sealed class AttributeRuns
{
    // Per-thread working space for replace, so a write allocates nothing.
    [ThreadStatic]
    private static AttributeRun[]? _scratch;

    private AttributeRun[] _runs;
    private int _count;
    private int _totalLength;

    public AttributeRuns(int length, TextAttribute fill)
    {
        _runs = new AttributeRun[4];
        _count = 0;
        _totalLength = 0;
        Reset(length, fill);
    }

    private AttributeRuns(AttributeRun[] runs, int count, int totalLength)
    {
        _runs = runs;
        _count = count;
        _totalLength = totalLength;
    }

    /// <summary>The number of columns the runs cover.</summary>
    public int Size => _totalLength;

    public bool IsEmpty => _totalLength == 0;

    public ReadOnlySpan<AttributeRun> Runs => _runs.AsSpan(0, _count);

    /// <summary>The runs for in-place value changes; a change must keep adjacent values different or call <see cref="Compact"/>.</summary>
    public Span<AttributeRun> MutableRuns => _runs.AsSpan(0, _count);

    public void Reset(int length, TextAttribute fill)
    {
        _count = 0;
        _totalLength = length;
        if (length > 0)
        {
            _runs[0] = new AttributeRun(fill, length);
            _count = 1;
        }
    }

    public void CopyFrom(AttributeRuns source)
    {
        if (_runs.Length < source._count)
        {
            _runs = new AttributeRun[source._count];
        }

        Array.Copy(source._runs, _runs, source._count);
        _count = source._count;
        _totalLength = source._totalLength;
    }

    public AttributeRuns Clone()
    {
        var runs = new AttributeRun[Math.Max(1, _count)];
        Array.Copy(_runs, runs, _count);
        return new AttributeRuns(runs, _count, _totalLength);
    }

    /// <summary>The value at a column.</summary>
    public TextAttribute At(int position)
    {
        int runIndex = Scan(position, out _);
        if (runIndex >= _count)
        {
            throw new ArgumentOutOfRangeException(nameof(position));
        }

        return _runs[runIndex].Value;
    }

    /// <summary>The columns [<paramref name="startIndex"/>, <paramref name="endIndex"/>) as new runs.</summary>
    public AttributeRuns Slice(int startIndex, int endIndex)
    {
        if (endIndex > _totalLength)
        {
            endIndex = _totalLength;
        }

        if (startIndex >= endIndex)
        {
            return new AttributeRuns([], 0, 0);
        }

        int beginRun = Scan(startIndex, out int startRunPosition);
        int endRun = Scan(endIndex - 1, out int endRunPosition);

        int count = endRun - beginRun + 1;
        var runs = new AttributeRun[count];
        Array.Copy(_runs, beginRun, runs, 0, count);
        runs[count - 1].Length = endRunPosition + 1;
        runs[0].Length -= startRunPosition;
        return new AttributeRuns(runs, count, endIndex - startIndex);
    }

    /// <summary>Replaces the columns [<paramref name="startIndex"/>, <paramref name="endIndex"/>) with one value.</summary>
    public void Replace(int startIndex, int endIndex, TextAttribute value)
    {
        CheckIndices(startIndex, ref endIndex);
        Span<AttributeRun> replacement = [new AttributeRun(value, endIndex - startIndex)];
        ReplaceUnchecked(startIndex, endIndex, replacement);
    }

    /// <summary>Replaces the columns [<paramref name="startIndex"/>, <paramref name="endIndex"/>) with the runs of another row; the size changes by the difference.</summary>
    public void Replace(int startIndex, int endIndex, AttributeRuns replacements)
    {
        CheckIndices(startIndex, ref endIndex);
        ReplaceUnchecked(startIndex, endIndex, replacements.Runs);
    }

    /// <summary>Changes the size: growing extends the last run, shrinking cuts trailing runs.</summary>
    public void ResizeTrailingExtent(int newSize)
    {
        if (newSize == 0)
        {
            _count = 0;
        }
        else if (newSize < _totalLength)
        {
            int run = Scan(newSize - 1, out int position);
            _runs[run].Length = position + 1;
            _count = run + 1;
        }
        else if (newSize > _totalLength)
        {
            if (_count == 0)
            {
                throw new InvalidOperationException("An empty run list cannot be extended.");
            }

            _runs[_count - 1].Length += newSize - _totalLength;
        }

        _totalLength = newSize;
    }

    public void ReplaceValues(TextAttribute oldValue, TextAttribute newValue)
    {
        for (int index = 0; index < _count; index++)
        {
            if (_runs[index].Value == oldValue)
            {
                _runs[index].Value = newValue;
            }
        }

        Compact();
    }

    /// <summary>Merges adjacent runs with equal values.</summary>
    public void Compact()
    {
        if (_count == 0)
        {
            return;
        }

        int write = 0;
        for (int read = 1; read < _count; read++)
        {
            if (_runs[read].Value == _runs[write].Value)
            {
                _runs[write].Length += _runs[read].Length;
            }
            else
            {
                _runs[++write] = _runs[read];
            }
        }

        _count = write + 1;
    }

    public bool ContentEquals(AttributeRuns other) => _totalLength == other._totalLength && Runs.SequenceEqual(other.Runs);

    /// <summary>Finds the run holding <paramref name="position"/> and the position within it; the run count when past the end.</summary>
    private int Scan(int position, out int runPosition)
    {
        int total = 0;
        for (int index = 0; index < _count; index++)
        {
            int length = _runs[index].Length;
            if (position < total + length)
            {
                runPosition = position - total;
                return index;
            }

            total += length;
        }

        runPosition = 0;
        return _count;
    }

    private void CheckIndices(int startIndex, ref int endIndex)
    {
        if (endIndex > _totalLength)
        {
            endIndex = _totalLength;
        }

        if (startIndex > endIndex || startIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(startIndex), "start_index <= end_index");
        }
    }

    private void ReplaceUnchecked(int startIndex, int endIndex, ReadOnlySpan<AttributeRun> replacements)
    {
        // Split the runs into the part before startIndex, the replacements, and the part from endIndex on.
        int capacity = _count + replacements.Length + 1;
        var result = _scratch;
        if (result == null || result.Length < capacity)
        {
            result = new AttributeRun[Math.Max(capacity, 16)];
            _scratch = result;
        }

        int resultCount = 0;
        int position = 0;
        for (int index = 0; index < _count; index++)
        {
            var run = _runs[index];
            int runStart = position;
            int runEnd = position + run.Length;
            position = runEnd;

            if (runStart < startIndex)
            {
                int keep = Math.Min(runEnd, startIndex) - runStart;
                result[resultCount++] = new AttributeRun(run.Value, keep);
            }
        }

        int replacedLength = 0;
        foreach (var replacement in replacements)
        {
            if (replacement.Length > 0)
            {
                result[resultCount++] = replacement;
                replacedLength += replacement.Length;
            }
        }

        position = 0;
        for (int index = 0; index < _count; index++)
        {
            var run = _runs[index];
            int runStart = position;
            int runEnd = position + run.Length;
            position = runEnd;

            if (runEnd > endIndex)
            {
                int keepStart = Math.Max(runStart, endIndex);
                result[resultCount++] = new AttributeRun(run.Value, runEnd - keepStart);
            }
        }

        if (_runs.Length < resultCount)
        {
            _runs = new AttributeRun[Math.Max(resultCount, _runs.Length * 2)];
        }

        Array.Copy(result, _runs, resultCount);
        _count = resultCount;
        _totalLength = _totalLength - (endIndex - startIndex) + replacedLength;
        Compact();
    }
}
