using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Pressing a button while its tooltip is up closes the tooltip, and closing a popup destroys its
/// window. The Win32 destroy path released the mouse capture unconditionally, which is a thread-wide
/// call: it took the capture the button had just taken, the owner window got WM_CAPTURECHANGED, and
/// the press was cancelled before the release could raise Click (issue #253). The press and release
/// are far enough apart here for the destroy to land between them, which is where the bug lives.
/// </summary>
[TestClass]
public sealed class ToolTipClickTests
{
    private const int PRESS_TO_RELEASE_MS = 400;

    [TestMethod]
    public async Task ButtonWithAToolTip_RaisesClickOnTheFirstPress()
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
            int clicks = 0;
            var button = new Button
            {
                Content = new TextBlock { Text = "Test" },
                ToolTip = new TextBlock { Text = "Tip" },
            };
            button.Click += () => clicks++;

            var window = new Window
            {
                Title = "ToolTipClick",
                StartupLocation = WindowStartupLocation.Manual,
                WindowSize = WindowSize.Fixed(360, 200),
                Content = button,
            };

            Input.GetCursorPos(out var restore);
            try
            {
                window.Show();
                MonitorProbe.SetWindowPos(window.Handle, 0,
                    monitor.PixelBounds.Left + 80, monitor.PixelBounds.Top + 80, 0, 0, MonitorProbe.MOVE_ONLY);
                Input.SetForegroundWindow(window.Handle);
                await Task.Delay(400);

                Input.GetWindowRect(window.Handle, out var frame);
                int centreX = (frame.Left + frame.Right) / 2;
                int centreY = (frame.Top + frame.Bottom) / 2;

                // Approach from outside so the pointer really enters the button and raises the tooltip.
                Input.SetCursorPos(centreX, centreY - 40);
                await Task.Delay(200);
                Input.SetCursorPos(centreX, centreY);
                await Task.Delay(800);

                Assert.IsTrue(
                    HasVisibleToolTipSurface(window.Handle),
                    "the tooltip surface never appeared, so the press below would not exercise the regression");

                Input.mouse_event(Input.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
                await Task.Delay(PRESS_TO_RELEASE_MS);
                Input.mouse_event(Input.MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
                await Task.Delay(400);

                Assert.AreEqual(1, clicks,
                    "the first press over a button showing a tooltip raised no Click: the tooltip's " +
                    "window teardown released the capture the press had taken");
            }
            finally
            {
                Input.SetCursorPos(restore.X, restore.Y);
                window.Close();
            }
        });
    }

    /// <summary>Whether the owner's thread owns a visible popup surface besides the window itself.</summary>
    private static bool HasVisibleToolTipSurface(nint owner)
    {
        bool found = false;
        uint thread = Input.GetWindowThreadProcessId(owner, out _);
        Input.EnumThreadWindows(thread, (hwnd, _) =>
        {
            if (hwnd != owner && Input.IsWindowVisible(hwnd))
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
    }
}
