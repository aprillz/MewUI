using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// CPU-only bitmap render target for the Metal backend.
/// Backs <see cref="IBitmapRenderTarget"/> with a BGRA32 byte buffer so
/// <see cref="Aprillz.MewUI.Controls.WriteableBitmapControl"/> works when
/// <c>UseBitmapGraphicsContext</c> is <see langword="false"/> (pixel-only path).
/// Creating an <see cref="IGraphicsContext"/> targeting this RT is not supported.
/// </summary>
internal sealed class MewVGMetalBitmapRenderTarget : IBitmapRenderTarget
{
    private readonly byte[] _pixels;
    private readonly object _gate = new();
    private int _version;
    private bool _disposed;

    private byte[]? _lockBuffer;
    private Action? _releaseAction;

    public MewVGMetalBitmapRenderTarget(int pixelWidth, int pixelHeight, double dpiScale)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelHeight, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dpiScale, 0);

        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        DpiScale = dpiScale;
        _pixels = new byte[pixelWidth * pixelHeight * 4];
    }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public double DpiScale { get; }

    public BitmapPixelFormat PixelFormat => BitmapPixelFormat.Bgra32;

    public int StrideBytes => PixelWidth * 4;

    public int Version => Volatile.Read(ref _version);

    public byte[] CopyPixels()
    {
        if (_disposed)
        {
            return Array.Empty<byte>();
        }

        var copy = new byte[_pixels.Length];
        Buffer.BlockCopy(_pixels, 0, copy, 0, _pixels.Length);
        return copy;
    }

    public Span<byte> GetPixelSpan()
    {
        return _disposed ? Span<byte>.Empty : _pixels.AsSpan();
    }

    public void Clear(Color color)
    {
        if (_disposed)
        {
            return;
        }

        byte b = color.B, g = color.G, r = color.R, a = color.A;
        for (int i = 0; i < _pixels.Length; i += 4)
        {
            _pixels[i + 0] = b;
            _pixels[i + 1] = g;
            _pixels[i + 2] = r;
            _pixels[i + 3] = a;
        }

        IncrementVersion();
    }

    public PixelBufferLock Lock()
    {
        Monitor.Enter(_gate);
        if (_disposed)
        {
            Monitor.Exit(_gate);
            throw new ObjectDisposedException(nameof(MewVGMetalBitmapRenderTarget));
        }

        int size = _pixels.Length;
        if (_lockBuffer == null || _lockBuffer.Length != size)
        {
            _lockBuffer = new byte[size];
        }

        Buffer.BlockCopy(_pixels, 0, _lockBuffer, 0, size);

        _releaseAction ??= () => Monitor.Exit(_gate);

        return new PixelBufferLock(
            _lockBuffer,
            PixelWidth,
            PixelHeight,
            StrideBytes,
            PixelFormat,
            _version,
            dirtyRegion: null,
            release: _releaseAction);
    }

    public void IncrementVersion()
    {
        Interlocked.Increment(ref _version);
    }

    public void Dispose()
    {
        _disposed = true;
    }
}
