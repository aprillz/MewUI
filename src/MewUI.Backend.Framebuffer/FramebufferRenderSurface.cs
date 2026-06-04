using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Framebuffer;

public sealed class FramebufferRenderSurface : ICpuPixelSurface, IPixelBufferSource
{
    private readonly byte[] _pixels;
    private ulong _version;
    private bool _disposed;

    public FramebufferRenderSurface(int pixelWidth, int pixelHeight, double dpiScale, RenderPixelFormat format, SurfaceCapabilities requiredCapabilities)
    {
        if (pixelWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelWidth));
        }

        if (pixelHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pixelHeight));
        }

        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        DpiScale = dpiScale <= 0 ? 1.0 : dpiScale;
        Format = format is RenderPixelFormat.Bgra8888 or RenderPixelFormat.Bgra8888Premultiplied
            ? format
            : RenderPixelFormat.Bgra8888Premultiplied;
        StrideBytes = checked(pixelWidth * 4);
        _pixels = new byte[checked(StrideBytes * pixelHeight)];

        var capabilities = SurfaceCapabilities.Renderable |
            SurfaceCapabilities.CpuReadable |
            SurfaceCapabilities.CpuWritable |
            SurfaceCapabilities.CacheableImageSource |
            SurfaceCapabilities.Alpha;

        if (Format == RenderPixelFormat.Bgra8888Premultiplied ||
            requiredCapabilities.HasFlag(SurfaceCapabilities.Premultiplied))
        {
            capabilities |= SurfaceCapabilities.Premultiplied;
            Format = RenderPixelFormat.Bgra8888Premultiplied;
        }

        Capabilities = capabilities;
    }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    public double DpiScale { get; }

    public RenderPixelFormat Format { get; private set; }

    public SurfaceUsage Usage => RenderSurfaceDefaults.PixelSurfaceUsage;

    public SurfaceCapabilities Capabilities { get; }

    public ulong Version => _version;

    int IRasterSource.Version => checked((int)Math.Min(int.MaxValue, _version));

    public bool IsDisposed => _disposed;

    public int StrideBytes { get; }

    bool IPixelBufferSource.IsPremultiplied => Capabilities.HasFlag(SurfaceCapabilities.Premultiplied);

    bool IPixelBufferSource.HasAlpha => Capabilities.HasFlag(SurfaceCapabilities.Alpha);

    public ReadOnlySpan<byte> GetReadOnlyPixelSpan()
    {
        ThrowIfDisposed();
        return _pixels;
    }

    public Span<byte> GetWritablePixelSpan()
    {
        ThrowIfDisposed();
        return _pixels;
    }

    public byte[] CopyPixels()
    {
        ThrowIfDisposed();
        return (byte[])_pixels.Clone();
    }

    public void IncrementVersion() => _version++;

    public PixelBufferLock Lock()
    {
        ThrowIfDisposed();
        return new PixelBufferLock(_pixels, PixelWidth, PixelHeight, StrideBytes, checked((int)Math.Min(int.MaxValue, _version)), null, null);
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FramebufferRenderSurface));
        }
    }
}
