using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI;

/// <summary>
/// Mutable bitmap source (BGRA32, straight alpha). Similar to WPF's WriteableBitmap.
/// Backends are expected to upload pixels as needed when <see cref="Version"/> changes.
/// </summary>
public class WriteableBitmap : IImageSource, INotifyImageChanged, IPixelBufferSource, IDisposable
{
    private readonly object _lock = new();
    private byte[] _bgra;
    private int _version;
    private bool _disposed;
    private PixelRegion? _dirtyRegion;

    /// <summary>
    /// Gets the bitmap width in pixels.
    /// </summary>
    public int PixelWidth { get; }

    /// <summary>
    /// Gets the bitmap height in pixels.
    /// </summary>
    public int PixelHeight { get; }

    /// <summary>
    /// Gets the stride (bytes per row).
    /// </summary>
    public int StrideBytes => PixelWidth * 4;

    /// <summary>
    /// Gets the pixel format (always BGRA32).
    /// </summary>
    public BitmapPixelFormat PixelFormat => BitmapPixelFormat.Bgra32;

    /// <summary>
    /// Monotonically increasing version. Incremented after any committed write.
    /// </summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>
    /// Raised when pixels have changed and the bitmap should be re-uploaded/redrawn.
    /// </summary>
    public event Action? Changed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WriteableBitmap"/> class.
    /// </summary>
    /// <param name="widthPx">Bitmap width in pixels.</param>
    /// <param name="heightPx">Bitmap height in pixels.</param>
    /// <param name="clear"><see langword="true"/> to clear the buffer to zero; otherwise leaves it uninitialized.</param>
    public WriteableBitmap(int widthPx, int heightPx, bool clear = true)
    {
        if (widthPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(widthPx));
        }

