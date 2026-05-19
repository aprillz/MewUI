using System.Runtime.InteropServices;

using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

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
/// Windows D3D11 GPU resource. Owns a raw decoder surface, released via the
/// callback supplied by the decoder/display path.
/// </summary>
internal sealed class D3D11GpuResource : IGpuFrameResource, IExternalRasterSource, IGpuResourceAffinityProvider
{
    private static readonly Guid IID_IDXGISurface = new("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");

    private readonly nint _textureHandle;
    private readonly Action<nint> _release;
    private readonly GpuResourceAffinity? _affinity;
    private bool _disposed;

    internal D3D11GpuResource(nint textureHandle, int subresourceIndex, nint deviceHandle, Action<nint> release)
    {
        _textureHandle = textureHandle;
        SubresourceIndex = subresourceIndex;
        DeviceHandle = deviceHandle;
        _release = release;
        _affinity = deviceHandle != 0
            ? new GpuResourceAffinity(Display: null, new GpuDeviceIdentity((ulong)deviceHandle, 0, deviceHandle))
            : null;
    }

    public nint TextureHandle => _disposed ? 0 : _textureHandle;
    public int SubresourceIndex { get; }
    public nint DeviceHandle { get; }
    public int PixelWidth { get; private set; }
    public int PixelHeight { get; private set; }
    public int Version => 0;
    public RenderPixelFormat Format => RenderPixelFormat.Bgra8888Premultiplied;
    public BitmapAlphaMode AlphaMode => BitmapAlphaMode.Ignore;
    public bool YFlipped => false;
    public GpuResourceAffinity? Affinity => _affinity;
    public SurfaceCapabilities Capabilities =>
        SurfaceCapabilities.ExternalHandle |
        SurfaceCapabilities.ExternallySynchronized |
        SurfaceCapabilities.GpuSampleable;
    public IReadOnlyList<ExternalRasterPlane> Planes =>
    [
        new ExternalRasterPlane(0, TextureHandle, PixelWidth, PixelHeight, 0, Format)
    ];

    internal void SetRasterSize(int pixelWidth, int pixelHeight)
    {
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
    }

    public bool TryRetain(nint handle)
    {
        if (_disposed || handle == 0 || handle != _textureHandle) return false;
        Marshal.AddRef(handle);
        return true;
    }

    public IExternalRasterLease Acquire()
    {
        if (_disposed || _textureHandle == 0)
        {
            throw new ObjectDisposedException(nameof(D3D11GpuResource));
        }

        Marshal.AddRef(_textureHandle);
        nint dxgiSurface = 0;
        _ = Marshal.QueryInterface(_textureHandle, in IID_IDXGISurface, out dxgiSurface);
        return new Lease(this, _textureHandle, dxgiSurface);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _release(_textureHandle);
    }

    private sealed class Lease : IExternalRasterLease, IGpuResourceAffinityProvider
    {
        private readonly D3D11GpuResource _owner;
        private nint _texture2D;
        private nint _dxgiSurface;

        public Lease(D3D11GpuResource owner, nint texture2D, nint dxgiSurface)
        {
            _owner = owner;
            _texture2D = texture2D;
            _dxgiSurface = dxgiSurface;
        }

        public int PixelWidth => _owner.PixelWidth;
        public int PixelHeight => _owner.PixelHeight;
        public bool YFlipped => false;
        public nint NativeHandle => _texture2D;
        public nint NativeAlternateHandle => _dxgiSurface;
        public GpuResourceAffinity? Affinity => _owner.Affinity;

        public void Dispose()
        {
            if (_dxgiSurface != 0)
            {
                Marshal.Release(_dxgiSurface);
                _dxgiSurface = 0;
            }

            if (_texture2D != 0)
            {
                Marshal.Release(_texture2D);
                _texture2D = 0;
            }
        }
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
