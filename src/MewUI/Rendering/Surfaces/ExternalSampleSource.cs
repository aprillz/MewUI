using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering;

public enum ExternalSampleSourceKind
{
    Unknown = 0,
    OpenGLTexture,
    MetalTexture,
    D3D11Texture,
    DxgiSurface,
    DmaBuf,
    IOSurface,
    CpuUploadStaging,
}

public readonly record struct ExternalSamplePlane(
    int Index,
    nint NativeHandle,
    int PixelWidth,
    int PixelHeight,
    int StrideBytes,
    RenderPixelFormat Format);

public interface IExternalSampleSource : IDisposable
{
    ExternalSampleSourceKind Kind { get; }

    int PixelWidth { get; }

    int PixelHeight { get; }

    RenderPixelFormat Format { get; }

    BitmapAlphaMode AlphaMode { get; }

    bool YFlipped { get; }

    SurfaceCapabilities Capabilities { get; }

    IReadOnlyList<ExternalSamplePlane> Planes { get; }

    nint NativeHandle { get; }

    IDisposable AcquireForSampling();
}
