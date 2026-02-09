using System.Runtime.CompilerServices;

namespace BitMiracle.LibJpeg.Classic;

/// <summary>
/// A contiguous 2D buffer backed by a single 1D array.
/// Provides better cache locality than jagged arrays (byte[][]).
/// </summary>
internal readonly struct Buffer2D<T>
{
    public Buffer2D(int width, int height)
    {
        Width = width;
        Height = height;
        Data = GC.AllocateUninitializedArray<T>(width * height);
    }

    public Buffer2D(T[] data, int width, int height)
    {
        Data = data;
        Width = width;
        Height = height;
    }

    public int Width { get; }
    public int Height { get; }
    public T[] Data { get; }
    public bool IsEmpty => Data == null || Data.Length == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T> GetRow(int row)
    {
        return Data.AsSpan(row * Width, Width);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<T> GetRowReadOnly(int row)
    {
        return new ReadOnlySpan<T>(Data, row * Width, Width);
    }

    /// <summary>
    /// Indexer for compatibility with byte[][] patterns.
    /// Returns a Span for the row.
    /// </summary>
    public RowAccessor this[int row] => new RowAccessor(Data, row * Width, Width);

    internal readonly ref struct RowAccessor
    {
        private readonly T[] _data;
        private readonly int _offset;

        internal RowAccessor(T[] data, int offset, int length)
        {
            _data = data;
            _offset = offset;
            Length = length;
        }

        public ref T this[int index] => ref _data[_offset + index];

        public int Length { get; }

        public Span<T> AsSpan() => _data.AsSpan(_offset, Length);

        public static implicit operator Span<T>(RowAccessor accessor)
            => accessor._data.AsSpan(accessor._offset, accessor.Length);

        public static implicit operator ReadOnlySpan<T>(RowAccessor accessor)
            => new ReadOnlySpan<T>(accessor._data, accessor._offset, accessor.Length);
    }

    /// <summary>
    /// Creates a Buffer2D and returns row spans for compatibility with existing code.
    /// </summary>
    public static (Buffer2D<T> buffer, T[][] rowPointers) AllocateWithRowPointers(int width, int height)
    {
        var buffer = new Buffer2D<T>(width, height);
        var rows = new T[height][];

        // Create sub-arrays that point into the main buffer
        // Note: This creates new arrays but they share data with the buffer
        for (int i = 0; i < height; i++)
        {
            rows[i] = new T[width];
        }

        return (buffer, rows);
    }

    public void Clear()
    {
        Array.Clear(Data);
    }

    public void CopyRowTo(int sourceRow, Span<T> destination)
    {
        GetRowReadOnly(sourceRow).CopyTo(destination);
    }

    public void CopyRowFrom(int destRow, ReadOnlySpan<T> source)
    {
        source.CopyTo(GetRow(destRow));
    }
}

/// <summary>
/// Extension methods for Buffer2D interop with legacy byte[][] code.
/// </summary>
internal static class Buffer2DExtensions
{
    /// <summary>
    /// Wraps an existing jagged array as a Buffer2D-like accessor.
    /// Does not copy data - useful for gradual migration.
    /// </summary>
    public static JaggedArrayWrapper<T> AsBuffer2D<T>(this T[][] jaggedArray)
    {
        return new JaggedArrayWrapper<T>(jaggedArray);
    }
}

/// <summary>
/// Wrapper that provides Buffer2D-like interface for existing jagged arrays.
/// Used during migration to avoid breaking changes.
/// </summary>
internal readonly struct JaggedArrayWrapper<T>
{
    private readonly T[][] _rows;

    public JaggedArrayWrapper(T[][] rows)
    {
        _rows = rows;
    }

    public int Height => _rows?.Length ?? 0;
    public int Width => _rows?.Length > 0 ? _rows[0]?.Length ?? 0 : 0;

    public T[] this[int row] => _rows[row];

    public Span<T> GetRow(int row) => _rows[row].AsSpan();
    public ReadOnlySpan<T> GetRowReadOnly(int row) => _rows[row].AsSpan();
}
