using System.Reflection;
using System.Runtime.InteropServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// The native state of transparent and fixed-size windows on macOS: a window that cannot be maximized
/// stays in its normal state, a size set after it is shown reaches the drawable, a borderless
/// transparent window has no hidden title bar, and a transparent window casts no system shadow.
/// </summary>
[TestClass]
public sealed class MacTransparentWindowTests
{
    private const string OBJC = "/usr/lib/libobjc.A.dylib";
    private const nuint STYLE_TITLED = 1;
    private const int SETTLE_MS = 600;

    [TestMethod]
    [DataRow(true, 140.0, 22.5)]
    [DataRow(false, 300.0, 200.0)]
    public Task FixedSizeWindow_StaysInItsNormalState(bool transparent, double width, double height) => RunOnMacAsync(async () =>
    {
        var window = NewWindow(transparent, borderless: transparent, WindowSize.Fixed(width, height));
        try
        {
            window.Show();
            window.MoveTo(300, 300);
            await Task.Delay(1000);

            var frame = SendRect(NsWindow(window), Sel("frame"));
            Assert.AreEqual(WindowState.Normal, window.WindowState, $"the window reported itself {window.WindowState} with frame {frame.Width}x{frame.Height}");
            Assert.AreEqual(width, frame.Width, 1.0, "the frame width left the fixed size");
        }
        finally
        {
            window.Close();
        }
    });

    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public Task SizeSetAfterTheWindowIsShown_ReachesTheDrawable(bool transparent) => RunOnMacAsync(async () =>
    {
        var window = NewWindow(transparent, borderless: transparent, WindowSize.Resizable(200, 100));
        try
        {
            window.Show();
            window.MoveTo(300, 300);
            await Task.Delay(SETTLE_MS);

            window.WindowSize = WindowSize.Resizable(320, 180);
            await Task.Delay(SETTLE_MS);

            var client = window.ClientSize;
            Assert.AreEqual(320.0, client.Width, 1.0, "the client width did not follow the new size");
            var drawable = SendSize(Send(window.Handle, Sel("layer")), Sel("drawableSize"));
            Assert.AreEqual(Math.Ceiling(client.Width * window.DpiScale), drawable.Width, 1.0, $"the drawable stayed {drawable.Width}x{drawable.Height} for a client of {client.Width}x{client.Height}");
            Assert.AreEqual(Math.Ceiling(client.Height * window.DpiScale), drawable.Height, 1.0, $"the drawable stayed {drawable.Width}x{drawable.Height} for a client of {client.Width}x{client.Height}");
        }
        finally
        {
            window.Close();
        }
    });

    [TestMethod]
    public Task TransparentWindow_HasATitleBarOnlyWhenItIsNotBorderless() => RunOnMacAsync(async () =>
    {
        var borderless = NewWindow(transparent: true, borderless: true, WindowSize.Fixed(200, 120));
        var chromed = NewWindow(transparent: true, borderless: false, WindowSize.Resizable(200, 120));
        try
        {
            borderless.Show();
            borderless.MoveTo(300, 300);
            chromed.Show();
            chromed.MoveTo(560, 300);
            await Task.Delay(SETTLE_MS);

            Assert.AreEqual((nuint)0, SendNuint(NsWindow(borderless), Sel("styleMask")), "a borderless transparent window kept a title bar in its style mask");
            Assert.AreEqual(STYLE_TITLED, SendNuint(NsWindow(chromed), Sel("styleMask")) & STYLE_TITLED, "a transparent window that is not borderless lost its hidden title bar");
        }
        finally
        {
            borderless.Close();
            chromed.Close();
        }
    });

    [TestMethod]
    public Task TransparentWindow_CastsNoSystemShadow() => RunOnMacAsync(async () =>
    {
        var transparent = NewWindow(transparent: true, borderless: false, WindowSize.Resizable(200, 120));
        var opaque = NewWindow(transparent: false, borderless: false, WindowSize.Resizable(200, 120));
        try
        {
            transparent.Show();
            transparent.MoveTo(300, 300);
            opaque.Show();
            opaque.MoveTo(560, 300);
            await Task.Delay(SETTLE_MS);

            Assert.IsFalse(SendBool(NsWindow(transparent), Sel("hasShadow")), "a transparent window kept the system shadow");
            Assert.IsTrue(SendBool(NsWindow(opaque), Sel("hasShadow")), "an opaque window lost the system shadow");
        }
        finally
        {
            transparent.Close();
            opaque.Close();
        }
    });

