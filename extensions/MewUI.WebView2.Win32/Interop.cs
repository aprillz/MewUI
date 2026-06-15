namespace Aprillz.MewUI.Controls;

internal static partial class Interop
{
    const string LibraryName = "user32.dll";

    [LibraryImport(LibraryName)]
    public static partial nint SetFocus(nint hWnd);


    // NOTE: this repo disables runtime marshalling (NativeAOT-friendly), so we must use LibraryImport
    // for string parameters, and avoid marshalled return types like bool.
    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
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

    [LibraryImport(LibraryName)]
    internal static partial int DestroyWindow(nint hWnd);

    [LibraryImport(LibraryName)]
    internal static partial int SetWindowPos(nint hWnd, nint insertAfter, int x, int y, int cx, int cy, uint flags);

    [LibraryImport(LibraryName)]
    internal static partial int ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport(LibraryName, EntryPoint = "SetWindowLongPtrW")]
    internal static partial nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport(LibraryName, EntryPoint = "CallWindowProcW")]
    internal static partial nint CallWindowProc(nint lpPrevWndFunc, nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport(LibraryName, EntryPoint = "DefWindowProcW")]
    internal static partial nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);
}
