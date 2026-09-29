using System.Runtime.InteropServices;
using System.Text;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// What an X11 window manager reads from a window's properties: its title in UTF-8, and the size limits a fixed or
/// limited window asks it to keep.
/// </summary>
[TestClass]
[OSCondition(OperatingSystems.Linux)]
public sealed class X11WindowPropertyTests
{
    private const string TITLE = "VintLingo \u2014 复古物语汉化工具";
    private const string CHANGED_TITLE = "두 번째 제목 ✓";

    [TestMethod]
    public Task ATitleOutsideLatin1ReachesTheWindowManagerInUtf8() => RunWindowAsync(WindowSize.Fixed(400, 200), async (window, display) =>
    {
        Assert.AreEqual(TITLE, X11.ReadUtf8(display, (nuint)window.Handle, "_NET_WM_NAME"), "the title was not set as UTF-8");

        window.Title = CHANGED_TITLE;
        await Task.Delay(200);

        Assert.AreEqual(CHANGED_TITLE, X11.ReadUtf8(display, (nuint)window.Handle, "_NET_WM_NAME"), "a changed title did not reach the window manager");
        Assert.AreEqual(CHANGED_TITLE, X11.ReadUtf8(display, (nuint)window.Handle, "_NET_WM_ICON_NAME"), "the icon name did not follow the title");
    });

    [TestMethod]
    public Task AFixedWindowPinsItsSize() => RunWindowAsync(WindowSize.Fixed(400, 200), (window, display) =>
    {
        var hints = X11.ReadNormalHints(display, (nuint)window.Handle);
        int width = (int)Math.Round(400 * window.DpiScale);
        int height = (int)Math.Round(200 * window.DpiScale);

        Assert.AreEqual(X11.P_MIN_SIZE | X11.P_MAX_SIZE, hints.Flags & (X11.P_MIN_SIZE | X11.P_MAX_SIZE), "the window manager was not asked to keep the size");
        Assert.AreEqual((width, height), (hints.MinWidth, hints.MinHeight), "the minimum size is not the window's size");
        Assert.AreEqual((width, height), (hints.MaxWidth, hints.MaxHeight), "the maximum size is not the window's size");
        return Task.CompletedTask;
    });

    [TestMethod]
    public Task AResizableWindowKeepsItsLimits() => RunWindowAsync(
        WindowSize.Resizable(400, 200, minWidth: 300, minHeight: 150, maxWidth: 600, maxHeight: 400),
        (window, display) =>
        {
            var hints = X11.ReadNormalHints(display, (nuint)window.Handle);
            double scale = window.DpiScale;

            Assert.AreEqual(X11.P_MIN_SIZE | X11.P_MAX_SIZE, hints.Flags & (X11.P_MIN_SIZE | X11.P_MAX_SIZE), "the window manager was not given the limits");
            Assert.AreEqual(((int)Math.Ceiling(300 * scale), (int)Math.Ceiling(150 * scale)), (hints.MinWidth, hints.MinHeight));
            Assert.AreEqual(((int)Math.Ceiling(600 * scale), (int)Math.Ceiling(400 * scale)), (hints.MaxWidth, hints.MaxHeight));
            return Task.CompletedTask;
        });

    // A window at its default startup location, which also asks the window manager for a position.
    private static async Task RunWindowAsync(WindowSize size, Func<Window, nint, Task> check)
    {
        Assert.IsTrue(RealAppSession.IsAvailable, "the application loop did not start on this platform");

        await RealAppSession.RunAsync(async () =>
        {
            var window = new Window { Title = TITLE, WindowSize = size, Content = new TextBlock { Text = "properties" } };
            nint display = X11.XOpenDisplay(0);
            Assert.AreNotEqual((nint)0, display, "a second X connection could not be opened");
            try
            {
                window.Show();
                await Task.Delay(400);
                await check(window, display);
            }
            finally
            {
                _ = X11.XCloseDisplay(display);
                window.Close();
            }
        });
    }

    private static class X11
    {
        public const long P_MIN_SIZE = 1 << 4;
        public const long P_MAX_SIZE = 1 << 5;

        public static string? ReadUtf8(nint display, nuint window, string property)
        {
            nint utf8String = XInternAtom(display, "UTF8_STRING", 0);
            int status = XGetWindowProperty(display, window, XInternAtom(display, property, 0), 0, 4096, 0, utf8String,
                out nint actualType, out int format, out nuint count, out _, out nint values);
            if (status != 0 || values == 0)
            {
                return null;
            }

            try
            {
                if (actualType != utf8String || format != 8)
                {
                    return null;
                }

                var bytes = new byte[(int)count];
                Marshal.Copy(values, bytes, 0, bytes.Length);
                return Encoding.UTF8.GetString(bytes);
            }
            finally
            {
                _ = XFree(values);
            }
        }

        public static SizeHints ReadNormalHints(nint display, nuint window)
        {
            Assert.AreNotEqual(0, XGetWMNormalHints(display, window, out var hints, out _), "the window has no size hints");
            return hints;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SizeHints
        {
            public long Flags;
            public int X, Y, Width, Height;
            public int MinWidth, MinHeight, MaxWidth, MaxHeight;
            public int WidthIncrement, HeightIncrement;
            public int MinAspectX, MinAspectY, MaxAspectX, MaxAspectY;
            public int BaseWidth, BaseHeight, WinGravity;
        }

        [DllImport("libX11.so.6")] public static extern nint XOpenDisplay(nint displayName);
        [DllImport("libX11.so.6")] public static extern int XCloseDisplay(nint display);
        [DllImport("libX11.so.6")] public static extern nint XInternAtom(nint display, string name, int onlyIfExists);
        [DllImport("libX11.so.6")] public static extern int XFree(nint data);
        [DllImport("libX11.so.6")] public static extern int XGetWMNormalHints(nint display, nuint window, out SizeHints hints, out long supplied);
        [DllImport("libX11.so.6")]
        public static extern int XGetWindowProperty(nint display, nuint window, nint property, long offset, long length,
            int delete, nint requestedType, out nint actualType, out int actualFormat,
            out nuint itemCount, out nuint bytesAfter, out nint values);
    }
}
