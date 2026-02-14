using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Structs;

[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int left;
    public int top;
    public int right;
    public int bottom;

    public RECT(int left, int top, int right, int bottom)
    {
        this.left = left;
        this.top = top;
        this.right = right;
        this.bottom = bottom;
    }

    public readonly int Width => right - left;
    public readonly int Height => bottom - top;

    public static RECT FromLTRB(int left, int top, int right, int bottom) =>
        new(left, top, right, bottom);

    public static RECT FromXYWH(int x, int y, int width, int height) =>
        new(x, y, x + width, y + height);
}
