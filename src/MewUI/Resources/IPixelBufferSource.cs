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

/// <summary>
/// Represents a rectangular region in pixel coordinates.
/// </summary>
public readonly record struct PixelRegion(int X, int Y, int Width, int Height)
{
    public static PixelRegion Union(PixelRegion a, PixelRegion b)
    {
        int x1 = Math.Min(a.X, b.X);
        int y1 = Math.Min(a.Y, b.Y);
        int x2 = Math.Max(a.X + a.Width, b.X + b.Width);
        int y2 = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new PixelRegion(x1, y1, x2 - x1, y2 - y1);
    }

    public bool IsEmpty => Width <= 0 || Height <= 0;
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

    /// <summary>
    /// The dirty region since the last lock, or null if the entire buffer should be considered dirty.
    /// Backends can use this for partial updates instead of re-uploading the entire buffer.
    /// </summary>
    public PixelRegion? DirtyRegion { get; }

    internal PixelBufferLock(
        byte[] buffer,
        int pixelWidth,
        int pixelHeight,
        int strideBytes,
        BitmapPixelFormat pixelFormat,
        int version,
        PixelRegion? dirtyRegion,
        Action? release)
    {
        Buffer = buffer;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        StrideBytes = strideBytes;
        PixelFormat = pixelFormat;
        Version = version;
        DirtyRegion = dirtyRegion;
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

