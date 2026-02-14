using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Structs;

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFOHEADER
{
    public uint biSize;
    public int biWidth;
    public int biHeight;
    public ushort biPlanes;
    public ushort biBitCount;
    public uint biCompression;
    public uint biSizeImage;
    public int biXPelsPerMeter;
    public int biYPelsPerMeter;
    public uint biClrUsed;
    public uint biClrImportant;

    public static BITMAPINFOHEADER Create() => new() { biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>() };
}

[StructLayout(LayoutKind.Sequential)]
internal struct RGBQUAD
{
    public byte rgbBlue;
    public byte rgbGreen;
    public byte rgbRed;
    public byte rgbReserved;
}

[StructLayout(LayoutKind.Sequential)]
internal struct BITMAPINFO
{
    public BITMAPINFOHEADER bmiHeader;
    public RGBQUAD bmiColors;

    public static BITMAPINFO Create32bpp(int width, int height) => new BITMAPINFO
    {
        bmiHeader = new BITMAPINFOHEADER
        {
            biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
            biWidth = width,
            biHeight = -height, // Top-down DIB
            biPlanes = 1,
            biBitCount = 32,
            biCompression = 0 // BI_RGB
        }
    };
}