        if (heightPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(heightPx));
        }

        PixelWidth = widthPx;
        PixelHeight = heightPx;
        _bgra = GC.AllocateUninitializedArray<byte>(checked(widthPx * heightPx * 4));

        if (clear)
        {
            Array.Clear(_bgra);
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="WriteableBitmap"/> class from a decoded bitmap.
    /// The pixel buffer is taken as-is (BGRA32, straight alpha).
    /// </summary>
    /// <param name="bitmap">The decoded bitmap.</param>
    public WriteableBitmap(DecodedBitmap bitmap)
    {
        if (bitmap.PixelFormat != BitmapPixelFormat.Bgra32)
        {
            throw new NotSupportedException($"Unsupported pixel format: {bitmap.PixelFormat}");
        }

        if (bitmap.WidthPx <= 0 || bitmap.HeightPx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bitmap), "Bitmap dimensions must be positive.");
        }

        PixelWidth = bitmap.WidthPx;
        PixelHeight = bitmap.HeightPx;
        _bgra = bitmap.Data ?? throw new ArgumentNullException(nameof(bitmap));
        if (_bgra.Length != PixelWidth * PixelHeight * 4)
        {
            throw new ArgumentException("Invalid pixel buffer length.", nameof(bitmap));
        }
    }

    /// <summary>
    /// Releases the pixel buffer. Further operations throw <see cref="ObjectDisposedException"/>.
    /// </summary>
    public void Dispose()
    {
        lock (_lock)
        {
            _disposed = true;
            _bgra = Array.Empty<byte>();
            _version = 0;
        }
    }

    /// <summary>
    /// Locks the bitmap for writing.
    /// The returned context will publish changes on Dispose (unless <paramref name="markDirtyOnDispose"/> is false).
    /// </summary>
    /// <param name="markDirtyOnDispose">
    /// When <see langword="true"/>, increments <see cref="Version"/> and raises <see cref="Changed"/> when the context is disposed,
    /// even if no dirty rect was recorded.
    /// </param>
    /// <returns>A write context that must be disposed to release the lock.</returns>
    public WriteContext LockForWrite(bool markDirtyOnDispose = true)
    {
        Monitor.Enter(_lock);
        if (_disposed)
        {
            Monitor.Exit(_lock);
            throw new ObjectDisposedException(nameof(WriteableBitmap));
        }

        return new WriteContext(this, markDirtyOnDispose);
    }

    /// <summary>
    /// Writes pixels into this bitmap.
    /// </summary>
    /// <param name="x">Destination X in pixels.</param>
    /// <param name="y">Destination Y in pixels.</param>
    /// <param name="width">Width in pixels.</param>
    /// <param name="height">Height in pixels.</param>
    /// <param name="srcBgra">Source buffer in BGRA order (straight alpha).</param>
    /// <param name="srcStrideBytes">Source stride in bytes per row.</param>
    public void WritePixels(int x, int y, int width, int height, ReadOnlySpan<byte> srcBgra, int srcStrideBytes)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (x < 0 || y < 0 || x + width > PixelWidth || y + height > PixelHeight)
        {
            throw new ArgumentOutOfRangeException(nameof(x), "Write rect must be within bounds.");
        }

        int dstStride = StrideBytes;
        int rowBytes = checked(width * 4);
        if (srcStrideBytes < rowBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(srcStrideBytes));
        }

        int needed = checked((height - 1) * srcStrideBytes + rowBytes);
        if (srcBgra.Length < needed)
        {
            throw new ArgumentException("Source buffer is too small for the specified rect/stride.", nameof(srcBgra));
        }

        Action? changed;
        lock (_lock)
        {
            ThrowIfDisposed();

            var dst = _bgra.AsSpan();
            int dstRow0 = checked((y * PixelWidth + x) * 4);
            int srcRow = 0;

            for (int row = 0; row < height; row++)
            {
                srcBgra.Slice(srcRow, rowBytes).CopyTo(dst.Slice(dstRow0, rowBytes));
                srcRow += srcStrideBytes;
                dstRow0 += dstStride;
            }

            changed = MarkDirty_NoLock(new PixelRegion(x, y, width, height));
        }

        changed?.Invoke();
    }

    /// <summary>
    /// Clears the bitmap to a constant BGRA color.
    /// </summary>
    /// <param name="b">Blue component.</param>
    /// <param name="g">Green component.</param>
    /// <param name="r">Red component.</param>
    /// <param name="a">Alpha component.</param>
    public void Clear(byte b, byte g, byte r, byte a = 255)
    {
        Action? changed;
        lock (_lock)
        {
            ThrowIfDisposed();

            uint bgra = (uint)(b | ((uint)g << 8) | ((uint)r << 16) | ((uint)a << 24));
            // Use Span.Fill on a uint view of the pixel buffer. The runtime provides a highly optimized
            // (often SIMD-accelerated) memset-like implementation for this pattern.
            MemoryMarshal.Cast<byte, uint>(_bgra.AsSpan()).Fill(bgra);

            changed = MarkDirty_NoLock(new PixelRegion(0, 0, PixelWidth, PixelHeight));
        }

        changed?.Invoke();
    }

    /// <summary>
    /// Clears the bitmap to a constant color.
    /// </summary>
    /// <param name="color">The clear color.</param>
    public void Clear(Color color)
    {
        using var ctx = LockForWrite();
        ctx.Clear(color);
    }

    IImage IImageSource.CreateImage(IGraphicsFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        return factory.CreateImageFromPixelSource(this);
    }

    PixelBufferLock IPixelBufferSource.Lock()
    {
        Monitor.Enter(_lock);
        if (_disposed)
        {
            Monitor.Exit(_lock);
            throw new ObjectDisposedException(nameof(WriteableBitmap));
        }

        int v = _version;

        // Get dirty region and clear after reading
        PixelRegion? dirtyRegion = _dirtyRegion;
        _dirtyRegion = null;

        return new PixelBufferLock(_bgra, PixelWidth, PixelHeight, StrideBytes, PixelFormat, v, dirtyRegion, () => Monitor.Exit(_lock));
    }

    internal Span<byte> GetPixelsMutableNoLock() => _bgra;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Action? MarkDirty_NoLock(PixelRegion? additionalDirtyRegion = null)
    {
        unchecked
        {
            _version++;
        }

        if (additionalDirtyRegion.HasValue)
        {
            AccumulateDirtyRegion_NoLock(additionalDirtyRegion.Value);
        }

        return Changed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AccumulateDirtyRegion_NoLock(PixelRegion region)
    {
        _dirtyRegion = _dirtyRegion.HasValue ? PixelRegion.Union(_dirtyRegion.Value, region) : region;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WriteableBitmap));
        }
    }

    /// <summary>
    /// Represents a locked write scope for a <see cref="WriteableBitmap"/>.
    /// Disposing the scope releases the lock and optionally raises <see cref="Changed"/>.
    /// </summary>
    public ref struct WriteContext
    {
        private readonly WriteableBitmap _owner;
        private readonly bool _markDirtyOnDispose;
        private bool _disposed;
        private bool _hasDirtyRect;
        private PixelRect _dirtyRect;

        /// <summary>
        /// Gets the bitmap width in pixels.
        /// </summary>
        public int PixelWidth => _owner.PixelWidth;

        /// <summary>
        /// Gets the bitmap height in pixels.
        /// </summary>
        public int PixelHeight => _owner.PixelHeight;

        /// <summary>
        /// Gets the stride (bytes per row).
        /// </summary>
        public int StrideBytes => _owner.StrideBytes;

        /// <summary>
        /// Gets the bitmap width in pixels.
        /// </summary>
        public int Width => _owner.PixelWidth;

        /// <summary>
        /// Gets the bitmap height in pixels.
        /// </summary>
        public int Height => _owner.PixelHeight;

        /// <summary>
        /// Gets the stride (bytes per row).
        /// </summary>
        public int Stride => _owner.StrideBytes;

        /// <summary>
        /// Gets a mutable view of the pixel buffer in BGRA32 byte order.
        /// </summary>
        public Span<byte> PixelsBgra32 => _owner.GetPixelsMutableNoLock();

        /// <summary>
        /// Gets a mutable view of the pixel buffer as packed BGRA32 <see cref="uint"/> values.
        /// </summary>
        public Span<uint> PixelsUInt32 => MemoryMarshal.Cast<byte, uint>(_owner.GetPixelsMutableNoLock());

        internal WriteContext(WriteableBitmap owner, bool markDirtyOnDispose)
        {
            _owner = owner;
            _markDirtyOnDispose = markDirtyOnDispose;
            _disposed = false;
            _hasDirtyRect = false;
            _dirtyRect = default;
        }

        /// <summary>
        /// Marks a dirty rectangle in pixel coordinates to minimize texture updates.
        /// </summary>
        /// <param name="x">X in pixels.</param>
        /// <param name="y">Y in pixels.</param>
        /// <param name="width">Width in pixels.</param>
        /// <param name="height">Height in pixels.</param>
        public void AddDirtyRect(int x, int y, int width, int height)
        {
            if (width <= 0 || height <= 0)
            {
                return;
            }

            int x1 = Math.Max(0, x);
            int y1 = Math.Max(0, y);
            int x2 = Math.Min(PixelWidth, x + width);
            int y2 = Math.Min(PixelHeight, y + height);
            if (x1 >= x2 || y1 >= y2)
            {
                return;
            }

            var rect = new PixelRect(x1, y1, x2 - x1, y2 - y1);
            _dirtyRect = _hasDirtyRect ? PixelRect.Union(_dirtyRect, rect) : rect;
            _hasDirtyRect = true;
        }

        /// <summary>
        /// Clears the bitmap to a constant color and marks it dirty.
        /// </summary>
        /// <param name="color">The clear color.</param>
        public void Clear(Color color)
        {
            uint bgra = (uint)(color.B | (color.G << 8) | (color.R << 16) | (color.A << 24));
            PixelsUInt32.Fill(bgra);
            AddDirtyRect(0, 0, PixelWidth, PixelHeight);
        }

        /// <summary>
        /// Commits changes (if configured) and releases the write lock.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            Action? changed = null;
            try
            {
                if (_markDirtyOnDispose || _hasDirtyRect)
                {
                    // Convert internal PixelRect to PixelRegion and pass to owner
                    PixelRegion? region = _hasDirtyRect
                        ? new PixelRegion(_dirtyRect.X, _dirtyRect.Y, _dirtyRect.Width, _dirtyRect.Height)
                        : null;
                    changed = _owner.MarkDirty_NoLock(region);
                }
            }
            finally
            {
                Monitor.Exit(_owner._lock);
            }

            changed?.Invoke();
        }
    }

    private readonly record struct PixelRect(int X, int Y, int Width, int Height)
    {
        public static PixelRect Union(PixelRect a, PixelRect b)
        {
            int x1 = Math.Min(a.X, b.X);
            int y1 = Math.Min(a.Y, b.Y);
            int x2 = Math.Max(a.X + a.Width, b.X + b.Width);
            int y2 = Math.Max(a.Y + a.Height, b.Y + b.Height);
            return new PixelRect(x1, y1, x2 - x1, y2 - y1);
        }
    }
}
