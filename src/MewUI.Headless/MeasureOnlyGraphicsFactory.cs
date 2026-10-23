using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Filters;
using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Headless;

/// <summary>
/// The registered backend with its drawing taken out: fonts, text measurement, surfaces and images
/// come from the backend, while every drawing context discards what is drawn into it.
/// </summary>
internal sealed class MeasureOnlyGraphicsFactory : IGraphicsFactory, ITextBackendFactory
{
    private readonly IGraphicsFactory _inner;

    public MeasureOnlyGraphicsFactory(IGraphicsFactory inner)
    {
        _inner = inner;
    }

    public IGraphicsFactory Inner => _inner;

    public string Backend => _inner.Backend;

    public RenderDeviceIdentity RenderIdentity => _inner.RenderIdentity;

    ITextBackendMeasurementContext ITextBackendFactory.CreateTextMeasurementContext(uint dpi)
    {
        if (_inner is not ITextBackendFactory backend)
        {
            throw new NotSupportedException($"The {_inner.Backend} backend has no text measurement context.");
        }

        return backend.CreateTextMeasurementContext(dpi);
    }

    public IFont CreateFont(string family, double size, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false)
        => _inner.CreateFont(family, size, weight, italic, underline, strikethrough);

    public IFont CreateFont(string family, double size, uint dpi, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false)
        => _inner.CreateFont(family, size, dpi, weight, italic, underline, strikethrough);

    public IGraphicsContext CreateContext(IRenderTarget target) => new NoOpGraphicsContext(target.DpiScale);

    public IGraphicsContext CreateContext(IRenderSurface surface) => new NoOpGraphicsContext(surface.DpiScale);

    public IImageFilterExecutor CreateImageFilterExecutor() => _inner.CreateImageFilterExecutor();

    public IDisposable AcquireConcurrentRenderUnit() => _inner.AcquireConcurrentRenderUnit();

    public IDisposable AcquireBackgroundRenderScope() => _inner.AcquireBackgroundRenderScope();

    public IRenderSurface CreateSurface(RenderSurfaceDescriptor descriptor) => _inner.CreateSurface(descriptor);

    public IImage CreateImageView(IRenderSurface surface) => _inner.CreateImageView(surface);

    public IImage CreateImageView(IPixelBufferSource source) => _inner.CreateImageView(source);

    public IImage CreateImageView(IExternalRasterSource source) => _inner.CreateImageView(source);

    public bool TryReadPixels(IRenderSurface source, Span<byte> destination, int destinationStrideBytes)
        => _inner.TryReadPixels(source, destination, destinationStrideBytes);

    public IRenderOperation RequestReadback(IRenderSurface source) => _inner.RequestReadback(source);

    public IRenderOperation FlushAsyncWork() => _inner.FlushAsyncWork();

    public IRenderResourceCache? ResourceCache => _inner.ResourceCache;

    public IRenderEffectDevice? Effects => _inner.Effects;

    public void Dispose()
    {
        // The backend factory is process-owned and outlives the session.
    }
}
