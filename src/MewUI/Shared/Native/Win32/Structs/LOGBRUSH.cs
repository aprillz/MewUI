using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Structs;

[StructLayout(LayoutKind.Sequential)]
internal struct LOGBRUSH
{
    public uint lbStyle;
    public uint lbColor;
    public nint lbHatch;
}
