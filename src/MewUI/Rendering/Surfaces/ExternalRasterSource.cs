using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering;

public readonly record struct ExternalRasterPlane(
    int Index,
    nint NativeHandle,
    int PixelWidth,
    int PixelHeight,
    int StrideBytes,
    RenderPixelFormat Format);

public interface IExternalRasterSource : IRasterSource, IDisposable
{
    RenderPixelFormat Format { get; }

    BitmapAlphaMode AlphaMode { get; }

    bool YFlipped { get; }

    SurfaceCapabilities Capabilities { get; }

    IReadOnlyList<ExternalRasterPlane> Planes { get; }

    IExternalRasterLease Acquire();
}

public interface IExternalRasterLease : IDisposable
{
    int PixelWidth { get; }

    int PixelHeight { get; }

    bool YFlipped { get; }
}

public interface IGlTextureLease : IExternalRasterLease
{
    uint TextureId { get; }
}

public interface IMetalTextureLease : IExternalRasterLease
{
    nint Texture { get; }
}

public interface ID3D11TextureLease : IExternalRasterLease
{
    nint Texture2D { get; }

    nint DxgiSurface { get; }
}
