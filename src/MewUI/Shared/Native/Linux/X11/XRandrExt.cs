using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

/// <summary>
/// Optional monitor geometry and refresh-rate queries; callers must handle unavailable extensions.
/// </summary>
internal static partial class XRandrExt
{
    private const string LibraryName = "libXrandr.so.2";

    [LibraryImport(LibraryName)]
    public static partial int XRRQueryExtension(nint display, out int eventBase, out int errorBase);

    [LibraryImport(LibraryName)]
    public static partial nint XRRGetScreenInfo(nint display, nint window);

    [LibraryImport(LibraryName)]
    public static partial short XRRConfigCurrentRate(nint config);

    [LibraryImport(LibraryName)]
    public static partial void XRRFreeScreenConfigInfo(nint config);

    [LibraryImport(LibraryName)]
    public static partial int XRRQueryVersion(nint display, out int major, out int minor);

    [LibraryImport(LibraryName)]
    public static partial nint XRRGetMonitors(nint display, nint window, int active, out int count);

    [LibraryImport(LibraryName)]
    public static partial void XRRFreeMonitors(nint monitors);

    [LibraryImport("libXinerama.so.1")]
    public static partial int XineramaIsActive(nint display);

    [LibraryImport("libXinerama.so.1")]
    public static partial nint XineramaQueryScreens(nint display, out int count);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MonitorInfo
    {
        public nuint Name;
        public int Primary;
        public int Automatic;
        public int OutputCount;
        public int X;
        public int Y;
        public int Width;
        public int Height;
        public int WidthMillimeters;
        public int HeightMillimeters;
        public nint Outputs;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct XineramaScreenInfo
    {
        public int ScreenNumber;
        public short X;
        public short Y;
        public short Width;
        public short Height;
    }
}
