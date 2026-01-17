namespace Aprillz.MewUI.Resources;

public interface IPixelBufferSource
{
    int PixelWidth { get; }
    int PixelHeight { get; }
    int StrideBytes { get; }
    BitmapPixelFormat PixelFormat { get; }

    /// <summary>
    /// Monotonically increasing version. Backends can use this to detect changes.
    /// </summary>
    int Version { get; }

    PixelBufferLock Lock();
}

public sealed class PixelBufferLock : IDisposable
{
    private readonly Action? _release;
    private bool _disposed;

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public int StrideBytes { get; }
    public BitmapPixelFormat PixelFormat { get; }
    public int Version { get; }

    public byte[] Buffer { get; }

    internal PixelBufferLock(
        byte[] buffer,
        int pixelWidth,
        int pixelHeight,
        int strideBytes,
        BitmapPixelFormat pixelFormat,
        int version,
        Action? release)
    {
        Buffer = buffer;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        StrideBytes = strideBytes;
        PixelFormat = pixelFormat;
        Version = version;
        _release = release;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _release?.Invoke();
    }
}

