using System.Runtime.InteropServices;

using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Video.Sample.Decoding;

public sealed class VideoFrame : IPixelBufferSource
{
    public TimeSpan Pts;
    public required byte[] BgraData;
    public int Width;
    public int Height;
    public bool HasCpuPixels;

    /// <summary>
    /// GPU resource associated with this frame, or null when the frame was
    /// decoded entirely in software. Disposing releases the underlying
    /// allocation and triggers backend-specific cleanup (Metal texture-cache
    /// flush on macOS, D3D11 pool return on Windows).
    /// </summary>
    public IGpuFrameResource? GpuResource;

    public int PixelWidth => Width;
    public int PixelHeight => Height;
    public int StrideBytes => Width * 4;
    public BitmapPixelFormat PixelFormat => BitmapPixelFormat.Bgra32;
    public bool IsPremultiplied => false;
    public bool HasAlpha => false;
    public int Version => 0;
    public bool IsStreaming => true;

    public nint GetTextureHandle() => (GpuResource as D3D11GpuResource)?.TextureHandle ?? 0;

    public bool RetainGpuHandle(nint handle) =>
        (GpuResource as D3D11GpuResource)?.TryRetain(handle) ?? false;

    public void ReleaseGpuHandle(nint handle)
    {
        if (handle != 0) Marshal.Release(handle);
    }

    public void ResetGpuState()
    {
        GpuResource?.Dispose();
        GpuResource = null;
        HasCpuPixels = false;
    }

    public PixelBufferLock Lock() => new(
        BgraData,
        Width,
        Height,
        StrideBytes,
        PixelFormat,
        Version,
        dirtyRegion: null,
        release: null);
}
