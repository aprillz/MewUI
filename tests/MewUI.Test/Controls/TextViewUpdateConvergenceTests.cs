using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// An update pass has to reach a resting point. A pass whose own work dirties the tree again asks
/// for the next one forever, which spends a core doing nothing and leaves input waiting behind the
/// passes; the text view is where that happens, because standing the visible lines up is what
/// replaces estimated line heights and moves the scroll offset. These say the pass settles: once
/// with nothing happening, and once after a scroll.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TextViewUpdateConvergenceTests
{
    private const int WIDTH = 300;
    private const int HEIGHT = 200;
    private const int LINES = 8_000;

    [TestMethod]
    public void AnIdleFrameAsksForNoFurtherUpdatePass()
    {
        var (window, textBox) = CreateHost();
        // That the flag says anything at all: dirtying the tree has to unsettle it, or the checks
        // below would hold on a pass that never stopped.
        textBox.InvalidateMeasure();
        Assert.IsFalse(window.IsUpdatePassSettled, "an invalidated tree still counted as settled");

        var probe = new Probe(textBox);
        for (int frame = 0; frame < 3; frame++)
        {
            window.PerformLayout();
        }

        Assert.IsTrue(window.IsUpdatePassSettled, "an idle pass asked for another one");
        probe.AssertQuiet("an idle pass");
    }

    [TestMethod]
    public void ScrollingWithTheWheelSettlesWithinOneUpdatePass()
    {
        var (window, textBox) = CreateHost();
        var moving = new Probe(textBox);
        for (int notch = 0; notch < 20; notch++)
        {
            window.SendMouseWheel(textBox.CenterOf(), -3);
        }

        window.PerformLayout();

        // That the probe below is watching something: the scroll itself must have moved the lines.
        Assert.IsGreaterThan(0, moving.LineChanges, "the wheel never reached the view");
        Assert.IsTrue(window.IsUpdatePassSettled, "the pass the wheel asked for asked for another one");
        var probe = new Probe(textBox);
        double offset = textBox.VerticalOffset;
        for (int frame = 0; frame < 3; frame++)
        {
            window.PerformLayout();
        }

        Assert.IsTrue(window.IsUpdatePassSettled, "the frames after a wheel scroll never stopped");
        Assert.AreEqual(offset, textBox.VerticalOffset, "the offset kept being corrected frame after frame");
        probe.AssertQuiet("the frames after a wheel scroll");
    }

    /// <summary>
    /// A jump straight into estimated territory, which a wheel scroll only reaches gradually. The
    /// corrections that replace those estimates are what the pass has to converge.
    /// </summary>
    [TestMethod]
    public void AJumpIntoEstimatedLinesSettlesToo()
    {
        var (window, textBox) = CreateHost();
        textBox.CaretPosition = textBox.Text.Length / 2;

        window.PerformLayout();

        Assert.IsTrue(window.IsUpdatePassSettled, "the pass the jump asked for asked for another one");
        var probe = new Probe(textBox);
        double offset = textBox.VerticalOffset;
        for (int frame = 0; frame < 3; frame++)
        {
            window.PerformLayout();
        }

        Assert.IsTrue(window.IsUpdatePassSettled, "the frames after a jump never stopped");
        Assert.AreEqual(offset, textBox.VerticalOffset, "the offset kept being corrected frame after frame");
        probe.AssertQuiet("the frames after a jump");
    }

    /// <summary>
    /// The two things a pass would have to keep changing to keep asking for the next one. Both are
    /// the view's own notifications, so a settled tree raises neither.
    /// </summary>
    private sealed class Probe
    {
        public Probe(ITextViewHost host)
        {
            host.LinesChanged += _ => LineChanges++;
            host.ScrollOffsetChanged += _ => OffsetChanges++;
        }

        public int LineChanges { get; private set; }

        public int OffsetChanges { get; private set; }

        public void AssertQuiet(string what)
        {
            Assert.AreEqual(0, LineChanges, $"{what} stood different lines up");
            Assert.AreEqual(0, OffsetChanges, $"{what} moved the scroll offset");
        }
    }

    private static (Window Window, MultiLineTextBox TextBox) CreateHost()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
        }

        // Lines of irregular length keep the one-row-per-line estimate wrong everywhere, so the
        // corrections a pass has to converge actually happen.
        string text = string.Join('\n', Enumerable.Range(0, LINES)
            .Select(static line => $"line {line} " + new string('W', line % 3 == 0 ? 200 : 10)));
        var textBox = new MultiLineTextBox().Width(WIDTH).Height(HEIGHT).Text(text);
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = textBox;
        window.PerformLayout();
        return (window, textBox);
    }
}
