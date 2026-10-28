using System.Runtime.InteropServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// <see cref="ScreenHelper.GetWorkAreaForPoint"/> and <see cref="Window.MoveToScreenPixels"/> against what the
/// system itself reports: every platform has to use screen device pixels, origin top-left, Y down.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ScreenPixelTests
{
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task OnWindows_WorkAreaMatchesTheMonitorWorkArea()
    {
        RequireLoop();
        await RealAppSession.RunAsync(() =>
        {
            Assert.IsNotEmpty(MonitorMatrix.Monitors, "no monitors to check");
            foreach (var monitor in MonitorMatrix.Monitors)
            {
                var center = new Win32.POINT { X = monitor.PixelBounds.CenterX, Y = monitor.PixelBounds.CenterY };
                var info = Win32.MonitorInfoAt(center);
                var actual = ScreenHelper.GetWorkAreaForPoint(new Point(center.X, center.Y));
                Log($"{monitor.Label} monitor={Describe(info.rcMonitor)} OS work={Describe(info.rcWork)} ScreenHelper={actual}");
                AssertRect(info.rcWork, actual, monitor.Label);
            }

            var primaryWork = new Win32.RECT();
            Assert.IsTrue(Win32.SystemParametersInfo(Win32.SPI_GETWORKAREA, 0, ref primaryWork, 0), "SPI_GETWORKAREA failed");
            var primary = MonitorMatrix.Monitors.First(static monitor => monitor.IsPrimary);
            var primaryActual = ScreenHelper.GetWorkAreaForPoint(new Point(primary.PixelBounds.CenterX, primary.PixelBounds.CenterY));
            Log($"SPI_GETWORKAREA={Describe(primaryWork)} ScreenHelper(primary)={primaryActual}");
            AssertRect(primaryWork, primaryActual, "primary via SPI_GETWORKAREA");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task OnWindows_MoveToScreenPixelsPutsTheWindowRectOnThePixel()
    {
        RequireLoop();
        await RealAppSession.RunAsync(async () =>
        {
            var window = NewWindow();
            try
            {
                window.Show();
                await Task.Delay(300);
                // Primary first, so each later monitor is reached by a move that crosses to it.
                foreach (var monitor in MonitorMatrix.Monitors.OrderByDescending(static probe => probe.IsPrimary))
                {
                    var work = ScreenHelper.GetWorkAreaForPoint(new Point(monitor.PixelBounds.CenterX, monitor.PixelBounds.CenterY));
                    int left = (int)work.X + 37;
                    int top = (int)work.Y + 23;
                    window.MoveToScreenPixels(left, top);
                    await Task.Delay(400);
                    Win32.GetWindowRect(window.Handle, out var rect);
                    Log($"{monitor.Label} target=({left}, {top}) GetWindowRect={Describe(rect)} window.Dpi={window.Dpi}");
                    Assert.AreEqual(left, rect.Left, $"left on {monitor.Label}");
                    Assert.AreEqual(top, rect.Top, $"top on {monitor.Label}");
                    Assert.AreEqual(monitor.Dpi, window.Dpi, $"the window took the scale of {monitor.Label}");

                    // Bottom-right corner placement, the case a caller fitting a window into the work area computes in pixels.
                    int right = (int)work.Right - 11;
                    int bottom = (int)work.Bottom - 7;
                    window.MoveToScreenPixels(right - rect.Width, bottom - rect.Height);
                    await Task.Delay(400);
                    Win32.GetWindowRect(window.Handle, out var corner);
                    Log($"{monitor.Label} bottom-right target=({right}, {bottom}) GetWindowRect={Describe(corner)}");
                    Assert.AreEqual(right, corner.Right, $"right on {monitor.Label}");
                    Assert.AreEqual(bottom, corner.Bottom, $"bottom on {monitor.Label}");
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task OnX11_WorkAreaMatchesTheDesktopWorkArea()
    {
        RequireLoop();
        await RealAppSession.RunAsync(() =>
        {
            using var display = new X11.Connection();
            var (rootWidth, rootHeight) = display.RootSize();
            var workAreas = display.RootCardinals("_NET_WORKAREA");
            var desktops = display.RootCardinals("_NET_CURRENT_DESKTOP");
            int desktop = desktops.Length > 0 ? (int)desktops[0] : 0;
            Assert.IsGreaterThanOrEqualTo(desktop * 4 + 4, workAreas.Length, "the window manager publishes no _NET_WORKAREA");

            var expected = new Rect(workAreas[desktop * 4], workAreas[desktop * 4 + 1], workAreas[desktop * 4 + 2], workAreas[desktop * 4 + 3]);
            var actual = ScreenHelper.GetWorkAreaForPoint(new Point(rootWidth / 2, rootHeight / 2));
            Log($"root={rootWidth}x{rootHeight} desktop={desktop} _NET_WORKAREA={expected} ScreenHelper={actual}");
            Assert.AreEqual(expected, actual, "the work area at the root center");
            return Task.CompletedTask;
        });
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task OnX11_MoveToScreenPixelsPutsTheFrameOnThePixel()
    {
        RequireLoop();
        await RealAppSession.RunAsync(async () =>
        {
            using var display = new X11.Connection();
            var window = NewWindow();
            try
            {
                window.Show();
                await Task.Delay(500);
                var work = ScreenHelper.GetWorkAreaForPoint(new Point(200, 200));
                int left = (int)work.X + 37;
                int top = (int)work.Y + 23;
                window.MoveToScreenPixels(left, top);
                await Task.Delay(600);

                var client = display.RootPositionOf((nuint)window.Handle);
                var extents = display.WindowCardinals((nuint)window.Handle, "_NET_FRAME_EXTENTS");
                long frameLeft = extents.Length >= 4 ? extents[0] : 0;
                long frameTop = extents.Length >= 4 ? extents[2] : 0;
                var outer = (X: client.X - frameLeft, Y: client.Y - frameTop);
                Log($"work={work} target=({left}, {top}) client origin=({client.X}, {client.Y}) _NET_FRAME_EXTENTS=[{string.Join(", ", extents)}] outer=({outer.X}, {outer.Y}) ClientToScreen(0,0)={window.ClientToScreen(new Point(0, 0))}");
                Assert.AreEqual(left, outer.X, "outer left");
                Assert.AreEqual(top, outer.Y, "outer top");
            }
            finally
            {
                window.Close();
            }
        });
    }

    [TestMethod]
    [OSCondition(OperatingSystems.OSX)]
    public async Task OnMacOS_WorkAreaMatchesTheVisibleFrame()
    {
        RequireLoop();
        await RealAppSession.RunAsync(() =>
        {
            var screens = MacScreens.All();
            Assert.IsNotEmpty(screens, "NSScreen reports no screens");
            foreach (nint screen in screens)
            {
                var frame = MacScreens.ToScreenPixels(MacScreens.Frame(screen));
                var expected = MacScreens.ToScreenPixels(MacScreens.VisibleFrame(screen));
                var actual = ScreenHelper.GetWorkAreaForPoint(new Point(frame.X + frame.Width / 2, frame.Y + frame.Height / 2));
                Log($"screen frame px={frame} visibleFrame px={expected} ScreenHelper={actual} reference scale={MacScreens.ReferenceScale()}");
                Assert.AreEqual(expected.X, actual.X, 0.5, "left");
                Assert.AreEqual(expected.Y, actual.Y, 0.5, "top");
                Assert.AreEqual(expected.Width, actual.Width, 0.5, "width");
                Assert.AreEqual(expected.Height, actual.Height, 0.5, "height");
            }

            return Task.CompletedTask;
        });
    }

    [TestMethod]
    [OSCondition(OperatingSystems.OSX)]
    public async Task OnMacOS_MoveToScreenPixelsPutsTheFrameOnThePixel()
    {
        RequireLoop();
        await RealAppSession.RunAsync(async () =>
        {
            var window = NewWindow();
            try
            {
                window.Show();
                await Task.Delay(500);
                var work = ScreenHelper.GetWorkAreaForPoint(new Point(200, 200));
                // Even pixels keep the target on whole points at a 2x backing scale.
                int left = (int)work.X + 38;
                int top = (int)work.Y + 24;
                window.MoveToScreenPixels(left, top);
                await Task.Delay(500);
                var outer = MacScreens.ToScreenPixels(MacScreens.WindowFrameOfView(window.Handle));
                Log($"work={work} target=({left}, {top}) NSWindow frame px={outer} ClientToScreen(0,0)={window.ClientToScreen(new Point(0, 0))}");
                Assert.AreEqual(left, outer.X, 1.0, "outer left");
                Assert.AreEqual(top, outer.Y, 1.0, "outer top");
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void RequireLoop()
    {
        if (!RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("The application loop did not start on this platform.");
        }
    }

    private static Window NewWindow()
        => new()
        {
            Title = "ScreenPixelMove",
            StartupLocation = WindowStartupLocation.Manual,
            WindowSize = WindowSize.Fixed(320, 200),
            Content = new TextBlock { Text = "pixel move" },
        };

    private static void AssertRect(Win32.RECT expected, Rect actual, string label)
    {
        Assert.AreEqual(expected.Left, actual.X, $"left of {label}");
        Assert.AreEqual(expected.Top, actual.Y, $"top of {label}");
        Assert.AreEqual(expected.Right - expected.Left, actual.Width, $"width of {label}");
        Assert.AreEqual(expected.Bottom - expected.Top, actual.Height, $"height of {label}");
    }

    private static string Describe(Win32.RECT rect) => $"[{rect.Left}, {rect.Top}, {rect.Right}, {rect.Bottom}]";

    private static void Log(string message) => MeasureLog.Write($"[ScreenPixel] {message}");

    private static class Win32
    {
        public const uint SPI_GETWORKAREA = 0x0030;
        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;

            public readonly int Width => Right - Left;
            public readonly int Height => Bottom - Top;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
        }

        public static MONITORINFO MonitorInfoAt(POINT point)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            Assert.IsTrue(GetMonitorInfo(MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST), ref info), "GetMonitorInfo failed");
            return info;
        }

        [DllImport("user32.dll")]
        private static extern nint MonitorFromPoint(POINT point, uint flags);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
        private static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

        [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
        public static extern bool SystemParametersInfo(uint action, uint param, ref RECT value, uint winIni);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(nint hwnd, out RECT rect);
    }

    private static class X11
    {
        private const string LIB = "libX11.so.6";
        private const nint ANY_PROPERTY_TYPE = 0;

        /// <summary>A second connection to the display, so the checks read the server's state rather than the framework's.</summary>
        public sealed class Connection : IDisposable
        {
            private readonly nint _display;

            public Connection()
            {
                _display = XOpenDisplay(0);
                Assert.AreNotEqual((nint)0, _display, "a second X connection could not be opened");
            }

            private nuint Root => XDefaultRootWindow(_display);

            public (int Width, int Height) RootSize()
            {
                int screen = XDefaultScreen(_display);
                return (XDisplayWidth(_display, screen), XDisplayHeight(_display, screen));
            }

            public long[] RootCardinals(string name) => WindowCardinals(Root, name);

            public long[] WindowCardinals(nuint window, string name)
            {
                nint atom = XInternAtom(_display, name, 1);
                if (atom == 0)
                {
                    return [];
                }

                int status = XGetWindowProperty(_display, window, atom, 0, 64, 0, ANY_PROPERTY_TYPE,
                    out _, out int format, out nuint count, out _, out nint values);
                if (status != 0 || values == 0)
                {
                    return [];
                }

                try
                {
                    // Format-32 items arrive as C longs, one pointer-size slot each.
                    var result = new long[format == 32 ? (int)count : 0];
                    for (int index = 0; index < result.Length; index++)
                    {
                        result[index] = Marshal.ReadIntPtr(values, index * IntPtr.Size);
                    }

                    return result;
                }
                finally
                {
                    _ = XFree(values);
                }
            }

            public (int X, int Y) RootPositionOf(nuint window)
            {
                _ = XTranslateCoordinates(_display, window, Root, 0, 0, out int rootX, out int rootY, out _);
                return (rootX, rootY);
            }

            public void Dispose() => _ = XCloseDisplay(_display);
        }

        [DllImport(LIB)] private static extern nint XOpenDisplay(nint displayName);
        [DllImport(LIB)] private static extern int XCloseDisplay(nint display);
        [DllImport(LIB)] private static extern nuint XDefaultRootWindow(nint display);
        [DllImport(LIB)] private static extern int XDefaultScreen(nint display);
        [DllImport(LIB)] private static extern int XDisplayWidth(nint display, int screen);
        [DllImport(LIB)] private static extern int XDisplayHeight(nint display, int screen);
        [DllImport(LIB)] private static extern nint XInternAtom(nint display, string name, int onlyIfExists);
        [DllImport(LIB)] private static extern int XFree(nint data);
        [DllImport(LIB)]
        private static extern int XTranslateCoordinates(nint display, nuint source, nuint destination, int sourceX, int sourceY,
            out int destinationX, out int destinationY, out nuint child);
        [DllImport(LIB)]
        private static extern int XGetWindowProperty(nint display, nuint window, nint property, long offset, long length,
            int delete, nint requestedType, out nint actualType, out int actualFormat,
            out nuint itemCount, out nuint bytesAfter, out nint values);
    }
}
