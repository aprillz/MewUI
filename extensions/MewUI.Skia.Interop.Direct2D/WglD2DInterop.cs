using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Skia.Interop.Direct2D;

/// <summary>
/// Win32 native interop needed by <see cref="SkiaWglInteropHost"/>: hidden window + WGL
/// context bootstrap, plus <c>WGL_NV_DX_interop</c> bindings to share a GL texture with a
/// D3D11 texture, plus the single ID3D11Device vtable slot needed to allocate the D3D11
/// texture against D2D's existing device.
/// </summary>
internal static unsafe partial class WglD2DInterop
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize;
        public ushort nVersion;
        public uint dwFlags;
        public byte iPixelType;
        public byte cColorBits;
        public byte cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift;
        public byte cAlphaBits, cAlphaShift;
        public byte cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits;
        public byte cDepthBits, cStencilBits, cAuxBuffers;
        public byte iLayerType;
        public byte bReserved;
        public uint dwLayerMask, dwVisibleMask, dwDamageMask;
    }

    public const uint PFD_DRAW_TO_WINDOW = 0x00000004;
    public const uint PFD_SUPPORT_OPENGL = 0x00000020;
    public const uint PFD_DOUBLEBUFFER   = 0x00000001;
    public const byte PFD_TYPE_RGBA      = 0;
    public const byte PFD_MAIN_PLANE     = 0;
    public const uint WS_POPUP           = 0x80000000;

    [LibraryImport("user32.dll", EntryPoint = "CreateWindowExW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowExW(
        uint dwExStyle, string lpClassName, string? lpWindowName,
        uint dwStyle, int X, int Y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll", EntryPoint = "GetDC")]
    public static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "ReleaseDC")]
    public static partial int ReleaseDC(nint hWnd, nint hDC);

    [LibraryImport("user32.dll", EntryPoint = "DestroyWindow")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("gdi32.dll", EntryPoint = "ChoosePixelFormat")]
    public static partial int ChoosePixelFormat(nint hdc, in PIXELFORMATDESCRIPTOR ppfd);

    [LibraryImport("gdi32.dll", EntryPoint = "SetPixelFormat")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetPixelFormat(nint hdc, int format, in PIXELFORMATDESCRIPTOR ppfd);

    [LibraryImport("opengl32.dll", EntryPoint = "wglCreateContext")]
    public static partial nint wglCreateContext(nint hdc);

    [LibraryImport("opengl32.dll", EntryPoint = "wglMakeCurrent")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool wglMakeCurrent(nint hdc, nint hglrc);

    [LibraryImport("opengl32.dll", EntryPoint = "wglDeleteContext")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool wglDeleteContext(nint hglrc);

    [LibraryImport("opengl32.dll", EntryPoint = "wglGetProcAddress", StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint wglGetProcAddress(string name);

    [LibraryImport("opengl32.dll", EntryPoint = "wglGetCurrentContext")]
    public static partial nint wglGetCurrentContext();

    [LibraryImport("opengl32.dll", EntryPoint = "wglGetCurrentDC")]
    public static partial nint wglGetCurrentDC();

    public const uint WGL_ACCESS_READ_ONLY_NV      = 0x00000000;
    public const uint WGL_ACCESS_READ_WRITE_NV     = 0x00000001;
    public const uint WGL_ACCESS_WRITE_DISCARD_NV  = 0x00000002;

    private static delegate* unmanaged<nint, nint>                          _pDXOpenDevice;
    private static delegate* unmanaged<nint, byte>                          _pDXCloseDevice;
    private static delegate* unmanaged<nint, nint, uint, uint, uint, nint>  _pDXRegisterObject;
    private static delegate* unmanaged<nint, nint, byte>                    _pDXUnregisterObject;
    private static delegate* unmanaged<nint, int, nint*, byte>              _pDXLockObjects;
    private static delegate* unmanaged<nint, int, nint*, byte>              _pDXUnlockObjects;
    private static bool _wglNvLoaded;

    public static bool LoadWglNvDxInterop()
    {
        if (_wglNvLoaded) return true;

        _pDXOpenDevice = (delegate* unmanaged<nint, nint>)wglGetProcAddress("wglDXOpenDeviceNV");
        _pDXCloseDevice = (delegate* unmanaged<nint, byte>)wglGetProcAddress("wglDXCloseDeviceNV");
        _pDXRegisterObject = (delegate* unmanaged<nint, nint, uint, uint, uint, nint>)wglGetProcAddress("wglDXRegisterObjectNV");
        _pDXUnregisterObject = (delegate* unmanaged<nint, nint, byte>)wglGetProcAddress("wglDXUnregisterObjectNV");
        _pDXLockObjects = (delegate* unmanaged<nint, int, nint*, byte>)wglGetProcAddress("wglDXLockObjectsNV");
        _pDXUnlockObjects = (delegate* unmanaged<nint, int, nint*, byte>)wglGetProcAddress("wglDXUnlockObjectsNV");

        _wglNvLoaded = _pDXOpenDevice != null && _pDXCloseDevice != null
                     && _pDXRegisterObject != null && _pDXUnregisterObject != null
                     && _pDXLockObjects != null && _pDXUnlockObjects != null;
        return _wglNvLoaded;
    }

    public static nint DXOpenDevice(nint d3dDevice) => _pDXOpenDevice(d3dDevice);
    public static bool DXCloseDevice(nint deviceInterop) => _pDXCloseDevice(deviceInterop) != 0;

    public static nint DXRegisterObject(nint deviceInterop, nint dxObject, uint glName, uint type, uint access)
        => _pDXRegisterObject(deviceInterop, dxObject, glName, type, access);

    public static bool DXUnregisterObject(nint deviceInterop, nint obj)
        => _pDXUnregisterObject(deviceInterop, obj) != 0;

    public static bool DXLockObject(nint deviceInterop, nint obj)
    {
        return _pDXLockObjects(deviceInterop, 1, &obj) != 0;
    }

    public static bool DXUnlockObject(nint deviceInterop, nint obj)
    {
        return _pDXUnlockObjects(deviceInterop, 1, &obj) != 0;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct DXGI_SAMPLE_DESC
    {
        public uint Count;
        public uint Quality;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct D3D11_TEXTURE2D_DESC
    {
        public uint Width;
        public uint Height;
        public uint MipLevels;
        public uint ArraySize;
        public uint Format;
        public DXGI_SAMPLE_DESC SampleDesc;
        public uint Usage;
        public uint BindFlags;
        public uint CPUAccessFlags;
        public uint MiscFlags;
    }

    public const uint DXGI_FORMAT_B8G8R8A8_UNORM   = 87;
    public const uint D3D11_USAGE_DEFAULT          = 0;
    public const uint D3D11_BIND_SHADER_RESOURCE   = 0x8;
    public const uint D3D11_BIND_RENDER_TARGET     = 0x20;
    public const uint D3D11_RESOURCE_MISC_SHARED   = 0x2;

    public static readonly Guid IID_ID3D11Texture2D = new("6f15aaf2-d208-4e89-9ab4-489535d34f9c");
    private const int Slot_ID3D11Device_CreateTexture2D = 5;

    public static int CreateTexture2D(nint d3d11Device, in D3D11_TEXTURE2D_DESC desc, out nint texture)
    {
        texture = 0;
        fixed (D3D11_TEXTURE2D_DESC* pDesc = &desc)
        fixed (nint* pOut = &texture)
        {
            var fn = (delegate* unmanaged<nint, D3D11_TEXTURE2D_DESC*, void*, nint*, int>)
                Vtable(d3d11Device, Slot_ID3D11Device_CreateTexture2D);
            return fn(d3d11Device, pDesc, null, pOut);
        }
    }

    public static uint Release(nint obj)
    {
        if (obj == 0) return 0;
        var fn = (delegate* unmanaged<nint, uint>)Vtable(obj, 2);
        return fn(obj);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void* Vtable(nint iface, int slot)
    {
        void** vtable = *(void***)iface;
        return vtable[slot];
    }
}
