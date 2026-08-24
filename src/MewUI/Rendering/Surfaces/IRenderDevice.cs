using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering;

public interface IRenderDevice
{
    RenderDeviceIdentity RenderIdentity => default;

    IRenderSurface CreateSurface(RenderSurfaceDescriptor descriptor);

    IGraphicsContext CreateContext(IRenderSurface surface);

    IImage CreateImageView(IRenderSurface surface);

    IImage CreateImageView(IPixelBufferSource source);

    IImage CreateImageView(IExternalRasterSource source);

    bool TryReadPixels(IRenderSurface source, Span<byte> destination, int destinationStrideBytes);

    IRenderOperation RequestReadback(IRenderSurface source);

    IRenderOperation FlushAsyncWork();

    IRenderResourceCache? ResourceCache { get; }

    IRenderEffectDevice? Effects { get; }
}

public readonly record struct RenderDeviceIdentity(ulong DeviceId, uint Generation, ulong ContextId = 0)
{
    private static long _nextDeviceId;

    internal static ulong AllocateDeviceId() => (ulong)Interlocked.Increment(ref _nextDeviceId);
}
