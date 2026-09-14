using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A splitter drag lives exactly as long as the mouse capture the press took. Losing the capture to
/// another element or to a release outside the framework ends the drag, so later pointer moves with
/// the button up do not keep resizing the panes (issue #259).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SplitPanelDragTests
{
    private static bool SkipOnNonWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        Assert.Inconclusive("GDI backend is Windows-only.");
        return true;
    }

    private static (Window window, SplitPanel panel, Border first, Border second, Point splitter) Host()
    {
        var first = new Border();
        var second = new Border();
        var panel = new SplitPanel { First = first, Second = second, SplitterThickness = 8 };

        var window = HeadlessWindow.Create(400, 100);
        window.Content = panel;
        window.PerformLayout();

        var splitter = new Point(first.Bounds.Right + panel.SplitterThickness / 2, 50);
        return (window, panel, first, second, splitter);
    }

    [TestMethod]
    public void DraggingWithTheButtonHeld_MovesTheSplitter()
    {
        if (SkipOnNonWindows()) return;

        var (window, _, first, _, splitter) = Host();
        double startWidth = first.Bounds.Width;

        window.SendMouseMove(splitter);
        window.SendMouseDown(splitter);
        window.SendMouseDrag(new Point(splitter.X + 50, splitter.Y));
        window.PerformLayout();

        Assert.AreEqual(startWidth + 50, first.Bounds.Width, 0.5, "the drag did not follow the pointer");

        window.SendMouseUp(new Point(splitter.X + 50, splitter.Y));
        Assert.IsNull(window.CapturedElement, "the release ended the capture");
    }

    [TestMethod]
    public void CaptureTakenByAnotherElement_EndsTheDrag()
    {
        if (SkipOnNonWindows()) return;

        var (window, _, first, second, splitter) = Host();
        double startWidth = first.Bounds.Width;

        window.SendMouseMove(splitter);
        window.SendMouseDown(splitter);
        Assert.IsNotNull(window.CapturedElement, "the press took the capture");

        window.CaptureMouse(second);
        window.ReleaseMouseCapture();
        WanderOverTheSplitter(window, first, splitter);

        Assert.AreEqual(startWidth, first.Bounds.Width, 0.5, "the splitter kept following the pointer after losing the capture");
    }

    /// <summary>
    /// Button-up moves in steps short enough to stay on the splitter, so a drag that wrongly survived
    /// would keep receiving the moves and walk the splitter along.
    /// </summary>
    private static void WanderOverTheSplitter(Window window, Border first, Point splitter)
    {
        window.SendMouseMove(new Point(splitter.X - 60, splitter.Y));
        window.SendMouseMove(splitter);
        for (int step = 1; step <= 5; step++)
        {
            window.SendMouseMove(new Point(splitter.X + step * 3, splitter.Y));
            window.PerformLayout();
        }
    }

    [TestMethod]
    public void MovesAfterTheCaptureEnded_DoNotResumeTheDrag()
    {
        if (SkipOnNonWindows()) return;

        var (window, _, first, _, splitter) = Host();
        double startWidth = first.Bounds.Width;

        window.SendMouseMove(splitter);
        window.SendMouseDown(splitter);
        window.ReleaseMouseCapture();

        // The pointer comes back over the splitter with the button already up, as after a release the
        // framework never saw.
        WanderOverTheSplitter(window, first, splitter);

        Assert.AreEqual(startWidth, first.Bounds.Width, 0.5, "a button-up move resumed the drag");
    }
}
