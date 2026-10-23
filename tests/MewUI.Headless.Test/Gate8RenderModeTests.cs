using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Headless;

namespace MewUI.Headless.Test;

/// <summary>
/// Gate 8: the frame a render-mode session captures equals the frame the same backend draws
/// straight from the visuals, and its pixels show what was drawn. The backend is the one the
/// fixture registered (GDI by default; MEWUI_HEADLESS_TEST_BACKEND=Direct2D selects Direct2D;
/// MewVG on Linux, where the frame is drawn on a surfaceless EGL context, and macOS).
/// </summary>
[TestClass]
public sealed class Gate8RenderModeTests
{
    private const int WIDTH = 240;
    private const int HEIGHT = 160;

    private static readonly Color BACKGROUND = Color.FromArgb(255, 240, 240, 240);
    private static readonly Color FILL = Color.FromArgb(255, 40, 120, 200);
    private static readonly Color CHANGED_BACKGROUND = Color.FromArgb(255, 32, 32, 32);

    [TestMethod]
    public void CaptureFrame_MatchesTheReferenceFrameOfTheRegisteredBackend()
    {
        HeadlessTestHost.WithSession(new HeadlessOptions { Rendering = HeadlessRendering.Offscreen }, session =>
        {
            var window = session.Run(() =>
            {
                var shown = new Window
                {
                    WindowSize = WindowSize.Resizable(WIDTH, HEIGHT),
                    Background = BACKGROUND,
                    Padding = new Thickness(20),
                    Content = new StackPanel().Children(
                        new Border
                        {
                            Width = 100,
                            Height = 40,
                            HorizontalAlignment = HorizontalAlignment.Left,
                            Background = FILL,
                        },
                        new TextBlock { Text = "Rendered headless" }),
                };
                shown.Show();
                return shown;
            });

            var frame = window.CaptureFrame();
            Assert.AreEqual(WIDTH, frame.Width);
            Assert.AreEqual(HEIGHT, frame.Height);
            Assert.AreEqual(1.0, frame.DpiScale);
            Assert.AreEqual(BACKGROUND, frame.GetPixel(5, 5), "the window background was not drawn");
            Assert.AreEqual(FILL, frame.GetPixel(30, 30), "the border fill was not drawn");
            AssertFramesEqual(window.CaptureReferenceFrame(), frame, "first frame");

            session.Run(() => window.Background = CHANGED_BACKGROUND);
            frame = window.CaptureFrame();
            Assert.AreEqual(CHANGED_BACKGROUND, frame.GetPixel(5, 5), "the background change was not drawn");
            AssertFramesEqual(window.CaptureReferenceFrame(), frame, "after the background changed");
        });
    }

    [TestMethod]
    public void MeasureOnly_CaptureFrameIsRejected()
    {
        var window = HeadlessTestHost.ShowWindow(new Border());
        try
        {
            Assert.ThrowsExactly<InvalidOperationException>(() => window.CaptureFrame());
        }
        finally
        {
            HeadlessTestHost.Close(window);
        }
    }

    private static void AssertFramesEqual(HeadlessFrame expected, HeadlessFrame actual, string label)
    {
        Assert.AreEqual(expected.Width, actual.Width, label);
        Assert.AreEqual(expected.Height, actual.Height, label);
        int differing = 0;
        for (int y = 0; y < expected.Height; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                if (expected.GetPixel(x, y) != actual.GetPixel(x, y))
                {
                    differing++;
                }
            }
        }

        Assert.AreEqual(0, differing, $"{label}: {differing} pixels differ from the reference frame");
    }
}
