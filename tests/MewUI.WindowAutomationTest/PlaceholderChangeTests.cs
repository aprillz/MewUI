using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A text control in a real window must show the placeholder it currently has (issue #260): the
/// text engine's owner cache once handed back the layout of the first placeholder for every later
/// one. The frame is read back through the window's own graphics factory on every platform, and
/// on Windows also from the screen, where the presented window is what the user sees.
/// </summary>
[TestClass]
public sealed class PlaceholderChangeTests
{
    private const double WINDOW_WIDTH = 320;
    private const double WINDOW_HEIGHT = 160;
    private const string WIDE = "WWWWWWWW";
    private const string NARROW = "ii";

    [TestMethod]
    public Task TextBox_ShowsTheCurrentPlaceholder() => RunAsync(static () => new TextBox());

    [TestMethod]
    public Task MultiLineTextBox_ShowsTheCurrentPlaceholder() => RunAsync(static () => new MultiLineTextBox());

    private static Task RunAsync(Func<TextBase> create)
    {
        if (!RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real application loop.");
        }

        return RealAppSession.RunAsync(async () =>
        {
            var editor = create();
            editor.Placeholder = WIDE;
            editor.Margin = new Thickness(16);
            editor.VerticalAlignment = VerticalAlignment.Top;
            // Focus goes to the button: a focused editor hides its placeholder.
            var panel = new StackPanel();
            panel.Add(new Button { Content = new TextBlock { Text = "focus" }, Margin = new Thickness(16, 16, 16, 0) });
            panel.Add(editor);
            var window = new Window
            {
                Title = "PlaceholderChange",
                StartupLocation = WindowStartupLocation.Manual,
                WindowSize = WindowSize.Fixed(WINDOW_WIDTH, WINDOW_HEIGHT),
                Content = panel,
            };

            try
            {
                window.Show();
                window.MoveTo(80, 80);
                await Task.Delay(500);
                Assert.IsFalse(editor.IsFocused, "precondition: the editor took focus, which hides the placeholder");

                var initial = await CaptureAsync(window);
                editor.Placeholder = NARROW;
                var changed = await CaptureAsync(window);
                editor.Placeholder = WIDE;
                var restored = await CaptureAsync(window);

                string subject = $"{window.GraphicsFactory.Backend} {editor.GetType().Name}";
                CollectionAssert.AreNotEqual(initial.Offscreen, changed.Offscreen,
                    $"{subject}: the frame rendered through the window's factory did not change with the placeholder");
                CollectionAssert.AreEqual(initial.Offscreen, restored.Offscreen,
                    $"{subject}: restoring the first placeholder did not restore the frame rendered through the window's factory");
                if (initial.Screen is not null)
                {
                    CollectionAssert.AreNotEqual(initial.Screen, changed.Screen,
                        $"{subject}: the presented window did not change with the placeholder");
                    CollectionAssert.AreEqual(initial.Screen, restored.Screen,
                        $"{subject}: restoring the first placeholder did not restore the presented window");
                }

                // Logged before the window closes: a display server that crashes the process on teardown still leaves the verdict.
                Console.Error.WriteLine($"{subject}: placeholder frames verified (screen {(initial.Screen is null ? "not read" : "read")})");
            }
            finally
            {
                window.Close();
            }
        });
    }

    /// <summary>Waits for the change to be presented, then reads the frame back; the screen is read only where a capture exists.</summary>
    private static async Task<(byte[] Offscreen, byte[]? Screen)> CaptureAsync(Window window)
    {
        await Task.Delay(400);
        var factory = window.GraphicsFactory;
        double scale = window.GetDpi() / 96.0;
        // The GL backend renders offscreen only under a current context, which the UI thread holds during a frame alone.
        using var scope = factory.AcquireBackgroundRenderScope();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(
            (int)Math.Round(WINDOW_WIDTH * scale), (int)Math.Round(WINDOW_HEIGHT * scale), scale));
        window.RenderFrameToSurface(surface);
        byte[] offscreen = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan().ToArray();
        byte[]? screen = OperatingSystem.IsWindows() ? ScreenCapture.OfClientArea(window.Handle).Bgra : null;
        return (offscreen, screen);
    }
}
