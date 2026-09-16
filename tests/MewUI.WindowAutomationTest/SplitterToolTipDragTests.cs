using System.Reflection;
using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Issue #259 with real input: a splitter dragged fast onto a button with a tooltip must keep following
/// the pointer, the tooltip must stay away while the button is held, and once the drag is over a
/// button-up move must not resize the panes again. The second test isolates the tooltip rule: a
/// pointer that arrives with the button held is mid-press, not resting on the element.
/// </summary>
[TestClass]
public sealed class SplitterToolTipDragTests
{
    [TestMethod]
    public async Task FastSplitterDragOntoAToolTipButton_KeepsDraggingAndShowsNoToolTip()
    {
        if (!OperatingSystem.IsWindows() || !RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real Win32 application loop.");
            return;
        }

        var monitor = MonitorMatrix.Monitors.FirstOrDefault();
        if (monitor is null)
        {
            Assert.Inconclusive("Needs a monitor to place the window on.");
            return;
        }

        await RealAppSession.RunAsync(async () =>
        {
            var first = new Button { Content = new TextBlock { Text = "First" } };
            var second = new Button { Content = new TextBlock { Text = "Second" }, ToolTip = new TextBlock { Text = "Test1" } };
            var panel = new SplitPanel { First = first, Second = second };
            var window = new Window
            {
                Title = "SplitterToolTipDrag",
                StartupLocation = WindowStartupLocation.Manual,
                WindowSize = WindowSize.Fixed(400, 200),
                Content = panel,
            };

            Input.GetCursorPos(out var restore);
            try
            {
                window.Show();
                MonitorProbe.SetWindowPos(window.Handle, 0,
                    monitor.PixelBounds.Left + 80, monitor.PixelBounds.Top + 80, 0, 0, MonitorProbe.MOVE_ONLY);
                Input.SetForegroundWindow(window.Handle);
                await Task.Delay(400);

                double startWidth = first.Bounds.Width;
                var splitter = ToScreen(window, first.Bounds.Right + panel.SplitterThickness / 2, first.Bounds.Height / 2);

                Input.SetCursorPos(splitter.X, splitter.Y - 40);
                await Task.Delay(150);
                Input.SetCursorPos(splitter.X, splitter.Y);
                await Task.Delay(150);
                Input.mouse_event(Input.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                await Task.Delay(60);

                // The fast drag: one jump deep into the second pane, then a few short moves on top of it.
                int jump = (int)Math.Round(120 * window.DpiScale);
                Input.SetCursorPos(splitter.X + jump, splitter.Y);
                await Task.Delay(40);
                for (int step = 1; step <= 3; step++)
                {
                    Input.SetCursorPos(splitter.X + jump + step * 4, splitter.Y);
                    await Task.Delay(40);
                }

                await Task.Delay(800);
                double heldWidth = first.Bounds.Width;
                bool tooltipWhileHeld = IsToolTipShowing(window);

                Input.mouse_event(Input.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                await Task.Delay(500);
                bool tooltipAfterRelease = IsToolTipShowing(window);
                double releasedWidth = first.Bounds.Width;

                // Button up: the pointer wanders in steps short enough to stay on the splitter, so a drag
                // that wrongly survived the release would walk the splitter along.
                Input.GetCursorPos(out var resting);
                for (int step = 1; step <= 5; step++)
                {
                    Input.SetCursorPos(resting.X - step * 3, resting.Y);
                    await Task.Delay(60);
                }

                await Task.Delay(400);
                double afterWanderWidth = first.Bounds.Width;

                Assert.IsGreaterThan(startWidth + 100, heldWidth,
                    $"the splitter stopped following the pointer: first pane {startWidth} -> {heldWidth}");
                Assert.IsFalse(tooltipWhileHeld, "the second button's tooltip appeared while the splitter was being dragged");
                Assert.IsFalse(tooltipAfterRelease, "a tooltip appeared on release although the pointer rests on the splitter");
                Assert.AreEqual(releasedWidth, afterWanderWidth, 0.5,
                    "a button-up move over the splitter resized the panes: the drag survived the release");
            }
            finally
            {
                Input.mouse_event(Input.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                Input.SetCursorPos(restore.X, restore.Y);
                window.Close();
            }
        });
    }

    [TestMethod]
    public async Task EnteringAToolTipButtonWithTheButtonHeld_ShowsNoToolTipUntilAFreshEnter()
    {
        if (!OperatingSystem.IsWindows() || !RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real Win32 application loop.");
            return;
        }

        var monitor = MonitorMatrix.Monitors.FirstOrDefault();
        if (monitor is null)
        {
            Assert.Inconclusive("Needs a monitor to place the window on.");
            return;
        }

        await RealAppSession.RunAsync(async () =>
        {
            // A Border takes no capture, so hover keeps following the pointer during the press.
            var plain = new Border { Width = 180 };
            var tipped = new Button { Content = new TextBlock { Text = "Second" }, ToolTip = new TextBlock { Text = "Test1" } };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Add(plain);
            row.Add(tipped);
            var window = new Window
            {
                Title = "ToolTipHeldEnter",
                StartupLocation = WindowStartupLocation.Manual,
                WindowSize = WindowSize.Fixed(400, 200),
                Content = row,
            };

            Input.GetCursorPos(out var restore);
            try
            {
                window.Show();
                MonitorProbe.SetWindowPos(window.Handle, 0,
                    monitor.PixelBounds.Left + 80, monitor.PixelBounds.Top + 80, 0, 0, MonitorProbe.MOVE_ONLY);
                Input.SetForegroundWindow(window.Handle);
                await Task.Delay(400);

                var plainCentre = ToScreen(window, plain.Bounds.X + plain.Bounds.Width / 2, plain.Bounds.Height / 2);
                var tippedCentre = ToScreen(window, tipped.Bounds.X + tipped.Bounds.Width / 2, tipped.Bounds.Height / 2);

                Input.SetCursorPos(plainCentre.X, plainCentre.Y);
                await Task.Delay(150);
                Input.mouse_event(Input.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                await Task.Delay(60);
                Input.SetCursorPos(tippedCentre.X, tippedCentre.Y);
                await Task.Delay(800);
                bool tooltipWhileHeld = IsToolTipShowing(window);

                Input.mouse_event(Input.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                await Task.Delay(500);
                bool tooltipAfterRelease = IsToolTipShowing(window);

                Input.SetCursorPos(plainCentre.X, plainCentre.Y);
                await Task.Delay(150);
                Input.SetCursorPos(tippedCentre.X, tippedCentre.Y);
                await Task.Delay(800);
                bool tooltipAfterFreshEnter = IsToolTipShowing(window);

                Assert.IsFalse(tooltipWhileHeld, "the tooltip opened for a pointer that entered with the button held");
                Assert.IsFalse(tooltipAfterRelease, "the release alone opened the tooltip without a fresh enter");
                Assert.IsTrue(tooltipAfterFreshEnter, "the tooltip never opened after leaving and re-entering with the button up");
            }
            finally
            {
                Input.mouse_event(Input.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                Input.SetCursorPos(restore.X, restore.Y);
                window.Close();
            }
        });
    }

    /// <summary>Screen pixel position of a client-area point given in DIPs.</summary>
    private static Input.POINT ToScreen(Window window, double dipX, double dipY)
    {
        var origin = new Input.POINT();
        Input.ClientToScreen(window.Handle, ref origin);
        return new Input.POINT
        {
            X = origin.X + (int)Math.Round(dipX * window.DpiScale),
            Y = origin.Y + (int)Math.Round(dipY * window.DpiScale),
        };
    }

    /// <summary>
    /// Whether the window has a popup open (its popup manager's count) or a visible surface owned by it.
    /// The thread-wide scan the other tooltip test uses also counts the session's keeper window, so the
    /// OS side is restricted to windows this window owns.
    /// </summary>
    private static bool IsToolTipShowing(Window window)
        => PopupCount(window) > 0 || HasVisibleOwnedSurface(window.Handle);

    private static int PopupCount(Window window)
    {
        var manager = typeof(Window).GetField("_popupManager", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window)!;
        return (int)manager.GetType().GetProperty("Count", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(manager)!;
    }

    private static bool HasVisibleOwnedSurface(nint owner)
    {
        bool found = false;
        uint thread = Input.GetWindowThreadProcessId(owner, out _);
        Input.EnumThreadWindows(thread, (hwnd, _) =>
        {
            if (hwnd != owner && Input.GetWindow(hwnd, Input.GW_OWNER) == owner && Input.IsWindowVisible(hwnd))
            {
                Input.GetWindowRect(hwnd, out var rect);
                if (rect.Right > rect.Left && rect.Bottom > rect.Top)
                {
                    found = true;
                    return false;
                }
            }

            return true;
        }, 0);

        return found;
    }

    private static class Input
    {
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;

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
        }

        public delegate bool EnumWindowsProc(nint hWnd, nint lParam);

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        public static extern bool ClientToScreen(nint hWnd, ref POINT point);

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nint extraInfo);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(nint hWnd);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(nint hWnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(nint hWnd, out uint processId);

        [DllImport("user32.dll")]
        public static extern bool EnumThreadWindows(uint threadId, EnumWindowsProc callback, nint lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(nint hWnd);

        public const uint GW_OWNER = 4;

        [DllImport("user32.dll")]
        public static extern nint GetWindow(nint hWnd, uint command);
    }
}
