namespace Aprillz.MewUI.Resources;

/// <summary>
/// Exposes a lockable CPU-side pixel buffer that backends can upload to GPU resources.
/// </summary>
public interface IPixelBufferSource : IRasterSource
{
    /// <summary>
    /// Gets the stride in bytes per row.
    /// </summary>
    int StrideBytes { get; }

    /// <summary>
    /// Gets whether the RGB channels are already multiplied by alpha; the default is false.
    /// </summary>
    /// <remarks>
    /// Consumers must skip their own premultiply step when true; otherwise the data is premultiplied a second
    /// time, darkening every semi-transparent pixel (a black halo on alpha-blended edges). The default
    /// preserves straight-alpha semantics for sources that have not opted in (PNG decode, raw byte arrays,
    /// <see cref="WriteableBitmap"/>); render-target sources that produce premultiplied output override to true.
    /// </remarks>
    bool IsPremultiplied => false;

    /// <summary>
    /// Gets whether the buffer carries a meaningful alpha channel; false means every pixel is fully opaque by
    /// construction (JPEG decode, opaque PNG, 24-bit BMP), and the default is true.
    /// </summary>
    /// <remarks>
    /// Lets consumers skip per-pixel alpha scans (the conservative "is every byte 0xFF?" check that runs
    /// before premultiply) and pick the opaque blending path, which avoids per-fragment blend math. The
    /// default preserves alpha-aware blending for any source that has not explicitly opted out.
    /// </remarks>
    bool HasAlpha => true;

    /// <summary>
    /// Gets the mechanism by which <see cref="Lock"/> exposes the pixel buffer; the default is
    /// <see cref="LockMode.Direct"/>.
    /// </summary>
    /// <remarks>
    /// GPU-resident sources override to <see cref="LockMode.Readback"/> so deferred consumers can detect the
    /// sync barrier before invoking <see cref="Lock"/>.
    /// </remarks>
    LockMode LockMode => LockMode.Direct;

    /// <summary>
    /// Gets whether the producer updates the buffer frequently (for example every video frame), letting
    /// consumers route through asynchronous upload paths; the default is false.
    /// </summary>
    /// <remarks>
    /// Static sources (a file-decoded PNG, an opaque <see cref="WriteableBitmap"/> drawn once) leave this false
    /// so the synchronous upload path keeps mipmap generation and other quality settings.
    /// </remarks>
    bool IsStreaming => false;

    /// <summary>
    /// Locks and returns the current pixel buffer snapshot.
    /// </summary>
    /// <remarks>
    /// Disposing the returned lock releases any underlying synchronization.
    /// </remarks>
    PixelBufferLock Lock();
}

/// <summary>
/// Represents a rectangular region in pixel coordinates.
/// </summary>
/// <param name="X">Left edge in pixels.</param>
/// <param name="Y">Top edge in pixels.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
public readonly record struct PixelRegion(int X, int Y, int Width, int Height)
{
    /// <summary>
    /// Returns the smallest region that contains both input regions.
    /// </summary>
    public static PixelRegion Union(PixelRegion a, PixelRegion b)
    {
        int x1 = Math.Min(a.X, b.X);
        int y1 = Math.Min(a.Y, b.Y);
        int x2 = Math.Max(a.X + a.Width, b.X + b.Width);
        int y2 = Math.Max(a.Y + a.Height, b.Y + b.Height);
        return new PixelRegion(x1, y1, x2 - x1, y2 - y1);
    }

    /// <summary>
    /// Gets a value indicating whether the region is empty.
    /// </summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>
/// Represents a locked pixel buffer snapshot and optional dirty region metadata.
/// </summary>
public sealed class PixelBufferLock : IDisposable
{
    private readonly Action? _release;
    private bool _disposed;

    /// <summary>
    /// Gets the buffer width in pixels.
    /// </summary>
    public int PixelWidth { get; }

    /// <summary>
    /// Gets the buffer height in pixels.
    /// </summary>
    public int PixelHeight { get; }

    /// <summary>
    /// Gets the stride in bytes per row.
    /// </summary>
    public int StrideBytes { get; }

    /// <summary>
    /// Gets the source version captured at lock time.
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// Gets the backing byte buffer containing pixel data.
    /// </summary>
    public byte[] Buffer { get; }

    /// <summary>
    /// Gets the dirty region since the last lock, or null when the entire buffer should be treated as dirty.
    /// </summary>
    /// <remarks>
    /// Backends can use this for partial updates instead of uploading the entire buffer again.
    /// </remarks>
    public PixelRegion? DirtyRegion { get; }

    /// <summary>
    /// Creates a lock over <paramref name="buffer"/> that invokes <paramref name="release"/> once on
    /// <see cref="Dispose"/>.
    /// </summary>
    public PixelBufferLock(
        byte[] buffer,
        int pixelWidth,
        int pixelHeight,
        int strideBytes,
        int version,
        PixelRegion? dirtyRegion,
        Action? release)
    {
        Buffer = buffer;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        StrideBytes = strideBytes;
        Version = version;
        DirtyRegion = dirtyRegion;
        _release = release;
    }

    /// <summary>
    /// Releases the lock.
    /// </summary>
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
