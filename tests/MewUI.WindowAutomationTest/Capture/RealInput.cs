using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Aprillz.MewUI;

namespace MewUI.WindowAutomationTest;

internal enum OsCaptureState
{
    /// <summary>The platform has no process-wide mouse capture to inspect.</summary>
    NotApplicable,
    Free,
    Held,
    HeldByAnotherWindow,
}

/// <summary>
/// Input the platform itself delivers to the window: Win32 cursor and button events, X11 XTest events,
/// and on macOS events posted to the application queue. Positions are client DIPs of the target window.
/// </summary>
internal abstract class RealInput
{
    protected const int SETTLE_MS = 120;

    private static RealInput? _current;

    /// <summary>The driver for the platform the suite runs on, created on first use from the UI thread.</summary>
    public static RealInput Current => _current ??= Create();

    public bool IsButtonDown { get; protected set; }

    public abstract Task MoveAsync(Window window, Point client);

    public abstract Task PressAsync(Window window, Point client);

    public abstract Task ReleaseAsync(Window window, Point client);

    /// <summary>Presses or releases the Space key for the active window.</summary>
    public abstract Task SpaceAsync(Window window, bool isDown);

    /// <summary>Makes the window the platform's active window.</summary>
    public abstract Task ActivateAsync(Window window);

    /// <summary>Moves activation away from the window while the button may be held; <paramref name="other"/> is an open window of this process.</summary>
    public abstract Task DeactivateAsync(Window window, Window other);

    public abstract OsCaptureState OsCapture(Window window);

    /// <summary>Clicks a window that stands in for another application's.</summary>
    public virtual Task ClickForeignWindowAsync()
    {
        Assert.Inconclusive("This platform's input driver cannot press on another application's window.");
        return Task.CompletedTask;
    }

    private static RealInput Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new Win32Input();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacInput();
        }

        return new X11Input();
    }
}

internal sealed class Win32Input : RealInput
{
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const byte VK_SPACE = 0x20;
    private const int SM_CXSCREEN = 0;
    private const int SM_CYSCREEN = 1;

    private Win32ForeignWindow? _foreign;

    public override async Task MoveAsync(Window window, Point client)
    {
        var screen = window.ClientToScreen(client);
        SetCursorPos((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
        await Task.Delay(SETTLE_MS);
    }

    public override async Task PressAsync(Window window, Point client)
    {
        await MoveAsync(window, client);
        EnsurePointerOverOwnWindow();
        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        IsButtonDown = true;
        await Task.Delay(SETTLE_MS);
    }

    public override async Task ReleaseAsync(Window window, Point client)
    {
        await MoveAsync(window, client);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        IsButtonDown = false;
        await Task.Delay(SETTLE_MS);
    }

    public override async Task SpaceAsync(Window window, bool isDown)
    {
        // Injected keys go to whichever application is in front; never type into another one.
        if (isDown && !IsOwnWindow(GetForegroundWindow()))
        {
            Assert.Inconclusive("The foreground window belongs to another application; the key was not sent.");
        }

        keybd_event(VK_SPACE, 0, isDown ? 0 : KEYEVENTF_KEYUP, 0);
        await Task.Delay(SETTLE_MS);
    }

    /// <summary>
    /// A process in the background may not take the foreground, so the window is raised above other
    /// applications and activated by a real click on its empty corner.
    /// </summary>
    public override async Task ActivateAsync(Window window)
    {
        window.Topmost = true;
        await Task.Delay(150);
        var corner = new Point(window.ClientSize.Width - 30, window.ClientSize.Height - 30);
        await PressAsync(window, corner);
        await ReleaseAsync(window, corner);
        await Task.Delay(200);
    }

    /// <summary>The process holding the foreground may hand it to another of its windows without a click.</summary>
    public override async Task DeactivateAsync(Window window, Window other)
    {
        SetForegroundWindow(other.Handle);
        await Task.Delay(300);
    }

    public override OsCaptureState OsCapture(Window window)
    {
        nint holder = GetCapture();
        if (holder == 0)
        {
            return OsCaptureState.Free;
        }

        return holder == window.Handle ? OsCaptureState.Held : OsCaptureState.HeldByAnotherWindow;
    }

    /// <summary>Clicks a window owned by another thread, which Win32 capture treats as another application's.</summary>
    public override async Task ClickForeignWindowAsync()
    {
        // The bottom-right of the primary screen, away from the scenario windows along the top-left.
        _foreign ??= new Win32ForeignWindow(GetSystemMetrics(SM_CXSCREEN) - 260, GetSystemMetrics(SM_CYSCREEN) - 260, 220, 140);
        await Task.Delay(200);

        GetWindowRect(_foreign.Handle, out var frame);
        var center = new NativePoint { X = (frame.Left + frame.Right) / 2, Y = (frame.Top + frame.Bottom) / 2 };
        SetCursorPos(center.X, center.Y);
        await Task.Delay(SETTLE_MS);

        if (WindowFromPoint(center) != _foreign.Handle)
        {
            Assert.Inconclusive($"The stand-in for another application's window is covered at ({center.X},{center.Y}); the press was not sent.");
        }

        mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
        mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        await Task.Delay(300);
    }

    private static void EnsurePointerOverOwnWindow()
    {
        GetCursorPos(out var cursor);
        nint hit = WindowFromPoint(cursor);
        if (!IsOwnWindow(hit))
        {
            var className = new StringBuilder(128);
            GetClassName(hit, className, className.Capacity);
            GetWindowThreadProcessId(hit, out uint hitProcess);
            Assert.Inconclusive(
                $"The pointer at ({cursor.X},{cursor.Y}) is over window class '{className}' of process {hitProcess}; the press was not sent.");
        }
    }

    private static bool IsOwnWindow(nint hwnd)
    {
        if (hwnd == 0)
        {
            return false;
        }

        GetWindowThreadProcessId(hwnd, out uint processId);
        return processId == (uint)Environment.ProcessId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")] private static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] private static extern nint WindowFromPoint(NativePoint point);
    [DllImport("user32.dll")] private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nint extraInfo);
    [DllImport("user32.dll")] private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, nint extraInfo);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern nint GetCapture();
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint hwnd, out NativeRect rect);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint hwnd, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint hwnd, StringBuilder className, int capacity);
}

