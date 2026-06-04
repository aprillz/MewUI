using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Rendering.Filters;

namespace Aprillz.MewUI.Rendering.Framebuffer;

public sealed class FramebufferGraphicsFactory : IGraphicsFactory
{
    public const string BackendIdentifier = "Framebuffer";

    private static readonly object Sync = new();

    private LinuxFramebuffer? _framebuffer;

    private FramebufferGraphicsFactory()
    {
    }

    public static FramebufferGraphicsFactory Instance { get; } = new();

    public string Backend => BackendIdentifier;

    public IRenderResourceCache? ResourceCache => null;

    public IRenderEffectDevice? Effects => null;

    public FramebufferOptions Options { get; private set; } = new();

    public void Configure(FramebufferOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        lock (Sync)
        {
            if (_framebuffer is not null)
            {
                throw new InvalidOperationException("Framebuffer backend has already opened the device.");
            }

            Options = options;
        }
    }

    public LinuxFramebuffer GetOrOpenFramebuffer()
    {
        lock (Sync)
        {
            return _framebuffer ??= LinuxFramebuffer.Open(Options);
        }
    }

    public IFont CreateFont(string family, double size, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false)
        => CreateFont(family, size, 96, weight, italic, underline, strikethrough);

    public IFont CreateFont(string family, double size, uint dpi, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false)
        => new FramebufferFont(family, size, dpi, weight, italic, underline, strikethrough);

    public IImage CreateImageFromFile(string path)
        => FramebufferImage.FromFile(path);

    public IImage CreateImageFromBytes(byte[] data)
        => FramebufferImage.FromBytes(data);

    public IGraphicsContext CreateContext(IRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target is IRenderSurface surface)
        {
            return CreateContext(surface);
        }

        throw new NotSupportedException("Framebuffer backend renders to IRenderSurface targets.");
    }

    public IGraphicsContext CreateMeasurementContext(uint dpi)
        => new FramebufferMeasurementContext(dpi);

    public IRenderSurface CreateSurface(RenderSurfaceDescriptor descriptor)
        => new FramebufferRenderSurface(descriptor.PixelWidth, descriptor.PixelHeight, descriptor.DpiScale, descriptor.Format, descriptor.RequiredCapabilities);

    public IGraphicsContext CreateContext(IRenderSurface surface)
    {
        if (surface is not FramebufferRenderSurface framebufferSurface)
        {
            throw new ArgumentException("Surface was not created by the framebuffer backend.", nameof(surface));
        }

        return new FramebufferGraphicsContext(framebufferSurface);
    }

    public IImage CreateImageView(IRenderSurface surface)
    {
        if (surface is not IPixelBufferSource pixelSource)
        {
            throw new NotSupportedException("Only CPU pixel render surfaces can be used as images by the framebuffer backend.");
        }

        return CreateImageView(pixelSource);
    }

    public IImage CreateImageView(IPixelBufferSource source)
        => FramebufferImage.FromPixelSource(source);

    public IImage CreateImageView(IExternalRasterSource source)
        => throw new NotSupportedException("External GPU raster sources are not supported by the framebuffer backend.");

    public bool TryReadPixels(IRenderSurface source, Span<byte> destination, int destinationStrideBytes)
    {
        if (source is not ICpuPixelSurface cpu || destinationStrideBytes < cpu.PixelWidth * 4)
        {
            return false;
        }

        var pixels = cpu.GetReadOnlyPixelSpan();
        int sourceStride = cpu.StrideBytes;
        int rowBytes = cpu.PixelWidth * 4;
        for (int y = 0; y < cpu.PixelHeight; y++)
        {
            pixels.Slice(y * sourceStride, rowBytes)
                .CopyTo(destination.Slice(y * destinationStrideBytes, rowBytes));
        }

        return true;
    }

    public IRenderOperation RequestReadback(IRenderSurface source) => RenderOperation.Completed;

    public IRenderOperation FlushAsyncWork() => RenderOperation.Completed;

    public IImageFilterExecutor CreateImageFilterExecutor() => new CpuImageFilterExecutor();

    public void Present(FramebufferRenderSurface surface)
        => GetOrOpenFramebuffer().Present(surface, Options.WaitForVSync);

    public void Dispose()
    {
        lock (Sync)
        {
            _framebuffer?.Dispose();
            _framebuffer = null;
        }
    }
}
