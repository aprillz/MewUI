using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering;

public sealed class BitmapRenderTargetSurfaceAdapter : ICpuPixelSurface, IDeferredCpuReadableSurface
{
    private readonly IBitmapRenderTarget _target;
    private readonly bool _ownsTarget;
    private bool _disposed;

    public BitmapRenderTargetSurfaceAdapter(
        IBitmapRenderTarget target,
        RenderSurfaceDescriptor? descriptor = null,
        bool ownsTarget = false)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _ownsTarget = ownsTarget;

        var fallback = RenderSurfaceDescriptor.CpuBitmap(
            target.PixelWidth,
            target.PixelHeight,
            target.DpiScale,
            target.IsPremultiplied);

        Descriptor = descriptor ?? fallback;
    }

    public RenderSurfaceDescriptor Descriptor { get; }

    public int PixelWidth => _target.PixelWidth;

    public int PixelHeight => _target.PixelHeight;

    public double DpiScale => _target.DpiScale;

    public RenderPixelFormat Format => _target.IsPremultiplied
        ? RenderPixelFormat.Bgra8888Premultiplied
        : RenderPixelFormat.Bgra8888;

    public SurfaceUsage Usage => Descriptor.Usage;

    public SurfaceCapabilities Capabilities
    {
        get
        {
            var capabilities = Descriptor.RequiredCapabilities |
                SurfaceCapabilities.Renderable |
                SurfaceCapabilities.CpuReadable |
                SurfaceCapabilities.CpuWritable |
                SurfaceCapabilities.Alpha;

            if (_target.IsPremultiplied)
            {
                capabilities |= SurfaceCapabilities.Premultiplied;
            }

            if (_target.LockMode == LockMode.Readback)
            {
                capabilities |= SurfaceCapabilities.DeferredReadback;
            }

            if (_target is IGpuTextureSource)
            {
                capabilities |= SurfaceCapabilities.GpuSampleable;
            }

            return capabilities;
        }
    }

    public ulong Version => (ulong)Math.Max(0, _target.Version);

    public bool IsDisposed => _disposed;

    public int StrideBytes => _target.StrideBytes;

    public bool HasPendingReadback => _target.LockMode == LockMode.Readback;

    public ReadOnlySpan<byte> GetReadOnlyPixelSpan() => _target.GetPixelSpan();

    public Span<byte> GetWritablePixelSpan() => _target.GetPixelSpan();

    public byte[] CopyPixels() => _target.CopyPixels();

    public void IncrementVersion() => _target.IncrementVersion();

    public IRenderOperation RequestReadback()
    {
        if (_target.LockMode == LockMode.Readback)
        {
            _ = _target.CopyPixels();
        }

        return RenderOperation.Completed;
    }

    public bool TryFlushReadback()
    {
        if (_target.LockMode == LockMode.Readback)
        {
            _ = _target.CopyPixels();
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_ownsTarget)
        {
            _target.Dispose();
        }
    }
}
