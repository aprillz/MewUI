namespace Aprillz.MewUI.Resources;

/// <summary>
/// Exposes a lockable CPU-side pixel buffer that backends can upload to GPU resources.
/// </summary>
public interface IPixelBufferSource
{
    /// <summary>
    /// Gets the buffer width in pixels.
    /// </summary>
    int PixelWidth { get; }

    /// <summary>
    /// Gets the buffer height in pixels.
    /// </summary>
    int PixelHeight { get; }

    /// <summary>
    /// Gets the stride in bytes per row.
    /// </summary>
    int StrideBytes { get; }

    /// <summary>
    /// Gets the pixel format.
    /// </summary>
    BitmapPixelFormat PixelFormat { get; }

    /// <summary>
    /// Whether the pixel buffer's RGB channels are already multiplied by alpha.
    /// Backend image uploads must skip their own premultiply step when true; otherwise
    /// the data gets premultiplied a second time, darkening every semi-transparent pixel
    /// (visible as a black halo on alpha-blended edges).
    /// </summary>
    /// <remarks>
    /// Default false to preserve straight-alpha semantics for sources that haven't opted
    /// in (PNG decode, raw byte arrays, <see cref="WriteableBitmap"/>). Render targets
    /// that produce premultiplied output (OpenGL/MewVG FBO, GDI DIB written via AlphaBlend)
    /// override to true.
    /// </remarks>
    bool IsPremultiplied => false;

    /// <summary>
    /// Whether the buffer carries a meaningful alpha channel. False means every pixel is
    /// fully opaque by construction (JPEG decode, opaque PNG, 24-bit BMP, etc.).
    /// </summary>
    /// <remarks>
    /// Backends use this to skip per-pixel alpha scans (the conservative "is every byte
    /// 0xFF?" check that runs before premultiply) and to select <c>ALPHA_MODE.IGNORE</c>
    /// over <c>PREMULTIPLIED</c> on the GPU image, which avoids the per-fragment blend math.
    /// Default <c>true</c> is the safe choice — it preserves alpha-aware blending for any
    /// source that hasn't explicitly opted out.
    /// </remarks>
    bool HasAlpha => true;

    /// <summary>
    /// Mechanism by which <see cref="Lock"/> exposes the pixel buffer. Default
    /// <see cref="LockMode.Direct"/> for CPU-resident sources; GPU-resident sources
    /// (Direct2D GPU bitmap, OpenGL FBO, Metal texture) override to
    /// <see cref="LockMode.Readback"/> so deferred consumers can detect the sync barrier
    /// before invoking <c>Lock</c>.
    /// </summary>
    LockMode LockMode => LockMode.Direct;

    /// <summary>
    /// Monotonically increasing version. Backends can use this to detect changes.
    /// </summary>
    int Version { get; }

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
    /// Gets the pixel format.
    /// </summary>
    public BitmapPixelFormat PixelFormat { get; }

    /// <summary>
    /// Gets the source version captured at lock time.
    /// </summary>
    public int Version { get; }

    /// <summary>
    /// Gets the backing byte buffer containing pixel data.
    /// </summary>
    public byte[] Buffer { get; }

    /// <summary>
    /// The dirty region since the last lock, or null if the entire buffer should be considered dirty.
    /// Backends can use this for partial updates instead of re-uploading the entire buffer.
    /// </summary>
    public PixelRegion? DirtyRegion { get; }

    public PixelBufferLock(
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
