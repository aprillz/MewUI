using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Structs;

[StructLayout(LayoutKind.Sequential)]
internal struct ICONINFO
{
    [MarshalAs(UnmanagedType.Bool)]
    public bool fIcon;
    public uint xHotspot;
    public uint yHotspot;
    public nint hbmMask;
    public nint hbmColor;
}

