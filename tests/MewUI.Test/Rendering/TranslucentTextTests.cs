using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Text drawn with an opacity below one goes through the backend's translucent group. It has to come out faded, not
/// vanish or take the frame down with it.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TranslucentTextTests
{
    private const int WIDTH = 300;
    private const int HEIGHT = 60;
    private const double TEXT_OPACITY = 0.55;
    private static readonly Color BACKGROUND = Color.FromRgb(250, 250, 250);
    private static readonly Color INK = Color.FromRgb(30, 30, 30);

    [TestMethod]
    [DataRow(TestBackend.Gdi)]
    [DataRow(TestBackend.Direct2D)]
    [DataRow(TestBackend.MewVG)]
    public void TextWithOpacity_IsDrawnFaded(TestBackend backend)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The backends under test are Windows-only.");
            return;
        }

        using var session = TestBackendSession.Open(backend);
        var text = new TextBlock { Text = "Standard Library", FontSize = 20, Foreground = INK, Opacity = TEXT_OPACITY };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = new Border { Background = BACKGROUND, Child = text };
        window.PerformLayout();

        using var surface = session.Factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderFrameToSurface(surface);
        window.RenderFrameToSurface(surface);

        var pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int darkest = int.MaxValue;
        int lightest = 0;
        for (int offset = 0; offset + 3 < pixels.Length; offset += 4)
        {
            int channel = Math.Min(pixels[offset], Math.Min(pixels[offset + 1], pixels[offset + 2]));
            darkest = Math.Min(darkest, channel);
            lightest = Math.Max(lightest, pixels[offset]);
        }

        Assert.AreEqual(BACKGROUND.B, lightest, "the frame around the text was not drawn");
        int faded = (int)Math.Round(INK.R * TEXT_OPACITY + BACKGROUND.R * (1 - TEXT_OPACITY));
        Assert.IsLessThan(faded + 20, darkest, $"the faded text left no ink: darkest channel {darkest}");
        Assert.IsGreaterThan(INK.R + 20, darkest, $"the text was drawn at full strength: darkest channel {darkest}");
    }
}
