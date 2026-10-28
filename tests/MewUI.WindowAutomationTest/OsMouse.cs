using System.Runtime.InteropServices;

using Aprillz.MewUI;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Moves the system pointer and presses its left button at screen device pixels, the way a user's mouse
/// would: SetCursorPos and mouse_event on Windows, the XTEST extension on X11, and CGEventPost on macOS.
/// </summary>
internal static class OsMouse
{
    private const int MOVE_SETTLE_MS = 60;
    private const int BUTTON_SETTLE_MS = 80;

    private static nint _x11Display;
    private static string? _unavailableReason;
    private static bool _initialized;
    private static bool _buttonDown;

    /// <summary>Whether this session can inject pointer input; the reason says why not.</summary>
    public static bool TryInitialize(out string reason)
    {
        if (!_initialized)
        {
            _initialized = true;
            _unavailableReason = Probe();
        }

        reason = _unavailableReason ?? string.Empty;
        return _unavailableReason == null;
    }

    public static async Task MoveAsync(Point screenPx)
    {
        Move(screenPx, _buttonDown);
        await Task.Delay(MOVE_SETTLE_MS);
    }

    public static async Task ClickAsync(Point screenPx)
    {
        await MoveAsync(screenPx);
        await PressAsync(screenPx);
        await ReleaseAsync(screenPx);
    }

    public static async Task PressAsync(Point screenPx)
    {
        Button(screenPx, press: true);
        await Task.Delay(BUTTON_SETTLE_MS);
    }

    public static async Task ReleaseAsync(Point screenPx)
    {
        Button(screenPx, press: false);
        await Task.Delay(BUTTON_SETTLE_MS);
    }

    /// <summary>Presses at <paramref name="fromPx"/>, moves to <paramref name="toPx"/> in even steps with the button held, and releases there.</summary>
    public static async Task DragAsync(Point fromPx, Point toPx, int steps)
    {
        await MoveAsync(fromPx);
        await PressAsync(fromPx);
        for (int step = 1; step <= steps; step++)
        {
            double fraction = (double)step / steps;
            await MoveAsync(new Point(fromPx.X + (toPx.X - fromPx.X) * fraction, fromPx.Y + (toPx.Y - fromPx.Y) * fraction));
        }

        await ReleaseAsync(toPx);
    }

    /// <summary>Lets go of a button a failed test left held, so the next test starts with the button up.</summary>
    public static void ReleaseIfHeld()
    {
        if (_buttonDown && OperatingSystem.IsWindows())
        {
            Win32.mouse_event(Win32.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
            _buttonDown = false;
        }
        else if (_buttonDown && OperatingSystem.IsLinux() && _x11Display != 0)
        {
            _ = X11.XTestFakeButtonEvent(_x11Display, 1, 0, 0);
            _ = X11.XFlush(_x11Display);
            _buttonDown = false;
        }
    }

    private static string? Probe()
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        if (OperatingSystem.IsLinux())
        {
            try
            {
                _x11Display = X11.XOpenDisplay(0);
                if (_x11Display == 0)
                {
                    return "No X display could be opened to inject pointer input.";
                }

                if (X11.XTestQueryExtension(_x11Display, out _, out _, out _, out _) == 0)
                {
                    return "The X server has no XTEST extension.";
                }

                return null;
            }
            catch (DllNotFoundException failure)
            {
                return $"libXtst is not installed: {failure.Message}";
            }
        }

        if (OperatingSystem.IsMacOS())
        {
            // Posting events needs the Accessibility grant of the process that launched the runner; without it they are dropped silently.
            return MacOS.CGPreflightPostEventAccess()
                ? null
                : "This process may not post input events (System Settings > Privacy & Security > Accessibility).";
        }

        return "Pointer injection runs on Windows, X11 and macOS.";
    }

    private static void Move(Point screenPx, bool withButtonDown)
    {
        int pixelX = (int)Math.Round(screenPx.X);
        int pixelY = (int)Math.Round(screenPx.Y);
        if (OperatingSystem.IsWindows())
        {
            Win32.SetCursorPos(pixelX, pixelY);
        }
        else if (OperatingSystem.IsLinux())
        {
            _ = X11.XTestFakeMotionEvent(_x11Display, -1, pixelX, pixelY, 0);
            _ = X11.XFlush(_x11Display);
        }
        else
        {
            MacOS.Post(withButtonDown ? MacOS.LEFT_MOUSE_DRAGGED : MacOS.MOUSE_MOVED, screenPx);
        }
    }

    private static void Button(Point screenPx, bool press)
    {
        _buttonDown = press;
        if (OperatingSystem.IsWindows())
        {
            Win32.mouse_event(press ? Win32.MOUSEEVENTF_LEFTDOWN : Win32.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
        }
        else if (OperatingSystem.IsLinux())
        {
            _ = X11.XTestFakeButtonEvent(_x11Display, 1, press ? 1 : 0, 0);
            _ = X11.XFlush(_x11Display);
        }
        else
        {
            MacOS.Post(press ? MacOS.LEFT_MOUSE_DOWN : MacOS.LEFT_MOUSE_UP, screenPx);
        }
    }

    private static class Win32
    {
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nint extraInfo);
    }

