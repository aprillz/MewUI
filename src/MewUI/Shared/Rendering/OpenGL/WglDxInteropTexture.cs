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
///   <item>Per frame: backend calls <see cref="Acquire"/> and receives an
///         <see cref="IExternalRasterLease"/> whose <c>NativeHandle</c> is the GL texture id NVG reads.</item>
///   <item>Per frame end: backend disposes the lease, which calls
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
    private static readonly Guid IID_IDXGIDevice = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
    private static readonly object _sharedDeviceGate = new();
    private static readonly Dictionary<nint, SharedDeviceHandle> s_sharedDevices = [];

    private nint _d3d11Device;
    private nint _wglDevice;       // shared wglDXOpenDeviceNV result per D3D11 device
    private nint _wglObject;       // wglDXRegisterObjectNV result
    private uint _glTextureId;
    private readonly GpuResourceAffinity? _affinity;
    private bool _locked;
    private bool _disposed;

    public int PixelWidth { get; }
    public int PixelHeight { get; }
    public int Version => 0;
    public RenderPixelFormat Format => AlphaMode == BitmapAlphaMode.Premultiplied
        ? RenderPixelFormat.Bgra8888Premultiplied
        : RenderPixelFormat.Bgra8888;

    public BitmapAlphaMode AlphaMode { get; }

    /// <summary>D3D11 textures are top-down (row 0 = top), unlike GL FBOs.
    /// MewVGExternalRasterImage adds <c>NVG_IMAGE_FLIPY</c> when this is
    /// <see langword="true"/>; for D3D11 sources we report <see langword="false"/>.</summary>
    public bool YFlipped => false;
    public GpuResourceAffinity? Affinity => _affinity;
    public SurfaceCapabilities Capabilities =>
        SurfaceCapabilities.ExternalHandle |
        SurfaceCapabilities.ExternallySynchronized |
        SurfaceCapabilities.GpuSampleable |
        SurfaceCapabilities.Alpha |
        (AlphaMode == BitmapAlphaMode.Premultiplied ? SurfaceCapabilities.Premultiplied : SurfaceCapabilities.None);

    public IReadOnlyList<ExternalRasterPlane> Planes =>
    [
        new ExternalRasterPlane(0, (nint)_glTextureId, PixelWidth, PixelHeight, 0, Format)
    ];

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
        nint currentContext = OpenGL32.wglGetCurrentContext();
        _affinity = currentContext != 0
            ? new GpuResourceAffinity(Display: null, new GpuDeviceIdentity((ulong)currentContext, 0, currentContext))
            : TryGetD3D11AdapterLuid(d3d11Device, out var lowPart, out var highPart)
                ? new GpuResourceAffinity(Display: null, new GpuDeviceIdentity(lowPart, highPart, 0))
                : null;

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

    public IExternalRasterLease Acquire()
    {
        Lock();
        return new GLLease(this);
    }

    private void Lock()
    {
        if (_disposed || _locked) return;
        WglDxInterop.LockObject(_wglDevice, _wglObject);
        _locked = true;
    }

    private void Unlock()
    {
        if (_disposed || !_locked) return;
        WglDxInterop.UnlockObject(_wglDevice, _wglObject);
        _locked = false;
    }

    private sealed class GLLease : IExternalRasterLease, IGpuResourceAffinityProvider
    {
        private WglDxInteropTexture? _owner;

        public GLLease(WglDxInteropTexture owner)
        {
            _owner = owner;
        }

        public nint NativeHandle => (nint)(_owner?._glTextureId ?? 0);
        public nint NativeAlternateHandle => 0;
        public int PixelWidth => _owner?.PixelWidth ?? 0;
        public int PixelHeight => _owner?.PixelHeight ?? 0;
        public bool YFlipped => _owner?.YFlipped ?? false;
        public GpuResourceAffinity? Affinity => _owner?.Affinity;

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.Unlock();
        }
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

    private static bool TryGetD3D11AdapterLuid(nint d3d11Device, out ulong lowPart, out long highPart)
    {
        lowPart = 0;
        highPart = 0;

        nint dxgiDevice = 0;
        nint adapter = 0;
        try
        {
            if (Marshal.QueryInterface(d3d11Device, in IID_IDXGIDevice, out dxgiDevice) < 0 || dxgiDevice == 0)
            {
                return false;
            }

            var dxgiDeviceVtable = *(nint**)dxgiDevice;
            var getAdapter = (delegate* unmanaged[Stdcall]<nint, nint*, int>)dxgiDeviceVtable[7];
            if (getAdapter(dxgiDevice, &adapter) < 0 || adapter == 0)
            {
                return false;
            }

            var adapterVtable = *(nint**)adapter;
            var getDesc = (delegate* unmanaged[Stdcall]<nint, DXGI_ADAPTER_DESC*, int>)adapterVtable[8];
            DXGI_ADAPTER_DESC desc;
            if (getDesc(adapter, &desc) < 0)
            {
                return false;
            }

            lowPart = desc.AdapterLuid.LowPart;
            highPart = desc.AdapterLuid.HighPart;
            return true;
        }
        finally
        {
            if (adapter != 0)
            {
                Marshal.Release(adapter);
            }

            if (dxgiDevice != 0)
            {
                Marshal.Release(dxgiDevice);
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC
    {
        public unsafe fixed char Description[128];
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public LUID AdapterLuid;
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct LUID
    {
        public readonly uint LowPart;
        public readonly int HighPart;
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
