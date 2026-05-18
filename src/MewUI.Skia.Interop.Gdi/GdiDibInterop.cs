using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Skia.Interop.Gdi;

/// <summary>
/// Minimal Win32 bindings for top-down 32-bpp BGRA DIB section allocation. Used by
/// <see cref="GdiSkiaSurfaceHost"/> to allocate shared memory that Skia paints into and
/// GDI samples from without an intermediate copy.
/// </summary>
internal static unsafe partial class GdiDibInterop
{
    [LibraryImport("user32.dll", EntryPoint = "GetDC")]
    public static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll", EntryPoint = "ReleaseDC")]
    public static partial int ReleaseDC(nint hWnd, nint hDC);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateDIBSection")]
    public static partial nint CreateDIBSection(
        nint hdc,
        ref BITMAPINFO pbmi,
        uint iUsage,
        out nint ppvBits,
        nint hSection,
        uint dwOffset);

    [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint hObject);

    public const uint DIB_RGB_COLORS = 0;
    public const uint BI_RGB = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth;
        public int biHeight;          // negative for top-down
        public ushort biPlanes;
        public ushort biBitCount;
        public uint biCompression;
        public uint biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public uint biClrUsed;
        public uint biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public BITMAPINFOHEADER bmiHeader;
        public uint bmiColors0;  // placeholder for color table (unused for 32-bpp)
    }

    public static BITMAPINFO Create32bppTopDown(int width, int height) => new()
    {
        bmiHeader = new BITMAPINFOHEADER
        {
            biSize = (uint)sizeof(BITMAPINFOHEADER),
            biWidth = width,
            biHeight = -height,       // top-down
            biPlanes = 1,
            biBitCount = 32,
            biCompression = BI_RGB,
        }
    };
}