internal sealed class X11Input : RealInput
{
    private const uint LEFT_BUTTON = 1;
    private const nuint XK_SPACE = 0x20;

    private readonly nint _display = XOpenDisplay(0);
    // Windows of another client: a managed one that takes the focus, and an override-redirect one a click does not focus.
    private ForeignWindow? _focusTarget;
    private ForeignWindow? _clickTarget;

    public override async Task MoveAsync(Window window, Point client)
    {
        var screen = window.ClientToScreen(client);
        XTestFakeMotionEvent(_display, -1, (int)Math.Round(screen.X), (int)Math.Round(screen.Y), 0);
        XFlush(_display);
        await Task.Delay(SETTLE_MS);
    }

    public override async Task PressAsync(Window window, Point client)
    {
        await MoveAsync(window, client);
        XTestFakeButtonEvent(_display, LEFT_BUTTON, true, 0);
        XFlush(_display);
        IsButtonDown = true;
        await Task.Delay(SETTLE_MS);
    }

    public override async Task ReleaseAsync(Window window, Point client)
    {
        await MoveAsync(window, client);
        XTestFakeButtonEvent(_display, LEFT_BUTTON, false, 0);
        XFlush(_display);
        IsButtonDown = false;
        await Task.Delay(SETTLE_MS);
    }

    public override async Task SpaceAsync(Window window, bool isDown)
    {
        uint keycode = XKeysymToKeycode(_display, XK_SPACE);
        XTestFakeKeyEvent(_display, keycode, isDown, 0);
        XFlush(_display);
        await Task.Delay(SETTLE_MS);
    }

    public override async Task ActivateAsync(Window window)
    {
        // The focus target is raised over whatever it overlaps; remove it before a scenario presses again.
        _focusTarget?.Dispose();
        _focusTarget = null;
        window.Activate();
        await Task.Delay(400);
    }

    /// <summary>Gives the keyboard focus to a window of a different client, as another application taking it would.</summary>
    public override async Task DeactivateAsync(Window window, Window other)
    {
        _focusTarget ??= new ForeignWindow(overrideRedirect: false);
        await Task.Delay(300);
        _focusTarget.Focus();
        await Task.Delay(400);
    }

