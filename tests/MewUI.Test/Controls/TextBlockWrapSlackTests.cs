using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Arrange snaps an element's left and right edges to the device grid independently, so a box can
/// land under the width its text measured. Wrapped text keeps the line breaks measure chose, so a
/// shortfall of a device pixel never turns one line into two; text wraps narrower only when it is
/// measured narrower.
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

        // Keeping the measured line breaks must not suppress wrapping a narrower measure asks for.
        double fullWidth = block.DesiredSize.Width;
        double halfWidth = fullWidth / 2;
        block.Measure(new Size(halfWidth, 200));
        block.Arrange(new Rect(0, 0, halfWidth, 200));
        Assert.IsLessThan(fullWidth * 0.75, block.Bounds.Width,
            "the arrange under test did not take effect");

        Assert.IsGreaterThan(1, block.GetRenderLayoutMetrics().LineCount);
    }

    [TestMethod]
    [DataRow(96u)]
    [DataRow(120u)]
    [DataRow(144u)]
    [DataRow(168u)]
    [DataRow(192u)]
    public void CenteredAtItsDesiredWidth_KeepsTheMeasuredLines(uint dpi)
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var window = HeadlessWindow.Create(296, 400);
        window.SetDpi(dpi);

        // Wrapping these words again at the widest measured line moves one to a third line at 125%.
        var block = new TextBlock
        {
            Text = "hhhhhhhhh iiiiiii jjjjj kkk llllllllll mmmmmmmm nnnnnn oooo pp qqqqqqqqq rrrrrrr sssss ttt",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };
        window.Content = block;
        window.PerformLayout();

        var render = block.GetRenderLayoutMetrics();
        Assert.IsLessThanOrEqualTo(block.Bounds.Height + 0.5 * 96 / dpi, render.ContentHeight,
            $"dpi {dpi}: {render.LineCount} lines need {render.ContentHeight} in a box {block.Bounds.Height} tall at width {block.Bounds.Width}");
    }
}
