using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Structs;

[StructLayout(LayoutKind.Sequential)]
internal struct XFORM
{
    public float eM11;
    public float eM12;
    public float eM21;
    public float eM22;
    public float eDx;
    public float eDy;
}