    /// <summary>Asks for a pointer grab from a second client: the server refuses while any client holds one.</summary>
    public override OsCaptureState OsCapture(Window window)
    {
        nint probe = XOpenDisplay(0);
        if (probe == 0)
        {
            return OsCaptureState.NotApplicable;
        }

        try
        {
            const uint MASK = 4 | 8 | 64;
            int status = XGrabPointer(probe, XDefaultRootWindow(probe), 0, MASK, 1, 1, 0, 0, 0);
            if (status == 0)
            {
                XUngrabPointer(probe, 0);
                XFlush(probe);
                return OsCaptureState.Free;
            }

            return OsCaptureState.Held;
        }
        finally
        {
            XCloseDisplay(probe);
        }
    }

    /// <summary>Clicks a window of a different client connection that the window manager does not focus on click.</summary>
    public override async Task ClickForeignWindowAsync()
    {
        _clickTarget ??= new ForeignWindow(overrideRedirect: true);
        await Task.Delay(300);
        var (x, y) = _clickTarget.Center();
        XTestFakeMotionEvent(_display, -1, x, y, 0);
        XTestFakeButtonEvent(_display, LEFT_BUTTON, true, 0);
        XTestFakeButtonEvent(_display, LEFT_BUTTON, false, 0);
        XFlush(_display);
        await Task.Delay(300);
    }

    private sealed class ForeignWindow : IDisposable
    {
        private const uint WIDTH = 220;
        private const uint HEIGHT = 140;
        private const nuint CW_OVERRIDE_REDIRECT = 1 << 9;

        private readonly nint _display = XOpenDisplay(0);
        private readonly nuint _window;

        /// <summary>Maps the window below the scenario windows, which sit side by side along the top of the screen.</summary>
        public ForeignWindow(bool overrideRedirect)
        {
            _window = XCreateSimpleWindow(_display, XDefaultRootWindow(_display), overrideRedirect ? 60 : 320, 520, WIDTH, HEIGHT, 1, 0, 0xFFFFFF);
            if (overrideRedirect)
            {
                var attributes = new XSetWindowAttributes { OverrideRedirect = 1 };
                XChangeWindowAttributes(_display, _window, CW_OVERRIDE_REDIRECT, ref attributes);
            }

            XMapRaised(_display, _window);
            XSync(_display, 0);
        }

        public (int X, int Y) Center()
        {
            XTranslateCoordinates(_display, _window, XDefaultRootWindow(_display), (int)WIDTH / 2, (int)HEIGHT / 2, out int x, out int y, out _);
            return (x, y);
        }

        public void Focus()
        {
            XSetInputFocus(_display, _window, 2, 0);
            XFlush(_display);
        }

        public void Dispose()
        {
            XDestroyWindow(_display, _window);
            XCloseDisplay(_display);
        }
    }

    [StructLayout(LayoutKind.Explicit, Size = 112)]
    private struct XSetWindowAttributes
    {
        [FieldOffset(88)] public int OverrideRedirect;
    }

    [DllImport("libX11.so.6")] private static extern nint XOpenDisplay(nint displayName);
    [DllImport("libX11.so.6")] private static extern int XCloseDisplay(nint display);
    [DllImport("libX11.so.6")] private static extern int XFlush(nint display);
    [DllImport("libX11.so.6")] private static extern int XSync(nint display, int discard);
    [DllImport("libX11.so.6")] private static extern nuint XDefaultRootWindow(nint display);
    [DllImport("libX11.so.6")] private static extern uint XKeysymToKeycode(nint display, nuint keysym);
    [DllImport("libX11.so.6")] private static extern int XGrabPointer(nint display, nuint grabWindow, int ownerEvents, uint eventMask, int pointerMode, int keyboardMode, nuint confineTo, nuint cursor, nuint time);
    [DllImport("libX11.so.6")] private static extern int XUngrabPointer(nint display, nuint time);
    [DllImport("libX11.so.6")] private static extern nuint XCreateSimpleWindow(nint display, nuint parent, int x, int y, uint width, uint height, uint borderWidth, nuint border, nuint background);
    [DllImport("libX11.so.6")] private static extern int XChangeWindowAttributes(nint display, nuint window, nuint valueMask, ref XSetWindowAttributes attributes);
    [DllImport("libX11.so.6")] private static extern int XMapRaised(nint display, nuint window);
    [DllImport("libX11.so.6")] private static extern int XTranslateCoordinates(nint display, nuint source, nuint destination, int sourceX, int sourceY, out int destinationX, out int destinationY, out nuint child);
    [DllImport("libX11.so.6")] private static extern int XSetInputFocus(nint display, nuint focus, int revertTo, nuint time);
    [DllImport("libX11.so.6")] private static extern int XDestroyWindow(nint display, nuint window);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeMotionEvent(nint display, int screenNumber, int x, int y, nuint delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeButtonEvent(nint display, uint button, bool isPress, nuint delay);
    [DllImport("libXtst.so.6")] private static extern int XTestFakeKeyEvent(nint display, uint keycode, bool isPress, nuint delay);
}

[StructLayout(LayoutKind.Sequential)]
internal struct NSPoint
{
    public double X;
    public double Y;

