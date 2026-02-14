using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Structs;

[StructLayout(LayoutKind.Sequential)]
internal struct SIZE
{
    public int cx;
    public int cy;

    public SIZE(int cx, int cy)
    {
        this.cx = cx;
        this.cy = cy;
    }
}
