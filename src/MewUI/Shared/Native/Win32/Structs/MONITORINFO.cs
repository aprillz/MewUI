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

/// <summary>MONITORINFOEXW: adds the display device name, which names the monitor to the GDI device APIs.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct MONITORINFOEX
{
    public int cbSize;
    public RECT rcMonitor;
    public RECT rcWork;
    public uint dwFlags;
    public fixed char szDevice[32];

    public static MONITORINFOEX Create()
        => new()
        {
            cbSize = sizeof(MONITORINFOEX)
        };
}
