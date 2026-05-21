using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

// POINTL has the same layout as POINT (two int32s); a top-level type is required because nested
// structs inside static classes cannot be used as [UnmanagedCallersOnly] parameter types on net10.0.
[StructLayout(LayoutKind.Sequential)]
internal struct POINTL
{
    public int x;
    public int y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FORMATETC
{
    public ushort cfFormat;
    public nint ptd;
    public uint dwAspect;
    public int lindex;
    public uint tymed;
}

[StructLayout(LayoutKind.Sequential)]
internal struct STGMEDIUM
{
    public uint tymed;
    public nint unionMember;
    public nint pUnkForRelease;
}

internal static partial class Ole32
{
    public const uint COINIT_APARTMENTTHREADED = 0x2;
    public const uint COINIT_MULTITHREADED = 0x0;

    // DROPEFFECT_* constants (must match DragDropEffects flag layout).
    public const uint DROPEFFECT_NONE = 0;
    public const uint DROPEFFECT_COPY = 1;
    public const uint DROPEFFECT_MOVE = 2;
    public const uint DROPEFFECT_LINK = 4;

    // CF_* clipboard format constants used by drag-and-drop payloads.
    public const ushort CF_TEXT = 1;
    public const ushort CF_UNICODETEXT = 13;
    public const ushort CF_HDROP = 15;

    // TYMED storage medium types.
    public const uint TYMED_HGLOBAL = 1;
    public const uint TYMED_FILE = 2;
    public const uint TYMED_ISTREAM = 4;
    public const uint TYMED_ISTORAGE = 8;

    // DVASPECT request flags.
    public const uint DVASPECT_CONTENT = 1;

    public const int S_OK = 0;
    public const int S_FALSE = 1;
    public const int E_NOINTERFACE = unchecked((int)0x80004002);
    public const int E_FAIL = unchecked((int)0x80004005);
    public const int DV_E_FORMATETC = unchecked((int)0x80040064);

    [LibraryImport("ole32.dll")]
    public static partial int CoInitializeEx(nint pvReserved, uint dwCoInit);

    [LibraryImport("ole32.dll")]
    public static partial int OleInitialize(nint pvReserved);

    [LibraryImport("ole32.dll")]
    public static partial void OleUninitialize();

    [LibraryImport("ole32.dll")]
    public static partial int RegisterDragDrop(nint hwnd, nint pDropTarget);

    [LibraryImport("ole32.dll")]
    public static partial int RevokeDragDrop(nint hwnd);

    [LibraryImport("ole32.dll")]
    public static partial void ReleaseStgMedium(ref STGMEDIUM pmedium);

    [LibraryImport("ole32.dll")]
    public static unsafe partial int CoCreateInstance(in Guid rclsid, nint pUnkOuter, uint dwClsContext, in Guid riid, out nint ppv);

    // CLSCTX values.
    public const uint CLSCTX_INPROC_SERVER = 0x1;
    public const uint CLSCTX_INPROC_HANDLER = 0x2;
    public const uint CLSCTX_LOCAL_SERVER = 0x4;
    public const uint CLSCTX_ALL = CLSCTX_INPROC_SERVER | CLSCTX_INPROC_HANDLER | CLSCTX_LOCAL_SERVER;

    // CLSID_DragDropHelper
    public static readonly Guid CLSID_DragDropHelper = new(0x4657278A, 0x411B, 0x11d2, 0x83, 0x9A, 0x00, 0xC0, 0x4F, 0xD9, 0x18, 0xD0);

    // IID_IDropTargetHelper
    public static readonly Guid IID_IDropTargetHelper = new(0x4657278B, 0x411B, 0x11d2, 0x83, 0x9A, 0x00, 0xC0, 0x4F, 0xD9, 0x18, 0xD0);
}
