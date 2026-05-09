using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering;

public sealed class GraphicsFactoryRenderDeviceAdapter : IRenderDevice
{
    private readonly IGraphicsFactory _factory;
    private readonly RenderResourceCache _resourceCache = new();

    public GraphicsFactoryRenderDeviceAdapter(IGraphicsFactory factory)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
    }

    public GraphicsBackend Backend => _factory.Backend;

    public IRenderResourceCache? ResourceCache => _resourceCache;

    public IRenderEffectDevice? Effects => null;

    public IRenderSurface CreateSurface(RenderSurfaceDescriptor descriptor)
    {
        var target = RequiresCpuBitmap(descriptor)
            ? _factory.CreateBitmapRenderTarget(
                descriptor.PixelWidth,
                descriptor.PixelHeight,
                descriptor.DpiScale,
                descriptor.RequiredCapabilities.HasFlag(SurfaceCapabilities.Alpha))
            : _factory.CreateOffscreenRenderTarget(
                descriptor.PixelWidth,
                descriptor.PixelHeight,
                descriptor.DpiScale,
                descriptor.RequiredCapabilities.HasFlag(SurfaceCapabilities.Alpha));

        return new BitmapRenderTargetSurfaceAdapter(target, descriptor, ownsTarget: true);
    }

    public IGraphicsContext CreateContext(IRenderSurface surface)
    {
        if (surface is BitmapRenderTargetSurfaceAdapter bitmapSurface)
        {
            return _factory.CreateContext(bitmapSurface.Target);
        }

        throw new NotSupportedException(
            $"{GetType().Name} can only create contexts for {nameof(BitmapRenderTargetSurfaceAdapter)} instances.");
    }

    public IImage CreateImageView(IRenderSurface surface)
    {
        if (surface is BitmapRenderTargetSurfaceAdapter bitmapSurface)
        {
            return CreateImageView(bitmapSurface.Target);
        }

        throw new NotSupportedException(
            $"{GetType().Name} can only create image views for pixel-backed surfaces.");
    }

    public IImage CreateImageView(IPixelBufferSource source)
    {
        return _factory.CreateImageFromPixelSource(source ?? throw new ArgumentNullException(nameof(source)));
    }

    public bool TryReadPixels(IRenderSurface source, Span<byte> destination, int destinationStrideBytes)
    {
        if (source is not ICpuPixelSurface cpuSurface)
        {
            return false;
        }

        int rowBytes = checked(cpuSurface.PixelWidth * 4);
        if (destinationStrideBytes < rowBytes)
        {
            return false;
        }

        int requiredBytes = checked(destinationStrideBytes * Math.Max(0, cpuSurface.PixelHeight - 1) + rowBytes);
        if (destination.Length < requiredBytes)
        {
            return false;
        }

        ReadOnlySpan<byte> sourcePixels = cpuSurface.GetReadOnlyPixelSpan();
        if (sourcePixels.Length < checked(cpuSurface.StrideBytes * Math.Max(0, cpuSurface.PixelHeight - 1) + rowBytes))
        {
            return false;
        }

        for (int y = 0; y < cpuSurface.PixelHeight; y++)
        {
            var sourceRow = sourcePixels.Slice(y * cpuSurface.StrideBytes, rowBytes);
            var destRow = destination.Slice(y * destinationStrideBytes, rowBytes);
            sourceRow.CopyTo(destRow);
        }

        return true;
    }

    public IRenderOperation RequestReadback(IRenderSurface source)
    {
        return source is IDeferredCpuReadableSurface deferred
            ? deferred.RequestReadback()
            : RenderOperation.Completed;
    }

    public IRenderOperation FlushAsyncWork() => RenderOperation.Completed;

    public void Dispose()
    {
        _resourceCache.Dispose();
    }

    private static bool RequiresCpuBitmap(RenderSurfaceDescriptor descriptor)
    {
        var caps = descriptor.RequiredCapabilities;
        return caps.HasFlag(SurfaceCapabilities.CpuWritable)
            || (caps.HasFlag(SurfaceCapabilities.CpuReadable)
                && !caps.HasFlag(SurfaceCapabilities.GpuSampleable));
    }
}
