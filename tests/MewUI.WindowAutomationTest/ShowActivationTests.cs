using System.Runtime.InteropServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Sizing a window from code and showing a window with <see cref="Window.ShowActivated"/> off leave the
/// active window, and the menu open on it, where they were.
/// </summary>
[TestClass]
public sealed class ShowActivationTests
{
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task OnWindows_ResizingAnotherWindowKeepsTheActiveWindowAndItsMenu()
    {
        Assert.IsTrue(RealAppSession.IsAvailable, "the application loop did not start on this platform");

        await RealAppSession.RunAsync(async () =>
        {
            var target = new Border { Background = Color.FromRgb(220, 220, 220) };
            var active = CreateWindow("ShowActivationActive", 420, 320, target);
            var other = CreateWindow("ShowActivationOther", 260, 180, new TextBlock { Text = "other" });
            var menu = new ContextMenu();
            menu.AddItem("First");
            menu.AddItem("Second");

            try
            {
                active.Show();
                active.MoveTo(120, 120);
                other.Show();
                other.MoveTo(620, 160);
                await Task.Delay(400);
                Assert.IsTrue(other.IsActive, "a window shown with the default settings was not activated");

                active.Activate();
                await Task.Delay(300);
                Assert.AreEqual(active.Handle, Win32.GetActiveWindow(), "the window under test could not be activated");

                menu.Show(target, new Point(40, 40));
                await Task.Delay(300);
                Assert.AreEqual(1, active.OpenPopupCount, "the menu did not open");

                other.WindowSize = WindowSize.Fixed(300, 220);
                await Task.Delay(400);

                Assert.AreEqual(active.Handle, Win32.GetActiveWindow(), "resizing the other window took activation");
                Assert.IsTrue(active.IsActive, "the window lost its active state when another window was resized");
                Assert.IsFalse(other.IsActive, "the resized window became active");
                Assert.AreEqual(1, active.OpenPopupCount, "the menu closed when another window was resized");
            }
            finally
            {
                other.Close();
                active.Close();
            }
        });
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task OnWindows_AWindowShownWithoutActivationLeavesTheActiveWindow()
    {
        Assert.IsTrue(RealAppSession.IsAvailable, "the application loop did not start on this platform");

        await RealAppSession.RunAsync(async () =>
        {
            var active = CreateWindow("ShowActivationActive", 420, 320, new TextBlock { Text = "active" });
            var passive = CreateWindow("ShowActivationPassive", 260, 180, new TextBlock { Text = "passive" });
            passive.ShowActivated = false;

            try
            {
                active.Show();
                active.MoveTo(120, 120);
                active.Activate();
                await Task.Delay(400);
                Assert.AreEqual(active.Handle, Win32.GetActiveWindow(), "the window under test could not be activated");

                passive.Show();
                passive.MoveTo(620, 160);
                await Task.Delay(400);
                AssertShownWithoutActivation(active, passive, "first show");

                passive.Hide();
                await Task.Delay(200);
                passive.Show();
                await Task.Delay(400);
                AssertShownWithoutActivation(active, passive, "show after hide");
            }
            finally
            {
                passive.Close();
                active.Close();
            }
        });
    }

    [TestMethod]
    [OSCondition(OperatingSystems.Linux)]
    public async Task OnX11_AWindowShownWithoutActivationAsksNotToBeFocused()
    {
        Assert.IsTrue(RealAppSession.IsAvailable, "the application loop did not start on this platform");

        await RealAppSession.RunAsync(async () =>
        {
            var active = CreateWindow("ShowActivationActive", 420, 320, new TextBlock { Text = "active" });
            var passive = CreateWindow("ShowActivationPassive", 260, 180, new TextBlock { Text = "passive" });
            passive.ShowActivated = false;
            nint display = X11.XOpenDisplay(0);
            Assert.AreNotEqual((nint)0, display, "a second X connection could not be opened");

            try
            {
                active.Show();
                active.MoveTo(120, 120);
                await Task.Delay(400);
                active.Activate();
                await Task.Delay(400);
                Assert.IsTrue(active.IsActive, "the window under test could not be activated");

                passive.Show();
                passive.MoveTo(620, 160);
                await Task.Delay(600);

                Assert.AreEqual((nint)0, X11.ReadCardinal(display, (nuint)passive.Handle, "_NET_WM_USER_TIME"),
                    "the window manager was not asked to leave the window unfocused");
                Assert.IsTrue(active.IsActive, "the previously active window lost its active state");
                Assert.IsFalse(passive.IsActive, "the window shown without activation became active");
            }
            finally
            {
                _ = X11.XCloseDisplay(display);
                passive.Close();
                active.Close();
            }
        });
    }

    private static void AssertShownWithoutActivation(Window active, Window passive, string step)
    {
        Assert.IsTrue(Win32.IsWindowVisible(passive.Handle), $"{step}: the window was not shown");
        Assert.AreEqual(active.Handle, Win32.GetActiveWindow(), $"{step}: showing the window took activation");
        Assert.IsTrue(active.IsActive, $"{step}: the previously active window lost its active state");
        Assert.IsFalse(passive.IsActive, $"{step}: the window shown without activation became active");
    }

    private static Window CreateWindow(string title, double width, double height, UIElement content) => new()
    {
        Title = title,
        StartupLocation = WindowStartupLocation.Manual,
        WindowSize = WindowSize.Fixed(width, height),
        Content = content,
    };

    private static class Win32
    {
        [DllImport("user32.dll")] public static extern nint GetActiveWindow();
        [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] public static extern bool IsWindowVisible(nint hwnd);
    }

    private static class X11
    {
        private const int CARDINAL_TYPE = 6;

        // Returns -1 when the window does not carry the property.
        public static nint ReadCardinal(nint display, nuint window, string name)
        {
            nint property = XInternAtom(display, name, 0);
            int status = XGetWindowProperty(display, window, property, 0, 1, 0, CARDINAL_TYPE,
                out _, out _, out nuint count, out _, out nint values);
            if (status != 0 || values == 0)
            {
                return -1;
            }

            try
            {
                return count == 0 ? -1 : Marshal.ReadIntPtr(values);
            }
            finally
            {
                _ = XFree(values);
            }
        }

        [DllImport("libX11.so.6")] public static extern nint XOpenDisplay(nint displayName);
        [DllImport("libX11.so.6")] public static extern int XCloseDisplay(nint display);
        [DllImport("libX11.so.6")] public static extern nint XInternAtom(nint display, string name, int onlyIfExists);
        [DllImport("libX11.so.6")] public static extern int XFree(nint data);
        [DllImport("libX11.so.6")]
        public static extern int XGetWindowProperty(nint display, nuint window, nint property, long offset, long length,
            int delete, nint requestedType, out nint actualType, out int actualFormat,
            out nuint itemCount, out nuint bytesAfter, out nint values);
    }
}
