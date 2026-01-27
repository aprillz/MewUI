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
    private readonly object _gate = new();
    private byte[] _bgra;
    private int _version;
    private bool _disposed;

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public int StrideBytes => PixelWidth * 4;
    public BitmapPixelFormat PixelFormat => BitmapPixelFormat.Bgra32;

    /// <summary>
    /// Monotonically increasing version. Incremented after any committed write.
    /// </summary>
    public int Version => Volatile.Read(ref _version);

    public event Action? Changed;

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

    public void Dispose()
    {
        lock (_gate)
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
    public WriteContext LockForWrite(bool markDirtyOnDispose = true)
    {
        Monitor.Enter(_gate);
        if (_disposed)
        {
            Monitor.Exit(_gate);
            throw new ObjectDisposedException(nameof(WriteableBitmap));
        }

        return new WriteContext(this, markDirtyOnDispose);
    }

    /// <summary>
    /// Writes pixels into this bitmap.
    /// </summary>
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
        lock (_gate)
        {
            ThrowIfDisposed();

            var dst = _bgra.AsSpan();
            int dstRow0 = checked((y * PixelWidth + x) * 4);
            int srcRow = 0;

            for (int r = 0; r < height; r++)
            {
                srcBgra.Slice(srcRow, rowBytes).CopyTo(dst.Slice(dstRow0, rowBytes));
                srcRow += srcStrideBytes;
                dstRow0 += dstStride;
            }

            changed = MarkDirty_NoLock();
        }

        changed?.Invoke();
    }

    public void Clear(byte b, byte g, byte r, byte a = 255)
    {
        Action? changed;
        lock (_gate)
        {
            ThrowIfDisposed();

            Span<byte> dst = _bgra.AsSpan();
            for (int i = 0; i < dst.Length; i += 4)
            {
                dst[i + 0] = b;
                dst[i + 1] = g;
                dst[i + 2] = r;
                dst[i + 3] = a;
            }

            changed = MarkDirty_NoLock();
        }

        changed?.Invoke();
    }

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
        Monitor.Enter(_gate);
        if (_disposed)
        {
            Monitor.Exit(_gate);
            throw new ObjectDisposedException(nameof(WriteableBitmap));
        }

        int v = _version;
        return new PixelBufferLock(_bgra, PixelWidth, PixelHeight, StrideBytes, PixelFormat, v, () => Monitor.Exit(_gate));
    }

    internal Span<byte> GetPixelsMutableNoLock() => _bgra;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Action? MarkDirty_NoLock()
    {
        unchecked
        {
            _version++;
        }

        return Changed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WriteableBitmap));
        }
    }

    public ref struct WriteContext
    {
        private readonly WriteableBitmap _owner;
        private readonly bool _markDirtyOnDispose;
        private bool _disposed;
        private bool _hasDirtyRect;
        private PixelRect _dirtyRect;

        public int PixelWidth => _owner.PixelWidth;
        public int PixelHeight => _owner.PixelHeight;
        public int StrideBytes => _owner.StrideBytes;
        public int Width => _owner.PixelWidth;
        public int Height => _owner.PixelHeight;
        public int Stride => _owner.StrideBytes;
        public Span<byte> PixelsBgra32 => _owner.GetPixelsMutableNoLock();
        public Span<uint> PixelsUInt32 => MemoryMarshal.Cast<byte, uint>(_owner.GetPixelsMutableNoLock());

        internal WriteContext(WriteableBitmap owner, bool markDirtyOnDispose)
        {
            _owner = owner;
            _markDirtyOnDispose = markDirtyOnDispose;
            _disposed = false;
            _hasDirtyRect = false;
            _dirtyRect = default;
        }

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

        public void Clear(Color color)
        {
            uint bgra = (uint)(color.B | (color.G << 8) | (color.R << 16) | (color.A << 24));
            PixelsUInt32.Fill(bgra);
            AddDirtyRect(0, 0, PixelWidth, PixelHeight);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void SetPixel(int x, int y, Color color)
        {
            if ((uint)x >= (uint)PixelWidth || (uint)y >= (uint)PixelHeight)
            {
                return;
            }

            int offset = y * StrideBytes + x * 4;
            var pixels = PixelsBgra32;
            pixels[offset + 0] = color.B;
            pixels[offset + 1] = color.G;
            pixels[offset + 2] = color.R;
            pixels[offset + 3] = color.A;

            AddDirtyRect(x, y, 1, 1);
        }

        public void FillRect(int x, int y, int width, int height, Color color)
        {
            int x1 = Math.Max(0, x);
            int y1 = Math.Max(0, y);
            int x2 = Math.Min(PixelWidth, x + width);
            int y2 = Math.Min(PixelHeight, y + height);

            if (x1 >= x2 || y1 >= y2)
            {
                return;
            }

            uint bgra = (uint)(color.B | (color.G << 8) | (color.R << 16) | (color.A << 24));
            var pixels32 = PixelsUInt32;

            for (int py = y1; py < y2; py++)
            {
                int rowStart = py * PixelWidth + x1;
                pixels32.Slice(rowStart, x2 - x1).Fill(bgra);
            }

            AddDirtyRect(x1, y1, x2 - x1, y2 - y1);
        }

        public void DrawHLine(int x1, int x2, int y, Color color)
        {
            if ((uint)y >= (uint)PixelHeight)
            {
                return;
            }

            if (x1 > x2)
            {
                (x1, x2) = (x2, x1);
            }

            x1 = Math.Max(0, x1);
            x2 = Math.Min(PixelWidth - 1, x2);
            if (x1 > x2)
            {
                return;
            }

            uint bgra = (uint)(color.B | (color.G << 8) | (color.R << 16) | (color.A << 24));
            int rowStart = y * PixelWidth + x1;
            PixelsUInt32.Slice(rowStart, x2 - x1 + 1).Fill(bgra);
            AddDirtyRect(x1, y, x2 - x1 + 1, 1);
        }

        public void DrawVLine(int x, int y1, int y2, Color color)
        {
            if ((uint)x >= (uint)PixelWidth)
            {
                return;
            }

            if (y1 > y2)
            {
                (y1, y2) = (y2, y1);
            }

            y1 = Math.Max(0, y1);
            y2 = Math.Min(PixelHeight - 1, y2);
            if (y1 > y2)
            {
                return;
            }

            uint bgra = (uint)(color.B | (color.G << 8) | (color.R << 16) | (color.A << 24));
            var pixels = PixelsUInt32;
            for (int y = y1; y <= y2; y++)
            {
                pixels[y * PixelWidth + x] = bgra;
            }

            AddDirtyRect(x, y1, 1, y2 - y1 + 1);
        }

        public void DrawLine(int x0, int y0, int x1, int y1, Color color)
        {
            // Bresenham
            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                SetPixel(x0, y0, color);
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int e2 = err << 1;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }

                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        public void FillEllipse(int cx, int cy, int rx, int ry, Color color)
        {
            if (rx < 0) rx = -rx;
            if (ry < 0) ry = -ry;
            if (rx == 0 && ry == 0)
            {
                SetPixel(cx, cy, color);
                return;
            }

            // Simple scanline fill using ellipse equation
            int x1 = Math.Max(0, cx - rx);
            int x2 = Math.Min(PixelWidth - 1, cx + rx);
            int y1 = Math.Max(0, cy - ry);
            int y2 = Math.Min(PixelHeight - 1, cy + ry);

            double rxsq = rx * (double)rx;
            double rysq = ry * (double)ry;
            if (rxsq <= 0 || rysq <= 0)
            {
                return;
            }

            for (int y = y1; y <= y2; y++)
            {
                double dy = y - cy;
                double t = 1.0 - (dy * dy) / rysq;
                if (t <= 0)
                {
                    continue;
                }

                int span = (int)Math.Floor(Math.Sqrt(t * rxsq));
                int sx1 = Math.Max(0, cx - span);
                int sx2 = Math.Min(PixelWidth - 1, cx + span);
                DrawHLine(sx1, sx2, y, color);
            }
        }

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
                    changed = _owner.MarkDirty_NoLock();
                }
            }
            finally
            {
                Monitor.Exit(_owner._gate);
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