    [TestMethod]
    public Task TransparentToolWindow_ShowsAndCloses() => RunOnMacAsync(async () =>
    {
        var window = NewWindow(transparent: true, borderless: false, WindowSize.Fixed(200, 120));
        window.IsToolWindow = true;
        window.Show();
        window.MoveTo(300, 300);
        await Task.Delay(SETTLE_MS);
        Assert.AreNotEqual((nint)0, window.Handle);
        window.Close();
    });

    [TestMethod]
    public Task ResizableWindow_StillReportsZoomAndMaximizes() => RunOnMacAsync(async () =>
    {
        var opaque = NewWindow(transparent: false, borderless: false, WindowSize.Resizable(400, 300));
        var transparent = NewWindow(transparent: true, borderless: false, WindowSize.Resizable(400, 300));
        try
        {
            opaque.Show();
            opaque.MoveTo(200, 200);
            await Task.Delay(SETTLE_MS);
            Assert.AreEqual(WindowState.Normal, opaque.WindowState);

            SendVoidArg(NsWindow(opaque), Sel("zoom:"), 0);
            await Task.Delay(1000);
            Assert.AreEqual(WindowState.Maximized, opaque.WindowState, "a zoomed window did not report itself maximized");
            SendVoidArg(NsWindow(opaque), Sel("zoom:"), 0);
            await Task.Delay(1000);
            Assert.AreEqual(WindowState.Normal, opaque.WindowState, "a window zoomed back did not report itself normal");
            opaque.Close();

            transparent.Show();
            transparent.MoveTo(200, 200);
            await Task.Delay(SETTLE_MS);
            Assert.AreEqual(WindowState.Normal, transparent.WindowState);

            transparent.WindowState = WindowState.Maximized;
            await Task.Delay(1000);
            nint nsWindow = NsWindow(transparent);
            var visible = SendRect(Send(nsWindow, Sel("screen")), Sel("visibleFrame"));
            var frame = SendRect(nsWindow, Sel("frame"));
            Assert.AreEqual(visible.Width, frame.Width, 1.0, "a maximized transparent window does not fill the work area");
            Assert.AreEqual(WindowState.Maximized, transparent.WindowState);

            transparent.WindowState = WindowState.Normal;
            await Task.Delay(1000);
            Assert.AreEqual(400.0, SendRect(nsWindow, Sel("frame")).Width, 1.0, "a restored transparent window did not return to its size");
            Assert.AreEqual(WindowState.Normal, transparent.WindowState);
        }
        finally
        {
            opaque.Close();
            transparent.Close();
        }
    });

    private static Task RunOnMacAsync(Func<Task> body)
    {
        if (!OperatingSystem.IsMacOS() || !RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real macOS application loop.");
        }

        return RealAppSession.RunAsync(body);
    }

    private static Window NewWindow(bool transparent, bool borderless, WindowSize size)
    {
        var window = new Window
        {
            Title = "MacTransparentWindow",
            StartupLocation = WindowStartupLocation.Manual,
            WindowSize = size,
            Topmost = true,
            Padding = new Thickness(0),
            Content = new Border { Background = Color.FromArgb(255, 40, 120, 200) },
        };
        if (transparent)
        {
            window.AllowsTransparency = true;
            window.Background = Color.Transparent;
        }

        window.Borderless = borderless;
        return window;
    }

    private static nint NsWindow(Window window)
    {
        var backend = typeof(Window).GetProperty("Backend", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        return (nint)backend.GetType().GetField("_nsWindow", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(backend)!;
    }

    private static nint Sel(string name) => sel_registerName(name);

    [StructLayout(LayoutKind.Sequential)]
    private struct CGSize
    {
        public double Width;
        public double Height;
    }

    [DllImport(OBJC)] private static extern nint sel_registerName(string name);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern nint Send(nint receiver, nint selector);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern void SendVoidArg(nint receiver, nint selector, nint argument);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern nuint SendNuint(nint receiver, nint selector);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool SendBool(nint receiver, nint selector);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern NSRect SendRect(nint receiver, nint selector);
    [DllImport(OBJC, EntryPoint = "objc_msgSend")] private static extern CGSize SendSize(nint receiver, nint selector);
}
