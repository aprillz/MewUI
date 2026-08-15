using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

/// <summary>
/// Minimal RandR surface used to read the screen's refresh rate. Callers must treat a zero rate as
/// "unknown" and fall back, because the extension may be absent and a nested or virtual server can
/// report no rate at all.
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
}
