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
        => RenderDeviceFactoryHelpers.CreateSurface(_factory, descriptor);

    public IGraphicsContext CreateContext(IRenderSurface surface)
        => RenderDeviceFactoryHelpers.CreateContext(_factory, surface);

    public IImage CreateImageView(IRenderSurface surface)
        => RenderDeviceFactoryHelpers.CreateImageView(_factory, surface);

    public IImage CreateImageView(IPixelBufferSource source)
    {
        return _factory.CreateImageFromPixelSource(source ?? throw new ArgumentNullException(nameof(source)));
    }

    public bool TryReadPixels(IRenderSurface source, Span<byte> destination, int destinationStrideBytes)
        => RenderDeviceFactoryHelpers.TryReadPixels(source, destination, destinationStrideBytes);

    public IRenderOperation RequestReadback(IRenderSurface source)
        => RenderDeviceFactoryHelpers.RequestReadback(source);

    public IRenderOperation FlushAsyncWork() => RenderOperation.Completed;

    public void Dispose()
    {
        _resourceCache.Dispose();
    }

}
