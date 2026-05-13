using Aprillz.MewUI.Resources;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// IImage implementation that wraps an externally-managed raster source. The source's
/// <see cref="IExternalRasterSource.Acquire"/> lease is driven by
/// <c>MewVGGraphicsContext</c> per frame; this class only handles the NVG
/// NoDelete-style wrapping of the native handle exposed by the lease.
/// </summary>
/// <remarks>
/// <para>
/// Created from an external raster source. The wrapping caller owns the lifetime of the
/// underlying <see cref="IExternalRasterSource"/> — disposing this image releases the
/// NVG bookkeeping but does NOT dispose the external resource.
/// </para>
/// <para>
/// Thread safety: NVG image-id table per backend instance. <see cref="GetOrCreateImageId"/>
/// is expected to run on the GL/Metal context thread (i.e. inside a frame); concurrent
/// access from multiple GL contexts uses the per-NVG dictionary.
/// </para>
/// </remarks>
internal sealed class MewVGExternalRasterImage : IImage
{
    private readonly IExternalRasterSource _source;
    private readonly Dictionary<(NanoVG vg, nint handle), int> _images = new();
    private bool _disposed;

    public IExternalRasterSource Source => _source;

    public int PixelWidth => _source.PixelWidth;
    public int PixelHeight => _source.PixelHeight;

    public MewVGExternalRasterImage(IExternalRasterSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
    }

    /// <summary>
    /// Returns an NVG image id for this texture lease on the given <paramref name="vg"/>.
    /// The caller owns the lease lifetime for the current frame.
    /// </summary>
    public int GetOrCreateImageId(NanoVG vg, IExternalRasterLease lease, NVGimageFlags flags)
    {
        if (_disposed) return 0;

        nint handle = GetNativeHandle(lease);
        if (handle == 0) return 0;

        if (_source.AlphaMode == BitmapAlphaMode.Premultiplied)
        {
            flags |= NVGimageFlags.Premultiplied;
        }
        if (lease.YFlipped)
        {
            flags |= NVGimageFlags.FlipY;
        }
        flags |= NVGimageFlags.NoDelete;

        var key = (vg, handle);
        if (_images.TryGetValue(key, out var cached) && cached != 0)
        {
            return cached;
        }

        int id = vg.CreateImageFromNativeHandle(handle, lease.PixelWidth, lease.PixelHeight, flags);
        if (id == 0)
        {
            id = vg.CreateImageFromHandle((int)handle, lease.PixelWidth, lease.PixelHeight, flags);
        }

        if (id != 0)
        {
            _images[key] = id;
        }
        return id;
    }

    private static nint GetNativeHandle(IExternalRasterLease lease)
        => lease switch
        {
            IGlTextureLease gl => (nint)gl.TextureId,
            IMetalTextureLease metal => metal.Texture,
            ID3D11TextureLease d3d => d3d.Texture2D != 0 ? d3d.Texture2D : d3d.DxgiSurface,
            _ => 0
        };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Release NVG bookkeeping. NoDelete means the underlying texture is owned by the
        // external raster source — we must NOT call glDeleteTextures on it ourselves.
        foreach (var ((vg, _), imageId) in _images)
        {
            if (imageId != 0)
            {
                vg.DeleteImage(imageId);
            }
        }
        _images.Clear();
    }
}
