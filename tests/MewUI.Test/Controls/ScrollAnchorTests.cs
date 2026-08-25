using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A stationary viewport must keep showing the same rows. Estimated line heights are replaced by
/// measured ones as other code paths materialize far lines, and the view is anchored to a document
/// position so those corrections move the scroll bar, never the content.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ScrollAnchorTests
{
    private static MultiLineTextBox CreateWrappedDocument(out Window window)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        // Wrapping lines of irregular length keep the one-row-per-line estimate wrong everywhere.
        string text = string.Join("\n", Enumerable.Range(0, 8_000)
            .Select(line => $"line {line} " + new string('W', line % 3 == 0 ? 200 : 10)));
        var textBox = new MultiLineTextBox().Width(300).Height(200).Wrap().Text(text);
        window = HeadlessWindow.Create(300, 200);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        return textBox;
    }

    private static (int Offset, double ScreenY) TopContent(MultiLineTextBox textBox)
    {
        ITextViewHost host = textBox;
        var top = host.VisibleTextLines[0];
        return (top.LogicalLine.Offset, top.DocumentY - textBox.VerticalOffset);
    }

    [TestMethod]
    public void RefiningFarEstimatesDoesNotMoveAStationaryViewport()
    {
        var textBox = CreateWrappedDocument(out var window);
        textBox.CaretPosition = textBox.Text.Length / 2;
        window.PerformLayout();
        (int offsetBefore, double screenYBefore) = TopContent(textBox);

        // Measure lines between the top and the viewport: their measured heights replace the
        // one-row estimates, which used to move what a fixed pixel offset resolved to. The arrange
        // is invalidated so the next layout genuinely re-applies the viewport.
        for (int query = 1; query <= 30; query++)
        {
            textBox.GetCharRectInWindow(textBox.Text.Length / 2 * query / 40);
        }
        textBox.InvalidateMeasure();
        window.PerformLayout();

        (int offsetAfter, double screenYAfter) = TopContent(textBox);
        Assert.AreEqual(offsetBefore, offsetAfter, "The top row changed while the viewport stood still.");
        Assert.AreEqual(screenYBefore, screenYAfter, 0.5, "The top row moved on screen while the viewport stood still.");
    }

    [TestMethod]
    public void RepeatedLayoutPassesKeepTheViewportStill()
    {
        var textBox = CreateWrappedDocument(out var window);
        textBox.CaretPosition = textBox.Text.Length / 3;
        window.PerformLayout();
        (int offsetBefore, double screenYBefore) = TopContent(textBox);

        for (int pass = 0; pass < 5; pass++)
        {
            textBox.InvalidateMeasure();
            window.PerformLayout();
        }

        (int offsetAfter, double screenYAfter) = TopContent(textBox);
        Assert.AreEqual(offsetBefore, offsetAfter);
        Assert.AreEqual(screenYBefore, screenYAfter, 0.5);
    }

    /// <summary>
    /// Jumping to the end materializes that region, replacing estimated line heights with measured
    /// ones. The thumb is ranged against the extent, so it has to follow that correction or it
    /// stops short of the end the viewport is actually at.
    /// </summary>
    [TestMethod]
    public void JumpingToTheEndPutsTheThumbAtTheEnd()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }
        // Uniform unwrapped lines, so every height off screen is an estimate until the jump.
        string text = string.Join("\n", Enumerable.Range(0, 8_000)
            .Select(static line => $"public static int Value{line} => {line}; // line {line}"));
        var textBox = new MultiLineTextBox().Width(300).Height(200).FontFamily("Consolas").FontSize(13).Text(text);
        var window = HeadlessWindow.Create(300, 200);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();

        // No layout pass afterwards: the jump happens on the render path, and the next arrange may
        // be a frame away. The thumb has to be right in between.
        textBox.CaretPosition = textBox.Text.Length;

        (double value, double maximum) = textBox.VerticalScrollBarRange;
        Assert.IsGreaterThan(0.0, maximum);
        Assert.AreEqual(maximum, value, maximum * 0.01,
            $"The caret is at the end but the thumb sits at {value / maximum:P0}.");
    }

    [TestMethod]
    public void WheelScrollingStillMovesAndClampsTheViewport()
    {
        var textBox = CreateWrappedDocument(out var window);
        textBox.CaretPosition = 0;
        window.PerformLayout();
        var center = new Point(150, 100);

        window.SendMouseWheel(center, -3);
        Assert.IsGreaterThan(0.0, textBox.VerticalOffset, "Wheel down must scroll.");

        double scrolled = textBox.VerticalOffset;
        window.SendMouseWheel(center, 3);
        Assert.IsLessThan(scrolled, textBox.VerticalOffset, "Wheel up must scroll back.");
    }
}
