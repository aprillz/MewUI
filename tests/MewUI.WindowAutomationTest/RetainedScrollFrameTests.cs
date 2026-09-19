using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A ScrollViewer scrolls in a real window on this platform backend. Every frame after a scroll
/// must equal a frame drawn straight from the visuals in the same state.
/// </summary>
[TestClass]
public sealed class RetainedScrollFrameTests
{
    private const double WINDOW_WIDTH = 240;
    private const double WINDOW_HEIGHT = 160;
    private const int ITEM_COUNT = 12;

    [TestMethod]
    public Task ScrollViewer_MatchesImmediateRenderOnThisBackend()
    {
        if (!RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real application loop.");
        }

        return RealAppSession.RunAsync(async () =>
        {
            var panel = new StackPanel();
            for (int itemIndex = 0; itemIndex < ITEM_COUNT; itemIndex++)
            {
                panel.Add(new Border
                {
                    Height = 36,
                    Margin = new Thickness(8, 2, 8, 2),
                    Background = Color.FromArgb(255, (byte)(40 + itemIndex * 17), (byte)(200 - itemIndex * 12), 120),
                    Child = new TextBlock { Text = $"Row {itemIndex}", Margin = new Thickness(8) },
                });
            }
            var scrollViewer = new ScrollViewer { VerticalScroll = ScrollMode.Visible, Content = panel };
            var window = new Window
            {
                Title = "RetainedScrollMove",
                StartupLocation = WindowStartupLocation.Manual,
                WindowSize = WindowSize.Fixed(WINDOW_WIDTH, WINDOW_HEIGHT),
                Content = scrollViewer,
            };

            try
            {
                window.Show();
                window.MoveTo(80, 80);
                await Task.Delay(500);
                string subject = $"{window.GraphicsFactory.Backend}";
                var initial = await CaptureAsync(window);
                scrollViewer.SetScrollOffsets(0, 20);
                var scrolled = await CaptureAsync(window);
                scrollViewer.SetScrollOffsets(0, 51);
                var scrolledAgain = await CaptureAsync(window);

                var immediate = await CaptureAsync(window, reference: true);

                CollectionAssert.AreNotEqual(initial, scrolled, $"{subject}: the frame did not change with the scroll");
                Assert.AreEqual(string.Empty, Describe(immediate, scrolledAgain, window), $"{subject}: the scene-driven frame after scrolling differs from the reference frame");
                Console.Error.WriteLine($"{subject}: retained scroll frames verified");
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static string Describe(byte[] expected, byte[] actual, Window window)
    {
        double scale = window.GetDpi() / 96.0;
        int pixelWidth = (int)Math.Round(WINDOW_WIDTH * scale);
        int differing = 0;
        int first = -1;
        int maxDelta = 0;
        for (int offset = 0; offset + 3 < expected.Length && offset + 3 < actual.Length; offset += 4)
        {
            int delta = Math.Max(
                Math.Abs(expected[offset] - actual[offset]),
                Math.Max(Math.Abs(expected[offset + 1] - actual[offset + 1]), Math.Abs(expected[offset + 2] - actual[offset + 2])));
            if (delta != 0)
            {
                differing++;
                maxDelta = Math.Max(maxDelta, delta);
                if (first < 0)
                {
                    first = offset / 4;
                }
            }
        }

        return differing == 0
            ? string.Empty
            : $"d{maxDelta} n{differing} s{scale} at {first % pixelWidth},{first / pixelWidth}";
    }

    private static async Task<byte[]> CaptureAsync(Window window, bool reference = false)
    {
        await Task.Delay(400);
        var factory = window.GraphicsFactory;
        double scale = window.GetDpi() / 96.0;
        // The GL backend renders offscreen only under a current context, which the UI thread holds during a frame alone.
        using var scope = factory.AcquireBackgroundRenderScope();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(
            (int)Math.Round(WINDOW_WIDTH * scale), (int)Math.Round(WINDOW_HEIGHT * scale), scale));
        if (reference)
        {
            window.RenderReferenceFrameToSurface(surface);
        }
        else
        {
            window.RenderFrameToSurface(surface);
        }

        return ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan().ToArray();
    }
}
