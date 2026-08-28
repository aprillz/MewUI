namespace Aprillz.MewUI.Controls;

internal static partial class Interop
{
    private const string USER32 = "user32.dll";
    private const string GDI32 = "gdi32.dll";

    [LibraryImport(USER32, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowExW(
        int exStyle,
        string className,
        string windowName,
        int style,
        int x,
        int y,
        int width,
        int height,
        nint parent,
        nint menu,
        nint instance,
        nint param);

    [LibraryImport(USER32)]
    internal static partial int DestroyWindow(nint hWnd);

    [LibraryImport(USER32)]
    internal static partial int SetWindowPos(nint hWnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport(USER32)]
    internal static partial int ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport(USER32)]
    internal static partial nint SetParent(nint child, nint parent);

    [LibraryImport(USER32)]
    internal static partial nint SetFocus(nint hWnd);

    [LibraryImport(USER32)]
    internal static partial nint GetFocus();

    [LibraryImport(USER32)]
    internal static partial int IsChild(nint parent, nint hWnd);

    [LibraryImport(USER32, EntryPoint = "GetWindowLongPtrW")]
    internal static partial nint GetWindowLongPtr(nint hWnd, int nIndex);

    [LibraryImport(USER32, EntryPoint = "SetWindowLongPtrW")]
    internal static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport(USER32, EntryPoint = "CallWindowProcW")]
    internal static partial nint CallWindowProc(nint prevWndProc, nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport(USER32, EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    // Passing 0 as the region clears any previously set region.
    [LibraryImport(USER32)]
    internal static partial int SetWindowRgn(nint hWnd, nint region, int redraw);

    [LibraryImport(GDI32)]
    internal static partial nint CreateRectRgn(int left, int top, int right, int bottom);

    [LibraryImport(USER32, EntryPoint = "SetWindowsHookExW")]
    internal static partial nint SetWindowsHookEx(int idHook, nint hookProc, nint module, uint threadId);

    [LibraryImport(USER32)]
    internal static partial int UnhookWindowsHookEx(nint hook);

    [LibraryImport(USER32)]
    internal static partial nint CallNextHookEx(nint hook, int code, nint wParam, nint lParam);

    [LibraryImport(USER32, EntryPoint = "IsDialogMessageW")]
    internal static unsafe partial int IsDialogMessage(nint dialog, MSG* message);

    [LibraryImport("kernel32.dll")]
    internal static partial uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    internal struct MSG
    {
        public nint HWnd;
        public uint Message;
        public nint WParam;
        public nint LParam;
        public uint Time;
        public int PointX;
        public int PointY;
    }
}
