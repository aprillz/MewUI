using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

internal static partial class Ole32
{
    public const uint COINIT_APARTMENTTHREADED = 0x2;
    public const uint COINIT_MULTITHREADED = 0x0;

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint pvReserved, uint dwCoInit);
}
