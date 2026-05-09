using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering;

public interface IRenderDevice
{
    GraphicsBackend Backend { get; }

    IRenderSurface CreateSurface(RenderSurfaceDescriptor descriptor);

    IGraphicsContext CreateContext(IRenderSurface surface);

    IImage CreateImageView(IRenderSurface surface);

    IImage CreateImageView(IPixelBufferSource source);

    IImage CreateImageView(IExternalSampleSource source);

    bool TryReadPixels(IRenderSurface source, Span<byte> destination, int destinationStrideBytes);

    IRenderOperation RequestReadback(IRenderSurface source);

    IRenderOperation FlushAsyncWork();

    IRenderResourceCache? ResourceCache { get; }

    IRenderEffectDevice? Effects { get; }
}
