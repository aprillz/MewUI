using Aprillz.MewUI.Resources;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// IImage implementation that wraps an externally-managed GPU texture (provided via
/// <see cref="IExternalLockedTexture"/>). The texture's
/// <see cref="IExternalLockedTexture.Acquire"/> / <see cref="IExternalLockedTexture.Release"/>
/// brackets are driven by <c>MewVGGraphicsContext</c> per frame; this class only handles
/// the NVG NoDelete-style wrapping of the native handle.
/// </summary>
/// <remarks>
/// <para>
/// Created from an external sample source. The wrapping caller (sample, library) owns the lifetime of the underlying
/// <see cref="IExternalLockedTexture"/> — disposing this image releases the NVG
/// bookkeeping but does NOT dispose the external texture.
/// </para>
/// <para>
/// Thread safety: NVG image-id table per backend instance. <see cref="GetOrCreateImageId"/>
/// is expected to run on the GL/Metal context thread (i.e. inside a frame); concurrent
/// access from multiple GL contexts uses the per-NVG dictionary.
/// </para>
/// </remarks>
internal sealed class MewVGExternalLockedImage : IImage
{
    private readonly IExternalLockedTexture _texture;
    private readonly bool _ownsTexture;
    private readonly Dictionary<NanoVG, int> _images = new();
    private bool _disposed;

    public IExternalLockedTexture Texture => _texture;

    public int PixelWidth => _texture.PixelWidth;
    public int PixelHeight => _texture.PixelHeight;

    /// <param name="texture">The external GPU texture to wrap.</param>
    /// <param name="ownsTexture">
    /// When <see langword="true"/>, this image's <see cref="Dispose"/> also disposes
    /// the underlying <paramref name="texture"/>. Set by factory paths that construct
    /// the texture internally (e.g. PBO+fence uploader for streaming CPU sources) so
    /// the consumer doesn't need to track it separately. Default <see langword="false"/>
    /// preserves the original semantics — caller-owned external textures (D3D11 video
    /// frame, IOSurface) survive image disposal.
    /// </param>
    public MewVGExternalLockedImage(IExternalLockedTexture texture, bool ownsTexture = false)
    {
        ArgumentNullException.ThrowIfNull(texture);
        _texture = texture;
        _ownsTexture = ownsTexture;
    }

    /// <summary>
    /// Returns an NVG image id for this texture on the given <paramref name="vg"/>. The
    /// caller MUST have invoked <c>_texture.Acquire()</c> for the current frame before
    /// calling this — the returned NVG id references the native handle directly via
    /// NoDelete, so it's only valid until the frame's matching <c>Release</c>.
    /// </summary>
    public int GetOrCreateImageId(NanoVG vg, NVGimageFlags flags)
    {
        if (_disposed) return 0;

        nint handle = _texture.NativeHandle;
        if (handle == 0) return 0;

        if (_texture.AlphaMode == BitmapAlphaMode.Premultiplied)
        {
            flags |= NVGimageFlags.Premultiplied;
        }
        if (_texture.YFlipped)
        {
            flags |= NVGimageFlags.FlipY;
        }
        flags |= NVGimageFlags.NoDelete;

        if (_images.TryGetValue(vg, out var cached) && cached != 0)
        {
            return cached;
        }

        // Try the backend-agnostic native-handle wrap first (Metal and any backend that
        // implements CreateImageFromNativeHandle). If unsupported (default returns 0), fall
        // back to the GL int-handle wrap. The texture's NativeHandle interpretation depends
        // on which backend produced the IImage — GL backends store a uint texture id in the
        // low 32 bits; Metal backends store an MTLTexture pointer.
        int id = vg.CreateImageFromNativeHandle(handle, _texture.PixelWidth, _texture.PixelHeight, flags);
        if (id == 0)
        {
            id = vg.CreateImageFromHandle((int)handle, _texture.PixelWidth, _texture.PixelHeight, flags);
        }

        if (id != 0)
        {
            _images[vg] = id;
        }
        return id;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Release NVG bookkeeping. NoDelete means the underlying texture is owned by the
        // IExternalLockedTexture — we must NOT call glDeleteTextures on it ourselves.
        foreach (var (vg, imageId) in _images)
        {
            if (imageId != 0)
            {
                vg.DeleteImage(imageId);
            }
        }
        _images.Clear();

        if (_ownsTexture)
        {
            _texture.Dispose();
        }
    }
}
