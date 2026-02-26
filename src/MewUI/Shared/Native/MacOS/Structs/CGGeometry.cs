using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Structs;

// CoreGraphics geometry types (CGRect/CGSize/CGPoint).
// Keep layout compatible with native (double-based) types used by AppKit/CoreGraphics on macOS.

[StructLayout(LayoutKind.Sequential)]
internal readonly struct CGPoint
{
    public readonly double X;
    public readonly double Y;

    public CGPoint(double x, double y)
    {
        X = x;
        Y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct CGSize
{
    public readonly double Width;
    public readonly double Height;

    public CGSize(double width, double height)
    {
        Width = width;
        Height = height;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct CGRect
{
    public readonly CGPoint Origin;
    public readonly CGSize Size;

    public CGRect(double x, double y, double width, double height)
    {
        Origin = new CGPoint(x, y);
        Size = new CGSize(width, height);
    }
}

