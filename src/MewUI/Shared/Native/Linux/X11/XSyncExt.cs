using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

/// <summary>
/// Minimal SYNC extension surface used for EWMH _NET_WM_SYNC_REQUEST frame synchronization.
/// Falls back gracefully - callers must check <see cref="XSyncQueryExtension"/> and
/// <see cref="XSyncInitialize"/> before relying on these APIs.
/// </summary>
internal static partial class XSyncExt
{
    private const string LibraryName = "libXext.so.6";

    [LibraryImport(LibraryName)]
    public static partial int XSyncQueryExtension(nint display, out int eventBase, out int errorBase);

    [LibraryImport(LibraryName)]
    public static partial int XSyncInitialize(nint display, out int majorVersion, out int minorVersion);

    [LibraryImport(LibraryName)]
    public static partial nint XSyncCreateCounter(nint display, XSyncValue initialValue);

    [LibraryImport(LibraryName)]
    public static partial int XSyncSetCounter(nint display, nint counter, XSyncValue value);

    [LibraryImport(LibraryName)]
    public static partial int XSyncDestroyCounter(nint display, nint counter);
}

/// <summary>64-bit counter value split into hi/lo halves as defined by the SYNC extension.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct XSyncValue
{
    public int Hi;
    public uint Lo;

    public static XSyncValue FromInt64(long value) => new()
    {
        Hi = (int)(value >> 32),
        Lo = (uint)value,
    };
}
