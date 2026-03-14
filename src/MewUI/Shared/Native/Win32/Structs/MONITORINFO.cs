using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Structs;

[StructLayout(LayoutKind.Sequential)]
internal struct MONITORINFO
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;

    public static MONITORINFO Create()
        => new()
        {
            cbSize = Marshal.SizeOf<MONITORINFO>()
        };
}
