using System.Runtime.InteropServices;

using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Native;

internal static partial class User32
{
    private const string LibraryName = "user32.dll";

    #region Window Class

    [LibraryImport(LibraryName, EntryPoint = "RegisterClassExW", SetLastError = true)]
    public static partial ushort RegisterClassEx(ref WNDCLASSEX lpWndClass);

    [LibraryImport(LibraryName, EntryPoint = "UnregisterClassW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnregisterClass(string lpClassName, nint hInstance);

    #endregion

    #region Window Creation and Management

    [LibraryImport(LibraryName, EntryPoint = "CreateWindowExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateWindowEx(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int x,
        int y,
        int nWidth,
        int nHeight,
        nint hWndParent,
        nint hMenu,
        nint hInstance,
        nint lpParam);

    [LibraryImport(LibraryName, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hWnd);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateWindow(nint hWnd);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(nint hWnd);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(nint hWnd);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EnableWindow(nint hWnd, [MarshalAs(UnmanagedType.Bool)] bool bEnable);

    #endregion

    #region Window Properties

    [LibraryImport(LibraryName, EntryPoint = "SetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowText(nint hWnd, string lpString);

    [LibraryImport(LibraryName, EntryPoint = "GetWindowTextW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int GetWindowText(nint hWnd, [Out] char[] lpString, int nMaxCount);

    [LibraryImport(LibraryName, EntryPoint = "GetWindowTextLengthW")]
    public static partial int GetWindowTextLength(nint hWnd);

    [LibraryImport(LibraryName, EntryPoint = "GetWindowLongPtrW")]
    private static partial nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [LibraryImport(LibraryName, EntryPoint = "SetWindowLongPtrW")]
    private static partial nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport(LibraryName, EntryPoint = "GetWindowLongW")]
    private static partial int GetWindowLongW(nint hWnd, int nIndex);

    [LibraryImport(LibraryName, EntryPoint = "SetWindowLongW")]
    private static partial int SetWindowLongW(nint hWnd, int nIndex, int dwNewLong);

    public static nint GetWindowLongPtr(nint hWnd, int nIndex)
        => nint.Size == 8 ? GetWindowLongPtrW(hWnd, nIndex) : (nint)GetWindowLongW(hWnd, nIndex);

    public static nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong)
        => nint.Size == 8 ? SetWindowLongPtrW(hWnd, nIndex, dwNewLong) : (nint)SetWindowLongW(hWnd, nIndex, (int)dwNewLong);

    #endregion

    #region Window Position and Size

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MoveWindow(nint hWnd, int x, int y, int nWidth, int nHeight, [MarshalAs(UnmanagedType.Bool)] bool bRepaint);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(nint hWnd, out RECT lpRect);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint hWnd, out RECT lpRect);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ClientToScreen(nint hWnd, ref POINT lpPoint);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ScreenToClient(nint hWnd, ref POINT lpPoint);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AdjustWindowRectEx(ref RECT lpRect, uint dwStyle, [MarshalAs(UnmanagedType.Bool)] bool bMenu, uint dwExStyle);

    #endregion

    #region Message Loop

    [LibraryImport(LibraryName, EntryPoint = "GetMessageW")]
    public static partial int GetMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [LibraryImport(LibraryName, EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PeekMessage(out MSG lpMsg, nint hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool TranslateMessage(ref MSG lpMsg);

    [LibraryImport(LibraryName, EntryPoint = "DispatchMessageW")]
    public static partial nint DispatchMessage(ref MSG lpMsg);

    [LibraryImport(LibraryName, EntryPoint = "SendMessageW")]
    public static partial nint SendMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    [LibraryImport(LibraryName, EntryPoint = "PostMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PostMessage(nint hWnd, uint Msg, nint wParam, nint lParam);

    [LibraryImport(LibraryName)]
    public static partial void PostQuitMessage(int nExitCode);

    [LibraryImport(LibraryName, EntryPoint = "DefWindowProcW")]
    public static partial nint DefWindowProc(nint hWnd, uint uMsg, nint wParam, nint lParam);

    #endregion

    #region Painting

    [LibraryImport(LibraryName)]
    public static partial nint BeginPaint(nint hWnd, out PAINTSTRUCT lpPaint);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EndPaint(nint hWnd, ref PAINTSTRUCT lpPaint);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InvalidateRect(nint hWnd, nint lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InvalidateRect(nint hWnd, ref RECT lpRect, [MarshalAs(UnmanagedType.Bool)] bool bErase);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ValidateRect(nint hWnd, nint lpRect);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RedrawWindow(nint hWnd, nint lprcUpdate, nint hrgnUpdate, uint flags);

    [LibraryImport(LibraryName)]
    public static partial nint GetDC(nint hWnd);

    [LibraryImport(LibraryName)]
    public static partial int ReleaseDC(nint hWnd, nint hDC);

    #endregion

    #region Focus and Capture

    [LibraryImport(LibraryName)]
    public static partial nint SetFocus(nint hWnd);

    [LibraryImport(LibraryName)]
    public static partial nint GetFocus();

    [LibraryImport(LibraryName)]
    public static partial nint SetCapture(nint hWnd);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    [LibraryImport(LibraryName)]
    public static partial nint GetCapture();

    #endregion

    #region Cursor

    [LibraryImport(LibraryName, EntryPoint = "LoadCursorW")]
    public static partial nint LoadCursor(nint hInstance, nint lpCursorName);

    [LibraryImport(LibraryName)]
    public static partial nint SetCursor(nint hCursor);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCursorPos(int x, int y);

    #endregion

    #region DPI

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetProcessDpiAwarenessContext(nint dpiContext);

    [LibraryImport(LibraryName)]
    public static partial uint GetDpiForWindow(nint hwnd);

    [LibraryImport(LibraryName)]
    public static partial uint GetDpiForSystem();

    [LibraryImport(LibraryName)]
    public static partial int GetSystemMetricsForDpi(int nIndex, uint dpi);

    #endregion

    #region Timer

    [LibraryImport(LibraryName)]
    public static partial nuint SetTimer(nint hWnd, nuint nIDEvent, uint uElapse, nint lpTimerFunc);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool KillTimer(nint hWnd, nuint uIDEvent);

    #endregion

    #region Caret

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CreateCaret(nint hWnd, nint hBitmap, int nWidth, int nHeight);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyCaret();

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowCaret(nint hWnd);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool HideCaret(nint hWnd);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCaretPos(int x, int y);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCaretPos(out POINT lpPoint);

    #endregion

    #region Keyboard

    [LibraryImport(LibraryName)]
    public static partial short GetKeyState(int nVirtKey);

    [LibraryImport(LibraryName)]
    public static partial short GetAsyncKeyState(int vKey);

    #endregion

    #region Clipboard

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool OpenClipboard(nint hWndNewOwner);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseClipboard();

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool EmptyClipboard();

    [LibraryImport(LibraryName)]
    public static partial nint SetClipboardData(uint uFormat, nint hMem);

    [LibraryImport(LibraryName)]
    public static partial nint GetClipboardData(uint uFormat);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsClipboardFormatAvailable(uint format);

    #endregion

    #region System Metrics

    [LibraryImport(LibraryName)]
    public static partial int GetSystemMetrics(int nIndex);

    #endregion

    #region Window hierarchy

    [LibraryImport(LibraryName)]
    public static partial nint GetParent(nint hWnd);

    [LibraryImport(LibraryName)]
    public static partial nint SetParent(nint hWndChild, nint hWndNewParent);

    [LibraryImport(LibraryName)]
    public static partial nint GetWindow(nint hWnd, uint uCmd);

    [LibraryImport(LibraryName)]
    public static partial nint WindowFromPoint(POINT point);

    [LibraryImport(LibraryName)]
    public static partial nint ChildWindowFromPoint(nint hWndParent, POINT point);

    #endregion

    #region Hit Testing

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool PtInRect(ref RECT lprc, POINT pt);

    #endregion

    #region Dialogs

    [LibraryImport(LibraryName, EntryPoint = "MessageBoxW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int MessageBox(nint hWnd, string lpText, string lpCaption, uint uType);

    #endregion
}
