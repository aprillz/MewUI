using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.DirectWrite;

internal static partial class DWrite
{
    internal static readonly Guid IID_IDWriteFactory = new("B859EE5A-D838-4B5B-A2E8-1ADC7D93DB48");

    [LibraryImport("dwrite.dll")]
    internal static partial int DWriteCreateFactory(
        DWRITE_FACTORY_TYPE factoryType,
        in Guid iid,
        out nint factory);
}
