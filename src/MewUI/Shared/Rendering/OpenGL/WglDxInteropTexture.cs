using Aprillz.MewUI.Native;
using Aprillz.MewUI.Resources;
using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// Wraps an external Direct3D 11 texture (typically an FFmpeg D3D11VA-decoded
/// <c>ID3D11Texture2D</c>) as an <see cref="IExternalRasterSource"/> for the
/// MewVG GL backend, using the WGL_NV_DX_interop extension for zero-copy GPU
/// texture reads. Constructed once per source texture; lives until the consuming IImage
/// is disposed.
/// </summary>
/// <remarks>
/// <para>
/// Lifecycle (managed by MewVG GL backend frame tracking):
/// <list type="number">
///   <item>Construction: <c>wglDXOpenDeviceNV</c> on D3D11 device, allocate GL
///         texture name, <c>wglDXRegisterObjectNV</c> binds the D3D11 texture to it.</item>
///   <item>Per frame: backend calls <see cref="Acquire"/> →
///         <c>wglDXLockObjectsNV</c>; <see cref="NativeHandle"/> exposes the GL
///         texture id which NVG samples.</item>
///   <item>Per frame end: backend calls <see cref="Release"/> →
///         <c>wglDXUnlockObjectsNV</c>; D3D11 may modify the texture again.</item>
///   <item>Disposal: unlock if needed, <c>wglDXUnregisterObjectNV</c>,
///         <c>glDeleteTextures</c>, <c>wglDXCloseDeviceNV</c>.</item>
/// </list>
/// </para>
/// <para>
/// Caller responsibilities: this class assumes the GL context is current on the
/// thread that constructs / disposes / Acquire-Releases the instance. The caller
/// should check <see cref="IsAvailable"/> before constructing — driver support
/// for WGL_NV_DX_interop is not universal (typically present on NVIDIA/AMD
/// desktop drivers; spotty on Intel and remote-desktop sessions).
/// </para>
/// </remarks>
public sealed unsafe class WglDxInteropTexture : IExternalRasterSource
{
    private static readonly object _sharedDeviceGate = new();
    private static readonly Dictionary<nint, SharedDeviceHandle> s_sharedDevices = [];

    private nint _d3d11Device;
    private nint _wglDevice;       // shared wglDXOpenDeviceNV result per D3D11 device
    private nint _wglObject;       // wglDXRegisterObjectNV result
    private uint _glTextureId;
    private bool _locked;
    private bool _disposed;

    public nint NativeHandle => (nint)_glTextureId;
    public int PixelWidth { get; }
    public int PixelHeight { get; }

    public BitmapAlphaMode AlphaMode { get; }

    /// <summary>D3D11 textures are top-down (row 0 = top), unlike GL FBOs.
    /// MewVGExternalLockedImage adds <c>NVG_IMAGE_FLIPY</c> when this is
    /// <see langword="true"/>; for D3D11 sources we report <see langword="false"/>.</summary>
    public bool YFlipped => false;

    /// <summary>True when WGL_NV_DX_interop is loaded and usable on the current GL
    /// context. Call this before constructing — construction throws when the
    /// extension isn't available.</summary>
    public static bool IsAvailable => WglDxInterop.TryLoad();

