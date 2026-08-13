using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Arrange snaps an element's left and right edges to the device grid independently, so a box can
/// land under the width its text measured. Render lays the text out again against that box, so a
/// shortfall of even one device pixel turns one line into two, painting over whatever sits below
/// and leaving hit-testing on the boxes layout handed out. The desired width carries the slack for
/// it, and the gap widens with the scale: one device pixel is 0.67 DIP at 150%.
/// </summary>
[TestClass]
public sealed class TextBlockWrapSlackTests
{
    [TestMethod]
    [DataRow(96u)]
    [DataRow(120u)]
    [DataRow(144u)]
    [DataRow(192u)]
    public void ADevicePixelOfArrangeShortfall_DoesNotWrap(uint dpi)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        window.SetDpi(dpi);

        var block = new TextBlock
        {
            Text = "This is a Warning message box sample.",
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = block;
        window.PerformLayout();

        double scale = dpi / 96.0;
        double devicePixel = 1 / scale;
        double singleLineHeight = block.DesiredSize.Height;
        double shortWidth = block.DesiredSize.Width - devicePixel;

        // The worst case arrange can produce: the measured width minus one device pixel.
        block.Arrange(new Rect(0, 0, shortWidth, block.DesiredSize.Height));
        Assert.IsLessThan(block.DesiredSize.Width, block.Bounds.Width,
            "arrange did not land under the desired width, so the assertion below proves nothing");

        var render = block.GetRenderLayoutMetrics();
        Assert.AreEqual(1, render.LineCount,
            $"dpi {dpi}: a {devicePixel:F2} DIP shortfall wrapped the text into {render.LineCount} lines");
        Assert.IsLessThanOrEqualTo(singleLineHeight + 0.51, render.ContentHeight,
            $"dpi {dpi}: the rendered text grew past its single-line height");
    }

    [TestMethod]
    public void AGenuinelyNarrowBox_StillWraps()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create();
        window.SetDpi(144);

        var block = new TextBlock
        {
            Text = "This is a Warning message box sample.",
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = block;
        window.PerformLayout();

        // Slack is for snapping, not for suppressing wrapping the caller asked for.
        double halfWidth = block.DesiredSize.Width / 2;
        block.Arrange(new Rect(0, 0, halfWidth, 200));
        Assert.IsLessThan(block.DesiredSize.Width * 0.75, block.Bounds.Width,
            "the arrange under test did not take effect");

        Assert.IsGreaterThan(1, block.GetRenderLayoutMetrics().LineCount);
    }
}
