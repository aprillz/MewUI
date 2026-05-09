using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Video.Sample.Decoding;

/// <summary>
/// Represents the GPU-side resource associated with a decoded video frame.
/// Disposing releases the GPU allocation and triggers any required backend
/// cleanup (texture-cache flush, pool return, etc.).
/// </summary>
public interface IGpuFrameResource : IDisposable { }

/// <summary>
/// macOS VideoToolbox zero-copy resource. Owns the CVMetalTextureRef /
/// CVPixelBuffer pair and flushes the texture cache on disposal so stale
/// IOSurface entries are reclaimed immediately rather than accumulating.
/// </summary>
internal sealed class VideoToolboxGpuResource : IGpuFrameResource
{
    private readonly VideoToolboxFrameTexture _texture;
    private readonly VideoToolboxMetalBridge _bridge;
    private bool _disposed;

    internal VideoToolboxGpuResource(VideoToolboxFrameTexture texture, VideoToolboxMetalBridge bridge)
    {
        _texture = texture;
        _bridge = bridge;
    }

    public VideoToolboxFrameTexture Texture => _texture;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _texture.Dispose();
        _bridge.Flush();
    }
}

/// <summary>
/// Windows D3D11 GPU resource. Owns either a converter-pool texture
/// (released back to the pool via <c>ReturnConvertedTexture</c>) or a raw
/// decoder surface (COM-released via <c>Marshal.Release</c>).
/// </summary>
internal sealed class D3D11GpuResource : IGpuFrameResource
{
    private readonly nint _textureHandle;
    private readonly Action<nint> _release;
    private bool _disposed;

    internal D3D11GpuResource(nint textureHandle, int subresourceIndex, nint deviceHandle, Action<nint> release)
    {
        _textureHandle = textureHandle;
        SubresourceIndex = subresourceIndex;
        DeviceHandle = deviceHandle;
        _release = release;
    }

    public nint TextureHandle => _disposed ? 0 : _textureHandle;
    public int SubresourceIndex { get; }
    public nint DeviceHandle { get; }

    public bool TryRetain(nint handle)
    {
        if (_disposed || handle == 0 || handle != _textureHandle) return false;
        Marshal.AddRef(handle);
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _release(_textureHandle);
    }
}

/// <summary>
/// Linux VAAPI GPU resource. Carries the (VADisplay, VASurfaceID) pair so the
/// display side can attempt zero-copy via DRM PRIME export → EGLImage import.
/// VA surfaces themselves are pool-managed by FFmpeg's hardware decoder; this
/// resource holds a reference to keep the surface valid past its decoded frame.
/// </summary>
internal sealed class VaapiGpuResource : IGpuFrameResource
{
    private bool _disposed;

    internal VaapiGpuResource(nint vaDisplay, uint vaSurfaceId)
    {
        VaDisplay = vaDisplay;
        VaSurfaceId = vaSurfaceId;
    }

    public nint VaDisplay { get; }
    public uint VaSurfaceId { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // FFmpeg pool-managed; nothing to release explicitly here.
    }
}