    public NSPoint(double x, double y)
    {
        X = x;
        Y = y;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct NSRect
{
    public double X;
    public double Y;
    public double Width;
    public double Height;
}

internal sealed class MacInput : RealInput
{
    private const string OBJC = "/usr/lib/libobjc.A.dylib";
    private const string CORE_GRAPHICS = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const nuint LEFT_MOUSE_DOWN = 1;
    private const nuint LEFT_MOUSE_UP = 2;
    private const nuint MOUSE_MOVED = 5;
    private const nuint LEFT_MOUSE_DRAGGED = 6;
    private const nuint KEY_DOWN = 10;
    private const nuint KEY_UP = 11;
    private const ushort KVK_SPACE = 49;

    private Window? _keyWindow;

    public override async Task MoveAsync(Window window, Point client)
    {
        WarpCursor(window, client);
        PostMouse(window, IsButtonDown ? LEFT_MOUSE_DRAGGED : MOUSE_MOVED, client, IsButtonDown ? 1 : 0);
        await Task.Delay(SETTLE_MS);
    }

    public override async Task PressAsync(Window window, Point client)
    {
        await MoveAsync(window, client);
        PostMouse(window, LEFT_MOUSE_DOWN, client, 1);
        IsButtonDown = true;
        await Task.Delay(SETTLE_MS);
    }

    public override async Task ReleaseAsync(Window window, Point client)
    {
        await MoveAsync(window, client);
        PostMouse(window, LEFT_MOUSE_UP, client, 1);
        IsButtonDown = false;
        await Task.Delay(SETTLE_MS);
    }

    public override async Task SpaceAsync(Window window, bool isDown)
    {
        nint characters = SendString(objc_getClass("NSString"), Sel("stringWithUTF8String:"), " ");
        nint ev = SendKeyEvent(
            objc_getClass("NSEvent"),
            Sel("keyEventWithType:location:modifierFlags:timestamp:windowNumber:context:characters:charactersIgnoringModifiers:isARepeat:keyCode:"),
            isDown ? KEY_DOWN : KEY_UP, new NSPoint(0, 0), 0, Uptime(), WindowNumber(window), 0, characters, characters, false, KVK_SPACE);
        SendPost(App(), Sel("postEvent:atStart:"), ev, false);
        await Task.Delay(SETTLE_MS);
    }

    /// <summary>
    /// A process started over SSH is never given key status by the window server, so key changes are
    /// delivered through the window delegate the backend installs, the same entry AppKit calls.
    /// </summary>
    public override async Task ActivateAsync(Window window)
    {
        if (_keyWindow != null && !ReferenceEquals(_keyWindow, window) && _keyWindow.Handle != 0)
        {
            SendDelegate(_keyWindow, "windowDidResignKey:");
        }

        SendDelegate(window, "windowDidBecomeKey:");
        _keyWindow = window;
        await Task.Delay(200);
    }

    public override async Task DeactivateAsync(Window window, Window other)
    {
        SendDelegate(window, "windowDidResignKey:");
        SendDelegate(other, "windowDidBecomeKey:");
        _keyWindow = other;
        await Task.Delay(200);
    }

    public override OsCaptureState OsCapture(Window window) => OsCaptureState.NotApplicable;

    private void PostMouse(Window window, nuint type, Point client, nint clickCount)
    {
        double contentHeight = window.ClientSize.Height;
        nint ev = SendMouseEvent(
            objc_getClass("NSEvent"),
            Sel("mouseEventWithType:location:modifierFlags:timestamp:windowNumber:context:eventNumber:clickCount:pressure:"),
            type, new NSPoint(client.X, contentHeight - client.Y), 0, Uptime(), WindowNumber(window), 0, 0, clickCount,
            type is LEFT_MOUSE_DOWN or LEFT_MOUSE_DRAGGED ? 1f : 0f);
        SendPost(App(), Sel("postEvent:atStart:"), ev, false);
    }

    /// <summary>
    /// Posted events leave the system cursor where it is, and the framework probes the cursor (drop target
    /// resolution), so the cursor is moved to the same point without generating an event of its own.
    /// </summary>
    private static void WarpCursor(Window window, Point client)
    {
        nint nsWindow = NsWindow(window);
        var cocoa = SendConvertPoint(nsWindow, Sel("convertPointToScreen:"), new NSPoint(client.X, window.ClientSize.Height - client.Y));
        nint screens = Send(objc_getClass("NSScreen"), Sel("screens"));
        nint primary = SendIndex(screens, Sel("objectAtIndex:"), 0);
        var primaryFrame = SendRect(primary, Sel("frame"));
        CGWarpMouseCursorPosition(new NSPoint(cocoa.X, primaryFrame.Height - cocoa.Y));
    }

    private static void SendDelegate(Window window, string selector)
    {
        nint nsWindow = NsWindow(window);
        nint windowDelegate = Send(nsWindow, Sel("delegate"));
        if (windowDelegate == 0)
        {
            return;
        }

        // The delegate reads the window from the notification object, as it does for AppKit's own.
        nint name = SendString(objc_getClass("NSString"), Sel("stringWithUTF8String:"), selector);
        nint notification = SendTwoArgs(objc_getClass("NSNotification"), Sel("notificationWithName:object:"), name, nsWindow);
        SendVoidArg(windowDelegate, Sel(selector), notification);
    }

    private static nint NsWindow(Window window)
    {
        var backend = typeof(Window).GetProperty("Backend", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        return (nint)backend.GetType().GetField("_nsWindow", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(backend)!;
    }

    private static nint WindowNumber(Window window) => Send(NsWindow(window), Sel("windowNumber"));

    private static double Uptime() => SendDouble(Send(objc_getClass("NSProcessInfo"), Sel("processInfo")), Sel("systemUptime"));

    private static nint App() => Send(objc_getClass("NSApplication"), Sel("sharedApplication"));

    private static nint Sel(string name) => sel_registerName(name);

    [DllImport(OBJC)] private static extern nint objc_getClass(string name);
    [DllImport(OBJC)] private static extern nint sel_registerName(string name);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint receiver, nint selector);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern double SendDouble(nint receiver, nint selector);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern void SendVoidArg(nint receiver, nint selector, nint argument);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern nint SendTwoArgs(nint receiver, nint selector, nint first, nint second);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern nint SendIndex(nint receiver, nint selector, nuint index);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern NSPoint SendConvertPoint(nint receiver, nint selector, NSPoint point);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern NSRect SendRect(nint receiver, nint selector);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern void SendPost(nint receiver, nint selector, nint ev, [MarshalAs(UnmanagedType.I1)] bool atStart);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern nint SendString(nint receiver, nint selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string value);
    [DllImport(CORE_GRAPHICS)] private static extern int CGWarpMouseCursorPosition(NSPoint point);

    [DllImport(OBJC, EntryPoint = "objc_msgSend")]
    private static extern nint SendMouseEvent(nint receiver, nint selector, nuint type, NSPoint location, nuint flags,
        double timestamp, nint windowNumber, nint context, nint eventNumber, nint clickCount, float pressure);

    [DllImport(OBJC, EntryPoint = "objc_msgSend")]
    private static extern nint SendKeyEvent(nint receiver, nint selector, nuint type, NSPoint location, nuint flags,
        double timestamp, nint windowNumber, nint context, nint characters, nint charactersIgnoringModifiers,
        [MarshalAs(UnmanagedType.I1)] bool isARepeat, ushort keyCode);
}
