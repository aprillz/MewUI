using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Skia.Interop.Gdi;

/// <summary>
/// Win32 + WGL P/Invoke needed by <see cref="GdiGLReadbackSkiaSurfaceHost"/>: hidden window,
/// WGL context, and <c>glReadPixels</c> for FBO → DIB transfer.
/// </summary>
internal static unsafe partial class WglBootstrap
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

    [LibraryImport("opengl32.dll", EntryPoint = "wglGetCurrentContext")]
    public static partial nint wglGetCurrentContext();

    // GL core 1.x — exported directly by opengl32.dll
    public const uint GL_BGRA = 0x80E1;
    public const uint GL_UNSIGNED_BYTE = 0x1401;
    public const uint GL_UNSIGNED_INT_8_8_8_8_REV = 0x8367;

    [DllImport("opengl32.dll", EntryPoint = "glReadPixels", ExactSpelling = true)]
    public static extern void glReadPixels(int x, int y, int width, int height, uint format, uint type, nint pixels);

    [DllImport("opengl32.dll", EntryPoint = "glPixelStorei", ExactSpelling = true)]
    public static extern void glPixelStorei(uint pname, int param);

    [DllImport("opengl32.dll", EntryPoint = "glGetString", ExactSpelling = true)]
    public static extern nint glGetString(uint name);

    public const uint GL_PACK_ALIGNMENT = 0x0D05;
    public const uint GL_PACK_ROW_LENGTH = 0x0D02;
    public const uint GL_VENDOR = 0x1F00;
    public const uint GL_RENDERER = 0x1F01;
    public const uint GL_VERSION = 0x1F02;

    public static string? GetGLString(uint name) => Marshal.PtrToStringAnsi(glGetString(name));
}
