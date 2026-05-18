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

/// <summary>
/// A lease over an externally-managed GPU/CPU raster resource. Native handle semantics
/// are defined by the producer/consumer pairing — typically a GPU texture handle for the
/// active rendering backend (GL texture id, MTLTexture*, ID3D11Texture2D*).
/// </summary>
public interface IExternalRasterLease : IDisposable
{
    int PixelWidth { get; }

    int PixelHeight { get; }

    bool YFlipped { get; }

    /// <summary>
    /// Primary native handle for the leased resource. Concrete meaning is API-paired
    /// with the consuming backend; <c>0</c> indicates the handle is unavailable.
    /// </summary>
    nint NativeHandle { get; }

    /// <summary>
    /// Optional secondary native handle. For D3D11 this is an <c>IDXGISurface*</c> aliasing
    /// the same resource as <see cref="NativeHandle"/>; producers without a cached alternate
    /// return <c>0</c> and the consumer falls back to deriving it from <see cref="NativeHandle"/>.
    /// </summary>
    nint NativeAlternateHandle { get; }
}
