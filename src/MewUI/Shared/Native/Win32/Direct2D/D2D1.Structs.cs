using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Direct2D;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct D2D1_COLOR_F(float r, float g, float b, float a)
{
    public readonly float r = r;
    public readonly float g = g;
    public readonly float b = b;
    public readonly float a = a;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct D2D1_POINT_2F(float x, float y)
{
    public readonly float x = x;
    public readonly float y = y;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct D2D1_RECT_F(float left, float top, float right, float bottom)
{
    public readonly float left = left;
    public readonly float top = top;
    public readonly float right = right;
    public readonly float bottom = bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct D2D1_SIZE_U(uint width, uint height)
{
    public readonly uint width = width;
    public readonly uint height = height;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct D2D1_PIXEL_FORMAT(uint format, D2D1_ALPHA_MODE alphaMode)
{
    public readonly uint format = format;
    public readonly D2D1_ALPHA_MODE alphaMode = alphaMode;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct D2D1_BITMAP_PROPERTIES(D2D1_PIXEL_FORMAT pixelFormat, float dpiX, float dpiY)
{
    public readonly D2D1_PIXEL_FORMAT pixelFormat = pixelFormat;
    public readonly float dpiX = dpiX;
    public readonly float dpiY = dpiY;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct D2D1_RENDER_TARGET_PROPERTIES(
    D2D1_RENDER_TARGET_TYPE type,
    D2D1_PIXEL_FORMAT pixelFormat,
    float dpiX,
    float dpiY,
    uint usage,
    uint minLevel)
{
    public readonly D2D1_RENDER_TARGET_TYPE type = type;
    public readonly D2D1_PIXEL_FORMAT pixelFormat = pixelFormat;
    public readonly float dpiX = dpiX;
    public readonly float dpiY = dpiY;
    public readonly uint usage = usage;
    public readonly uint minLevel = minLevel;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct D2D1_HWND_RENDER_TARGET_PROPERTIES(nint hwnd, D2D1_SIZE_U pixelSize, D2D1_PRESENT_OPTIONS presentOptions)
{
    public readonly nint hwnd = hwnd;
    public readonly D2D1_SIZE_U pixelSize = pixelSize;
    public readonly D2D1_PRESENT_OPTIONS presentOptions = presentOptions;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct D2D1_ROUNDED_RECT(D2D1_RECT_F rect, float radiusX, float radiusY)
{
    public readonly D2D1_RECT_F rect = rect;
    public readonly float radiusX = radiusX;
    public readonly float radiusY = radiusY;
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct D2D1_ELLIPSE(D2D1_POINT_2F point, float radiusX, float radiusY)
{
    public readonly D2D1_POINT_2F point = point;
    public readonly float radiusX = radiusX;
    public readonly float radiusY = radiusY;
}
