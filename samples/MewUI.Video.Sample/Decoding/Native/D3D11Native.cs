using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Video.Sample.Decoding;

/// <summary>
/// Sample-private native bindings for ID3D11Texture2D / ID3D11Device. Used by the video
/// decoder + interop bridge to (a) create a D3D11 device with BGRA_SUPPORT for handoff to
/// FFmpeg's D3D11VA path and (b) introspect a texture's bind flags / format / array size
/// before passing it through cross-API interop wrappers (which crash if given an
/// incompatible texture). Lives in the sample so that MewUI's GL backend stays free of
/// D3D11 awareness.
/// </summary>
internal static unsafe class D3D11Native
{
    // D3D11_BIND_FLAG values
    public const uint D3D11_BIND_SHADER_RESOURCE = 0x8;

    public const uint D3D11_BIND_RENDER_TARGET = 0x20;
    public const uint D3D11_BIND_DECODER = 0x200;

    // DXGI_FORMAT subset — opaque/non-RGB formats that GL interop typically can't sample
    public const uint DXGI_FORMAT_NV12 = 103;

    public const uint DXGI_FORMAT_P010 = 104;
    public const uint DXGI_FORMAT_YUY2 = 107;
    public const uint DXGI_MEMORY_SEGMENT_GROUP_LOCAL = 0;
    public const uint DXGI_MEMORY_SEGMENT_GROUP_NON_LOCAL = 1;

    // D3D11_CREATE_DEVICE_FLAG values
    public const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x20;

    public const uint D3D11_CREATE_DEVICE_VIDEO_SUPPORT = 0x800;

    // D3D_DRIVER_TYPE values
    public const uint D3D_DRIVER_TYPE_HARDWARE = 1;

    public const uint D3D11_SDK_VERSION = 7;

    // ID3D11Texture2D vtable layout (inherits IUnknown[3] + ID3D11DeviceChild[4] + ID3D11Resource[3]):
    //   [0..2]   IUnknown: QueryInterface, AddRef, Release
    //   [3..6]   ID3D11DeviceChild: GetDevice, GetPrivateData, SetPrivateData, SetPrivateDataInterface
    //   [7..9]   ID3D11Resource: GetType, SetEvictionPriority, GetEvictionPriority
    //   [10]     ID3D11Texture2D::GetDesc
    private const int VTBL_GETDEVICE_INDEX = 3;
    private const int VTBL_GETDESC_INDEX = 10;
    private const int ContextFlushIndex = 111;

