using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Direct2D;

internal static partial class D2D1
{
    internal static readonly Guid IID_ID2D1Factory = new("06152247-6F50-465A-9245-118BFD3B6007");
    internal static readonly Guid IID_ID2D1Factory1 = new("bb12d362-daee-4b9a-aa1d-14ba401cfa1f");
    internal static readonly Guid IID_ID2D1DeviceContext = new("e8f7fe7a-191c-466d-ad95-975678bda998");

    /// <summary>D2D built-in Gaussian blur effect (CLSID_D2D1GaussianBlur).</summary>
    internal static readonly Guid CLSID_D2D1GaussianBlur = new("1FEB6D69-2FE6-4ac9-8C58-1D7F93E7A6A5");

    /// <summary>D2D built-in color matrix effect (CLSID_D2D1ColorMatrix).</summary>
    internal static readonly Guid CLSID_D2D1ColorMatrix = new("921F03D6-641C-47DF-852D-B4BB6153AE11");

    /// <summary>D2D built-in composite (Porter-Duff) effect (CLSID_D2D1Composite).</summary>
    internal static readonly Guid CLSID_D2D1Composite = new("48FC9F51-F6AC-48F1-8B58-3B28AC46F76D");

    /// <summary>D2D built-in 2D affine transform effect (CLSID_D2D12DAffineTransform) - used for offset.</summary>
    internal static readonly Guid CLSID_D2D12DAffineTransform = new("6AA97485-6354-4cfc-908C-E4A74F62C96C");

    /// <summary>DXGI pixel format BGRA8 unorm - matches the DIB / D2D RT format used here.</summary>
    internal const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;

    internal static readonly Guid IID_ID2D1Device = new("47dd575d-ad47-4dc8-822c-39ed8d4537cc");
    internal static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");
    internal static readonly Guid IID_IDXGISurface = new("cafcb56c-6ac3-4889-bf47-9e23bbd260ec");

    /// <summary>D3D11CreateDevice - creates a hardware D3D11 device for D2D 1.1 GPU pipeline.
    /// The returned <c>ID3D11Device</c> can be queried for <c>IDXGIDevice</c>, which feeds
    /// <c>ID2D1Factory1.CreateDevice</c>.</summary>
    [LibraryImport("d3d11.dll")]
    internal static partial int D3D11CreateDevice(
        nint pAdapter,
        uint driverType,            // D3D_DRIVER_TYPE
        nint software,
        uint flags,                  // D3D11_CREATE_DEVICE_FLAG
        nint pFeatureLevels,
        uint featureLevels,
        uint sdkVersion,             // D3D11_SDK_VERSION = 7
        out nint ppDevice,
        out uint pFeatureLevel,
        out nint ppImmediateContext);

    [LibraryImport("d2d1.dll")]
    internal static partial int D2D1CreateFactory(
        D2D1_FACTORY_TYPE factoryType,
        in Guid riid,
        nint pFactoryOptions,
        out nint ppIFactory);
}