    private static class X11
    {
        [DllImport("libX11.so.6")] public static extern nint XOpenDisplay(nint displayName);
        [DllImport("libX11.so.6")] public static extern int XFlush(nint display);
        [DllImport("libXtst.so.6")]
        public static extern int XTestQueryExtension(nint display, out int eventBase, out int errorBase, out int major, out int minor);
        [DllImport("libXtst.so.6")] public static extern int XTestFakeMotionEvent(nint display, int screen, int x, int y, nuint delay);
        [DllImport("libXtst.so.6")] public static extern int XTestFakeButtonEvent(nint display, uint button, int isPress, nuint delay);
    }

    private static class MacOS
    {
        public const int LEFT_MOUSE_DOWN = 1;
        public const int LEFT_MOUSE_UP = 2;
        public const int MOUSE_MOVED = 5;
        public const int LEFT_MOUSE_DRAGGED = 6;

        private const int HID_EVENT_TAP = 0;
        private const int LEFT_BUTTON = 0;
        private const string CORE_GRAPHICS = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
        private const string CORE_FOUNDATION = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

        [StructLayout(LayoutKind.Sequential)]
        private struct CGPoint
        {
            public double X;
            public double Y;
        }

        /// <summary>Posts one pointer event; screen pixels become the points of the global display space (top-left origin).</summary>
        public static void Post(int eventType, Point screenPx)
        {
            double scale = MacScreens.ReferenceScale();
            var position = new CGPoint { X = screenPx.X / scale, Y = screenPx.Y / scale };
            nint mouseEvent = CGEventCreateMouseEvent(0, eventType, position, LEFT_BUTTON);
            if (mouseEvent == 0)
            {
                Assert.Fail($"CGEventCreateMouseEvent failed for event type {eventType}.");
            }

            CGEventPost(HID_EVENT_TAP, mouseEvent);
            CFRelease(mouseEvent);
        }

        [DllImport(CORE_GRAPHICS)]
        [return: MarshalAs(UnmanagedType.U1)]
        public static extern bool CGPreflightPostEventAccess();

        [DllImport(CORE_GRAPHICS)]
        private static extern nint CGEventCreateMouseEvent(nint source, int mouseType, CGPoint position, int mouseButton);

        [DllImport(CORE_GRAPHICS)]
        private static extern void CGEventPost(int tap, nint cgEvent);

        [DllImport(CORE_FOUNDATION)]
        private static extern void CFRelease(nint cfObject);
    }
}

/// <summary>
/// Writes a measurement line to standard output and to measure.log next to the test assembly, because the
/// test runner keeps the output of passing tests out of its console report.
/// </summary>
internal static class MeasureLog
{
    private static readonly string _path = Path.Combine(AppContext.BaseDirectory, "measure.log");

    public static void Write(string message)
    {
        Console.WriteLine(message);
        File.AppendAllText(_path, message + Environment.NewLine);
    }
}

/// <summary>Reads NSScreen geometry the way the platform layer's screen-pixel contract describes it, without going through MewUI.</summary>
internal static class MacScreens
{
    private const string OBJC = "/usr/lib/libobjc.dylib";

    [StructLayout(LayoutKind.Sequential)]
    public struct NSRect
    {
        public double X;
        public double Y;
        public double Width;
        public double Height;
    }

    public static IReadOnlyList<nint> All()
    {
        nint screens = Send(objc_getClass("NSScreen"), sel_registerName("screens"));
        nuint count = screens == 0 ? 0 : SendUInt(screens, sel_registerName("count"));
        var result = new List<nint>();
        for (nuint index = 0; index < count; index++)
        {
            result.Add(SendIndex(screens, sel_registerName("objectAtIndex:"), index));
        }

        return result;
    }

    /// <summary>The backing scale of the first screen, the one whose top-left is the global origin.</summary>
    public static double ReferenceScale()
    {
        var screens = All();
        double scale = screens.Count == 0 ? 1.0 : SendDouble(screens[0], sel_registerName("backingScaleFactor"));
        return scale > 0 ? scale : 1.0;
    }

    public static NSRect Frame(nint screen) => SendRect(screen, sel_registerName("frame"));

    public static NSRect VisibleFrame(nint screen) => SendRect(screen, sel_registerName("visibleFrame"));

    /// <summary>The frame of the window that hosts the given view, in Cocoa points.</summary>
    public static NSRect WindowFrameOfView(nint view)
    {
        nint window = view == 0 ? 0 : Send(view, sel_registerName("window"));
        return window == 0 ? default : SendRect(window, sel_registerName("frame"));
    }

    /// <summary>A Cocoa rectangle (points, bottom-left origin) as a screen-pixel rectangle (top-left origin, Y down).</summary>
    public static Rect ToScreenPixels(NSRect cocoa)
    {
        var screens = All();
        var reference = screens.Count == 0 ? default : Frame(screens[0]);
        double scale = ReferenceScale();
        double top = reference.Y + reference.Height - (cocoa.Y + cocoa.Height);
        return new Rect(cocoa.X * scale, top * scale, cocoa.Width * scale, cocoa.Height * scale);
    }

    [DllImport(OBJC)] private static extern nint objc_getClass(string name);
    [DllImport(OBJC)] private static extern nint sel_registerName(string name);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint receiver, nint selector);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern nuint SendUInt(nint receiver, nint selector);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern nint SendIndex(nint receiver, nint selector, nuint index);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern double SendDouble(nint receiver, nint selector);
    // An all-double struct comes back in registers on arm64, so plain objc_msgSend returns it there.
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern NSRect SendRect(nint receiver, nint selector);
}
