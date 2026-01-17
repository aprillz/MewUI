using System.Runtime.CompilerServices;

using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI;

/// <summary>
/// Mutable bitmap source (BGRA32, straight alpha). Similar to WPF's WritableBitmap, but simplified.
/// Backends are expected to upload pixels as needed when <see cref="Version"/> changes.
/// </summary>
public sealed class WritableBitmap : IImageSource, INotifyImageChanged, IPixelBufferSource, IDisposable
{
    private readonly object _lock = new();
    private byte[] _bgra;
    private int _version;
    private bool _disposed;

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public int StrideBytes => PixelWidth * 4;
    public BitmapPixelFormat PixelFormat => BitmapPixelFormat.Bgra32;

    /// <summary>
    /// Monotonically increasing version. Incremented after any write.
    /// </summary>
    public int Version => Volatile.Read(ref _version);

    public event Action? Changed;

    public WritableBitmap(int widthPx, int heightPx, bool clear = true)
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

    public WritableBitmap(DecodedBitmap bitmap)
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
        lock (_lock)
        {
            _disposed = true;
            _bgra = Array.Empty<byte>();
            _version = 0;
        }
    }

    public WritableBitmapLock Lock()
    {
        Monitor.Enter(_lock);
        if (_disposed)
        {
            Monitor.Exit(_lock);
            throw new ObjectDisposedException(nameof(WritableBitmap));
        }

        return new WritableBitmapLock(this, markDirtyOnDispose: true);
    }

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
        lock (_lock)
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
            throw new ObjectDisposedException(nameof(WritableBitmap));
        }

        int v = _version;
        return new PixelBufferLock(_bgra, PixelWidth, PixelHeight, StrideBytes, PixelFormat, v, release: () => Monitor.Exit(_lock));
    }

    internal Span<byte> GetPixelsMutableNoLock() => _bgra;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Action? MarkDirty_NoLock()
    {
        unchecked { _version++; }
        return Changed;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WritableBitmap));
        }
    }

    public readonly ref struct WritableBitmapLock
    {
        private readonly WritableBitmap _owner;
        private readonly bool _markDirtyOnDispose;

        public int PixelWidth => _owner.PixelWidth;
        public int PixelHeight => _owner.PixelHeight;
        public int StrideBytes => _owner.StrideBytes;
        public Span<byte> PixelsBgra32 => _owner.GetPixelsMutableNoLock();

        internal WritableBitmapLock(WritableBitmap owner, bool markDirtyOnDispose)
        {
            _owner = owner;
            _markDirtyOnDispose = markDirtyOnDispose;
        }

        public void Dispose()
        {
            Action? changed = null;
            if (_markDirtyOnDispose)
            {
                changed = _owner.MarkDirty_NoLock();
            }

            Monitor.Exit(_owner._lock);
            changed?.Invoke();
        }
    }
}
