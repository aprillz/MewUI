using System.Reflection;
using System.Runtime.InteropServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A window hidden after it was shown comes back where it was when it is shown again (issue #314).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowHideShowTests
{
    private const int SETTLE_MS = 600;
    private const int POSITION_TOLERANCE_PX = 2;

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task OnX11_ShowAfterHide_MapsTheWindowAgainWhereItWas()
    {
        Assert.IsTrue(RealAppSession.IsAvailable, "the application loop did not start on this platform");

        await RealAppSession.RunAsync(async () =>
        {
            var window = CreateWindow("HideShowX11");
            nint display = X11.XOpenDisplay(0);
            Assert.AreNotEqual((nint)0, display, "a second X connection could not be opened");

            try
            {
                window.Show();
                window.MoveTo(300, 200);
                await Task.Delay(SETTLE_MS);
                var handle = (nuint)window.Handle;
                Assert.AreEqual(X11.IS_VIEWABLE, X11.MapState(display, handle), "the window was not shown");
                var shownAt = X11.RootPosition(display, handle);

                window.Hide();
                await Task.Delay(SETTLE_MS);
                Assert.AreEqual(X11.IS_UNMAPPED, X11.MapState(display, handle), "Hide left the window mapped");

                window.Show();
                await Task.Delay(SETTLE_MS);
                Assert.AreEqual(X11.IS_VIEWABLE, X11.MapState(display, handle), "Show after Hide left the window unmapped");
                var shownAgainAt = X11.RootPosition(display, handle);
                Assert.IsTrue(Math.Abs(shownAt.X - shownAgainAt.X) <= POSITION_TOLERANCE_PX
                    && Math.Abs(shownAt.Y - shownAgainAt.Y) <= POSITION_TOLERANCE_PX,
                    $"the window came back at {shownAgainAt} instead of {shownAt}");
            }
            finally
            {
                _ = X11.XCloseDisplay(display);
                window.Close();
            }
        });
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task OnX11_HidingAMinimizedWindow_WithdrawsIt_AndShowMinimizesItAgain()
    {
        Assert.IsTrue(RealAppSession.IsAvailable, "the application loop did not start on this platform");

        await RealAppSession.RunAsync(async () =>
        {
            var window = CreateWindow("HideShowMinimizedX11");
            nint display = X11.XOpenDisplay(0);
            Assert.AreNotEqual((nint)0, display, "a second X connection could not be opened");

            try
            {
                window.Show();
                window.MoveTo(300, 200);
                await Task.Delay(SETTLE_MS);
                var handle = (nuint)window.Handle;
                window.WindowState = WindowState.Minimized;
                await Task.Delay(SETTLE_MS);
                if (X11.WmState(display, handle) != X11.ICONIC_STATE)
                {
                    Assert.Inconclusive("the window manager did not iconify the window");
                }

                window.Hide();
                await Task.Delay(SETTLE_MS);
                var hiddenState = X11.WmState(display, handle);
                Assert.IsTrue(hiddenState is X11.WITHDRAWN_STATE or X11.NO_WM_STATE,
                    $"Hide left the minimized window in WM_STATE {hiddenState}");
                Assert.AreEqual(WindowState.Minimized, window.WindowState, "the hidden window lost its state");

                window.Show();
                await Task.Delay(SETTLE_MS);
                Assert.AreEqual(X11.ICONIC_STATE, X11.WmState(display, handle), "Show did not bring the window back minimized");
            }
            finally
            {
                _ = X11.XCloseDisplay(display);
                window.Close();
            }
        });
    }

    [TestMethod]
    [OSCondition(OperatingSystems.OSX)]
    public async Task OnMacOS_ShowAfterHide_OrdersTheWindowInAgainWhereItWas()
    {
        if (!RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real macOS application loop.");
        }

        await RealAppSession.RunAsync(async () =>
        {
            var window = CreateWindow("HideShowMac");
            bool closed = false;
            window.Closed += () => closed = true;
            try
            {
                window.Show();
                window.MoveTo(300, 200);
                await Task.Delay(SETTLE_MS);
                nint nsWindow = MacOS.NsWindow(window);
                Assert.IsTrue(MacOS.IsVisible(nsWindow), "the window was not shown");
                var shownAt = window.Position;

                window.Hide();
                await Task.Delay(SETTLE_MS);
                Assert.IsFalse(closed, "Hide closed the window");
                Assert.AreEqual(nsWindow, MacOS.NsWindow(window), "Hide released the native window");
                Assert.IsFalse(MacOS.IsVisible(nsWindow), "Hide left the window on screen");

                window.Show();
                await Task.Delay(SETTLE_MS);
                Assert.IsTrue(MacOS.IsVisible(nsWindow), "Show after Hide left the window off screen");
                Assert.AreEqual(shownAt, window.Position, "the window came back somewhere else");

                window.Hide();
                await Task.Delay(SETTLE_MS);
                window.Close();
                await Task.Delay(SETTLE_MS);
                Assert.IsTrue(closed, "closing the hidden window did not raise Closed");
                Assert.AreEqual((nint)0, MacOS.NsWindow(window), "the window closed while hidden was not released");
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static Window CreateWindow(string title) => new()
    {
        Title = title,
        StartupLocation = WindowStartupLocation.Manual,
        WindowSize = WindowSize.Fixed(320, 200),
        Content = new TextBlock { Text = title },
    };

    private static class X11
    {
        public const int IS_UNMAPPED = 0;
        public const int IS_VIEWABLE = 2;
        public const long NO_WM_STATE = -1;
        public const long WITHDRAWN_STATE = 0;
        public const long ICONIC_STATE = 3;

        public static int MapState(nint display, nuint window)
        {
            Assert.AreNotEqual(0, XGetWindowAttributes(display, window, out var attributes), "the window attributes could not be read");
            return attributes.map_state;
        }

        public static Point RootPosition(nint display, nuint window)
        {
            _ = XTranslateCoordinates(display, window, XDefaultRootWindow(display), 0, 0, out int rootX, out int rootY, out _);
            return new Point(rootX, rootY);
        }

        // The first field of WM_STATE, or NO_WM_STATE when the window does not carry it.
        public static long WmState(nint display, nuint window)
        {
            nint property = XInternAtom(display, "WM_STATE", 0);
            int status = XGetWindowProperty(display, window, property, 0, 2, 0, property,
                out _, out _, out nuint count, out _, out nint values);
            if (status != 0 || values == 0)
            {
                return NO_WM_STATE;
            }

            try
            {
                return count == 0 ? NO_WM_STATE : Marshal.ReadIntPtr(values);
            }
            finally
            {
                _ = XFree(values);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct XWindowAttributes
        {
            public int x, y;
            public int width, height;
            public int border_width;
            public int depth;
            public nint visual;
            public nint root;
            public int @class;
            public int bit_gravity;
            public int win_gravity;
            public int backing_store;
            public ulong backing_planes;
            public ulong backing_pixel;
            public int save_under;
            public nint colormap;
            public int map_installed;
            public int map_state;
            public long all_event_masks;
            public long your_event_mask;
            public long do_not_propagate_mask;
            public int override_redirect;
            public nint screen;
        }

        [DllImport("libX11.so.6")] public static extern nint XOpenDisplay(nint displayName);
        [DllImport("libX11.so.6")] public static extern int XCloseDisplay(nint display);
        [DllImport("libX11.so.6")] public static extern nuint XDefaultRootWindow(nint display);
        [DllImport("libX11.so.6")] public static extern nint XInternAtom(nint display, string name, int onlyIfExists);
        [DllImport("libX11.so.6")] public static extern int XFree(nint data);
        [DllImport("libX11.so.6")] public static extern int XGetWindowAttributes(nint display, nuint window, out XWindowAttributes attributes);
        [DllImport("libX11.so.6")]
        public static extern int XTranslateCoordinates(nint display, nuint source, nuint destination, int sourceX, int sourceY,
            out int destinationX, out int destinationY, out nuint child);
        [DllImport("libX11.so.6")]
        public static extern int XGetWindowProperty(nint display, nuint window, nint property, long offset, long length,
            int delete, nint requestedType, out nint actualType, out int actualFormat,
            out nuint itemCount, out nuint bytesAfter, out nint values);
    }

    private static class MacOS
    {
        private const string OBJC = "/usr/lib/libobjc.A.dylib";

        public static nint NsWindow(Window window)
        {
            var backend = typeof(Window).GetProperty("Backend", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
            return (nint)backend.GetType().GetField("_nsWindow", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(backend)!;
        }

        public static bool IsVisible(nint nsWindow) => SendBool(nsWindow, sel_registerName("isVisible"));

        [DllImport(OBJC)] private static extern nint sel_registerName(string name);
        [DllImport(OBJC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SendBool(nint receiver, nint selector);
    }
}
