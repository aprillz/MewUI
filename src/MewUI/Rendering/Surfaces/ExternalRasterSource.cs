using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering;

/// <summary>One plane of an external raster, such as the luma or chroma plane of a video frame.</summary>
/// <param name="Index">Plane index.</param>
/// <param name="NativeHandle">Native handle of the plane; its concrete type is defined by the producer and consumer pairing.</param>
/// <param name="PixelWidth">Width in device pixels.</param>
/// <param name="PixelHeight">Height in device pixels.</param>
/// <param name="StrideBytes">Byte distance between rows; 0 when the resource has no CPU layout.</param>
/// <param name="Format">Pixel format of the plane.</param>
public readonly record struct ExternalRasterPlane(
    int Index,
    nint NativeHandle,
    int PixelWidth,
    int PixelHeight,
    int StrideBytes,
    RenderPixelFormat Format);

/// <summary>
/// A raster resource owned outside the backend, such as a decoded video frame, that a backend imports
/// without copying; <see cref="Acquire"/> leases it for one use.
/// </summary>
public interface IExternalRasterSource : IRasterSource, IDisposable
{
    /// <summary>Gets the pixel format.</summary>
    RenderPixelFormat Format { get; }

    /// <summary>Gets the alpha mode of the pixels.</summary>
    BitmapAlphaMode AlphaMode { get; }

    /// <summary>Gets whether row 0 is the bottom of the image.</summary>
    bool YFlipped { get; }

    /// <summary>
    /// Gets the surface capabilities the resource offers; <see cref="SurfaceCapabilities.ExternallySynchronized"/>
    /// means the lease is what synchronizes access.
    /// </summary>
    SurfaceCapabilities Capabilities { get; }

    /// <summary>Gets the planes of the resource.</summary>
    IReadOnlyList<ExternalRasterPlane> Planes { get; }

    /// <summary>Leases the resource for the consuming backend; dispose the lease once the draw that reads it has been issued.</summary>
    IExternalRasterLease Acquire();
}

/// <summary>
/// A lease over an externally-managed GPU/CPU raster resource. Native handle semantics
/// are defined by the producer/consumer pairing - typically a GPU texture handle belonging to
/// the active rendering backend.
/// </summary>
public interface IExternalRasterLease : IDisposable
{
    /// <summary>Gets the width in device pixels.</summary>
    int PixelWidth { get; }

    /// <summary>Gets the height in device pixels.</summary>
    int PixelHeight { get; }

    /// <summary>Gets whether row 0 is the bottom of the image.</summary>
    bool YFlipped { get; }

    /// <summary>
    /// Primary native handle for the leased resource. Concrete meaning is API-paired
    /// with the consuming backend; <c>0</c> indicates the handle is unavailable.
    /// </summary>
    nint NativeHandle { get; }

    /// <summary>
    /// Optional secondary native handle aliasing the same resource as <see cref="NativeHandle"/>, for
    /// backends whose API pairs a second interface with the primary one; producers without a cached
    /// alternate return <c>0</c> and the consumer derives it from <see cref="NativeHandle"/>.
    /// </summary>
    nint NativeAlternateHandle { get; }
}
