using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Direct2D;

internal static partial class D2D1
{
    internal static readonly Guid IID_ID2D1Factory = new("06152247-6F50-465A-9245-118BFD3B6007");

    [LibraryImport("d2d1.dll")]
    internal static partial int D2D1CreateFactory(
        D2D1_FACTORY_TYPE factoryType,
        in Guid riid,
        nint pFactoryOptions,
        out nint ppIFactory);
}
