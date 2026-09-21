using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A transparent window blends its frame onto the screen. A translucent drawing in it (the shadow of a
/// popup) has to look the same after many frames as after the first: a frame blended onto what the
/// previous frame left darkens it a little more every time.
/// </summary>
[TestClass]
public sealed class TransparentWindowRepaintTests
{
    private const int WINDOW_DIP = 200;

    [TestMethod]
    public async Task TranslucentContentOfATransparentWindow_DoesNotBuildUpOverFrames()
    {
        if (!OperatingSystem.IsWindows() || !RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real Win32 application loop and its screen capture.");
            return;
        }

        await RealAppSession.RunAsync(async () =>
        {
            var backdrop = new Window
            {
                Title = "TransparentRepaintBackdrop",
                StartupLocation = WindowStartupLocation.Manual,
                Background = Color.FromArgb(255, 255, 255, 255),
                WindowSize = WindowSize.Fixed(WINDOW_DIP + 200, WINDOW_DIP + 200),
            };
            var blinker = new Border
            {
                Width = 8,
                Height = 8,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Background = Color.FromArgb(255, 200, 0, 0),
            };
            var shade = new Border
            {
                Margin = new Thickness(30),
                Background = Color.FromArgb(48, 0, 0, 0),
            };
            var content = new Grid();
            content.Children(shade, blinker);
            var window = new Window
            {
                Title = "TransparentRepaint",
                StartupLocation = WindowStartupLocation.Manual,
                AllowsTransparency = true,
                Background = Color.Transparent,
                Padding = new Thickness(0),
                WindowSize = WindowSize.Fixed(WINDOW_DIP, WINDOW_DIP),
                Content = content,
            };

            try
            {
                backdrop.Show();
                backdrop.MoveTo(100, 100);
                await Task.Delay(400);
                window.Show();
                window.MoveTo(160, 160);
                await Task.Delay(600);

                var first = SampleShade(window);

                for (int frame = 0; frame < 30; frame++)
                {
                    blinker.Background = Color.FromArgb(255, (byte)(200 - frame), 0, 0);
                    await Task.Delay(30);
                }

                await Task.Delay(300);
                var later = SampleShade(window);

                Assert.IsLessThanOrEqualTo(
                    2,
                    Math.Abs(first - later),
                    $"{window.GraphicsFactory.Backend}: the translucent area read {first} after the first frames and {later} after thirty more; it built up");
                Assert.IsLessThan(250, first, $"the translucent area read {first}: nothing of it reached the screen, so this run says nothing");
            }
            finally
            {
                window.Close();
                backdrop.Close();
            }
        });
    }

    /// <summary>Brightness at the centre of the window, read from the screen so the blend with what is behind counts.</summary>
    private static int SampleShade(Window window)
    {
        GetWindowRect(window.Handle, out var rect);
        int centerX = (rect.Left + rect.Right) / 2;
        int centerY = (rect.Top + rect.Bottom) / 2;
        var shot = ScreenCapture.OfScreen(centerX - 2, centerY - 2, 4, 4);
        var (blue, green, red, _) = shot.At(1, 1);
        return (blue + green + red) / 3;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint hwnd, out RECT rect);
}
