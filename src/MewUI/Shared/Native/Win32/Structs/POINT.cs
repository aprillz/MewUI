using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Structs;

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int x;
    public int y;

    public POINT(int x, int y)
    {
        this.x = x;
        this.y = y;
    }
}