    private static readonly Guid IID_ID3D11Multithread = new("9B7E4E00-342C-4106-A19F-4F2704F689F0");
    private static readonly Guid IID_IDXGIDevice = new("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
    private static readonly Guid IID_IDXGIAdapter3 = new("645967A4-1392-4310-A798-8053CE3E93FD");

    /// <summary>
    /// Calls <c>ID3D11Texture2D::GetDesc</c> via the COM vtable to read the texture
    /// description. Returns false when <paramref name="texture"/> is 0; the signature
    /// is <c>void GetDesc(D3D11_TEXTURE2D_DESC* pDesc)</c> so failure modes show up
    /// as garbage in the returned struct rather than an HRESULT.
    /// </summary>
    public static bool TryGetTexture2DDesc(nint texture, out D3D11_TEXTURE2D_DESC desc)
    {
        desc = default;
        if (texture == 0) return false;

        var vtbl = *(nint**)texture;
        var getDesc = (delegate* unmanaged[Stdcall]<nint, D3D11_TEXTURE2D_DESC*, void>)vtbl[VTBL_GETDESC_INDEX];
        D3D11_TEXTURE2D_DESC local;
        getDesc(texture, &local);
        desc = local;
        return true;
    }

    /// <summary>
    /// Calls <c>ID3D11DeviceChild::GetDevice</c> on a texture. The returned device is
    /// AddRef'ed by D3D11; callers must release it.
    /// </summary>
    public static bool TryGetTextureDevice(nint texture, out nint device)
    {
        device = 0;
        if (texture == 0) return false;

        var vtbl = *(nint**)texture;
        var getDevice = (delegate* unmanaged[Stdcall]<nint, nint*, void>)vtbl[VTBL_GETDEVICE_INDEX];
        nint local;
        getDevice(texture, &local);
        device = local;
        return local != 0;
    }

    /// <summary>
    /// Validates that a texture is sample-able by cross-API interop wrappers
    /// (WGL_NV_DX_interop, D2D::CreateBitmapFromDxgiSurface). Returns null when OK,
    /// otherwise a short reason string suitable for logging. Caller must perform this
    /// check BEFORE passing the texture to interop wrappers — those wrappers AV inside
    /// the driver when given an incompatible texture.
    /// </summary>
    public static string? ValidateForInterop(nint texture)
    {
        if (!TryGetTexture2DDesc(texture, out var desc))
        {
            return "GetDesc returned false (texture pointer invalid)";
        }
        if ((desc.BindFlags & D3D11_BIND_SHADER_RESOURCE) == 0)
        {
            return $"missing D3D11_BIND_SHADER_RESOURCE (bind=0x{desc.BindFlags:X})";
        }
        if (desc.ArraySize > 1)
        {
            return $"texture is a Texture2DArray (ArraySize={desc.ArraySize})";
        }
        if (desc.Format is DXGI_FORMAT_NV12 or DXGI_FORMAT_P010 or DXGI_FORMAT_YUY2)
        {
            return $"YUV format DXGI_FORMAT={desc.Format} is not GL/D2D-sampleable";
        }
        return null;
    }

    /// <summary>
    /// Direct binding to <c>D3D11CreateDevice</c>. Used to create a D3D11 device with
    /// specific feature flags (e.g. BGRA_SUPPORT) before handing it to other libraries
    /// (FFmpeg, D2D) that don't expose those flags through their own APIs.
    /// </summary>
    [DllImport("d3d11.dll")]
    public static extern int D3D11CreateDevice(
        nint adapter,
        uint driverType,
        nint software,
        uint flags,
        nint pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,
        out nint ppDevice,
        out uint pFeatureLevel,
        out nint ppImmediateContext);

    public static bool TryEnableMultithreadProtection(nint deviceContext)
    {
        if (deviceContext == 0)
        {
            return false;
        }

        nint multithread = 0;
        Guid iid = IID_ID3D11Multithread;
        if (Marshal.QueryInterface(deviceContext, in iid, out multithread) < 0 || multithread == 0)
        {
            return false;
        }

        try
        {
            var vtbl = *(nint**)multithread;
            var setMultithreadProtected = (delegate* unmanaged[Stdcall]<nint, int, int>)vtbl[5];
            return setMultithreadProtected(multithread, 1) != 0;
        }
        finally
        {
            Marshal.Release(multithread);
        }
    }

    public static void FlushDeviceContext(nint deviceContext)
    {
        if (deviceContext == 0)
        {
            return;
        }

        var vtbl = *(nint**)deviceContext;
        var flush = (delegate* unmanaged[Stdcall]<nint, void>)vtbl[ContextFlushIndex];
        flush(deviceContext);
    }

    public static bool TryQueryVideoMemoryInfo(nint d3d11Device, uint segmentGroup, out DXGI_QUERY_VIDEO_MEMORY_INFO info)
    {
        info = default;
        if (d3d11Device == 0)
        {
            return false;
        }

        nint dxgiDevice = 0;
        nint adapter = 0;
        nint adapter3 = 0;

        try
        {
            Guid dxgiDeviceId = IID_IDXGIDevice;
            if (Marshal.QueryInterface(d3d11Device, in dxgiDeviceId, out dxgiDevice) < 0 || dxgiDevice == 0)
            {
                return false;
            }

            var dxgiDeviceVtable = *(nint**)dxgiDevice;
            var getAdapter = (delegate* unmanaged[Stdcall]<nint, nint*, int>)dxgiDeviceVtable[7];
            if (getAdapter(dxgiDevice, &adapter) < 0 || adapter == 0)
            {
                return false;
            }

            Guid adapter3Id = IID_IDXGIAdapter3;
            if (Marshal.QueryInterface(adapter, in adapter3Id, out adapter3) < 0 || adapter3 == 0)
            {
                return false;
            }

            var adapter3Vtable = *(nint**)adapter3;
            var queryVideoMemoryInfo = (delegate* unmanaged[Stdcall]<nint, uint, uint, DXGI_QUERY_VIDEO_MEMORY_INFO*, int>)adapter3Vtable[14];
            DXGI_QUERY_VIDEO_MEMORY_INFO localInfo;
            if (queryVideoMemoryInfo(adapter3, 0, segmentGroup, &localInfo) < 0)
            {
                return false;
            }

            info = localInfo;
            return true;
        }
        finally
        {
            if (adapter3 != 0)
            {
                Marshal.Release(adapter3);
            }

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

    public static bool TryGetAdapterLuid(nint d3d11Device, out ulong lowPart, out long highPart)
    {
        lowPart = 0;
        highPart = 0;

        if (d3d11Device == 0)
        {
            return false;
        }

        nint dxgiDevice = 0;
        nint adapter = 0;

        try
        {
            Guid dxgiDeviceId = IID_IDXGIDevice;
            if (Marshal.QueryInterface(d3d11Device, in dxgiDeviceId, out dxgiDevice) < 0 || dxgiDevice == 0)
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

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_QUERY_VIDEO_MEMORY_INFO
    {
        public ulong Budget;
        public ulong CurrentUsage;
        public ulong AvailableForReservation;
        public ulong CurrentReservation;
    }

    /// <summary>D3D11_TEXTURE2D_DESC layout — matches the C struct exactly.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;            // DXGI_FORMAT
        public uint SampleDescCount;
        public uint SampleDescQuality;
        public uint Usage;             // D3D11_USAGE
        public uint BindFlags;         // D3D11_BIND_FLAG
        public uint CPUAccessFlags;    // D3D11_CPU_ACCESS_FLAG
        public uint MiscFlags;         // D3D11_RESOURCE_MISC_FLAG
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct DXGI_ADAPTER_DESC
    {
        public fixed char Description[128];
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
}