    public WglDxInteropTexture(nint d3d11Device, nint d3d11Texture, int pixelWidth, int pixelHeight,
        BitmapAlphaMode alphaMode = BitmapAlphaMode.Premultiplied)
    {
        if (d3d11Device == 0) throw new ArgumentException("D3D11 device handle is 0.", nameof(d3d11Device));
        if (d3d11Texture == 0) throw new ArgumentException("D3D11 texture handle is 0.", nameof(d3d11Texture));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelWidth, 0);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelHeight, 0);

        if (!WglDxInterop.TryLoad() || !WglDxInterop.IsAvailable)
        {
            throw new InvalidOperationException("WGL_NV_DX_interop is not available on this driver / context.");
        }

        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        AlphaMode = alphaMode;

        // NOTE: Caller MUST validate that the supplied texture is compatible with
        // WGL_NV_DX_interop (single-slice Texture2D, BIND_SHADER_RESOURCE set, GL-sample-able
        // format). Passing an incompatible texture causes the driver's register implementation
        // to AV (0xC0000005) inside native code — there's no way to recover. The MewVG GL
        // backend deliberately knows nothing about D3D11; validation lives at the call site
        // (e.g., the video sample) where D3D11 introspection helpers are available.
        _wglDevice = AcquireSharedDeviceHandle(d3d11Device);
        if (_wglDevice == 0)
        {
            throw new InvalidOperationException("wglDXOpenDeviceNV failed.");
        }

        _d3d11Device = d3d11Device;

        // Allocate an empty GL texture object — wglDXRegisterObjectNV binds the D3D11
        // texture to this name. After registration, GL_TEXTURE_2D operations on the
        // name (other than Lock/Unlock + sample) are undefined.
        OpenGL32.glGenTextures(1, out _glTextureId);
        if (_glTextureId == 0)
        {
            ReleaseSharedDeviceHandle();
            throw new InvalidOperationException("glGenTextures returned 0.");
        }

        _wglObject = WglDxInterop.RegisterObject(_wglDevice, d3d11Texture, _glTextureId,
            WglDxInterop.GL_TEXTURE_2D, WglDxInterop.WGL_ACCESS_READ_ONLY_NV);
        if (_wglObject == 0)
        {
            OpenGL32.glDeleteTextures(1, ref _glTextureId);
            _glTextureId = 0;
            ReleaseSharedDeviceHandle();
            throw new InvalidOperationException("wglDXRegisterObjectNV failed.");
        }
    }

    public void Acquire()
    {
        if (_disposed || _locked) return;
        WglDxInterop.LockObject(_wglDevice, _wglObject);
        _locked = true;
    }

    public void Release()
    {
        if (_disposed || !_locked) return;
        WglDxInterop.UnlockObject(_wglDevice, _wglObject);
        _locked = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_locked)
        {
            WglDxInterop.UnlockObject(_wglDevice, _wglObject);
            _locked = false;
        }

        if (_wglObject != 0)
        {
            WglDxInterop.UnregisterObject(_wglDevice, _wglObject);
            _wglObject = 0;
        }

        if (_glTextureId != 0)
        {
            OpenGL32.glDeleteTextures(1, ref _glTextureId);
            _glTextureId = 0;
        }

        if (_wglDevice != 0)
        {
            ReleaseSharedDeviceHandle();
        }
    }

    private static nint AcquireSharedDeviceHandle(nint d3d11Device)
    {
        lock (_sharedDeviceGate)
        {
            if (s_sharedDevices.TryGetValue(d3d11Device, out var shared))
            {
                shared.RefCount++;
                return shared.WglDevice;
            }

            Marshal.AddRef(d3d11Device);
            nint wglDevice = WglDxInterop.OpenDevice(d3d11Device);
            if (wglDevice == 0)
            {
                Marshal.Release(d3d11Device);
                return 0;
            }

            s_sharedDevices.Add(d3d11Device, new SharedDeviceHandle(wglDevice));
            return wglDevice;
        }
    }

    private void ReleaseSharedDeviceHandle()
    {
        if (_d3d11Device == 0 || _wglDevice == 0)
        {
            _d3d11Device = 0;
            _wglDevice = 0;
            return;
        }

        lock (_sharedDeviceGate)
        {
            if (s_sharedDevices.TryGetValue(_d3d11Device, out var shared))
            {
                shared.RefCount--;
                if (shared.RefCount == 0)
                {
                    s_sharedDevices.Remove(_d3d11Device);
                    WglDxInterop.CloseDevice(shared.WglDevice);
                    Marshal.Release(_d3d11Device);
                }
            }
        }

        _d3d11Device = 0;
        _wglDevice = 0;
    }

    private sealed class SharedDeviceHandle(nint wglDevice)
    {
        public nint WglDevice { get; } = wglDevice;

        public int RefCount { get; set; } = 1;
    }
}
