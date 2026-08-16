using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Controls;

/// <summary>
/// A whole line's coordinates, answered without laying the line out. A line long enough to be cut
/// into slices has no laid-out text outside the viewport, so these come from the estimate that
/// chose the slices.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TextLineExtentTests
{
    private const int LONG_LINE = 4000;

    private static MultiLineTextBox CreateBox(string text)
    {
        var box = new MultiLineTextBox
        {
            Text = text,
            FontFamily = "Consolas",
            FontSize = 13,
            Wrap = false
        };
        box.Measure(new Size(400, 200));
        box.Arrange(new Rect(0, 0, 400, 200));
        return box;
    }

    [TestMethod]
    public void AShortLineAnswersFromItsLaidOutText()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        ITextViewHost host = CreateBox("abcdef");
        var extent = host.GetLineExtent(0);

        Assert.IsNotNull(extent);
        Assert.IsTrue(extent.IsExact, "A line laid out whole is measured, not estimated.");
        Assert.AreEqual(0, extent.SourceOffset);
        Assert.AreEqual(6, extent.SourceLength);
        Assert.AreEqual(0, extent.GetXForOffset(0), 0.001);
        Assert.AreEqual(extent.Width, extent.GetXForOffset(extent.SourceLength), 0.001);
        Assert.AreEqual(3, extent.GetOffsetForX(extent.GetXForOffset(3)));
    }

    [TestMethod]
    public void ASlicedLineAnswersBeyondWhatIsLaidOut()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var box = CreateBox(new string('x', LONG_LINE));
        ITextViewHost host = box;
        var extent = host.GetLineExtent(0);

        Assert.IsNotNull(extent);
        Assert.AreEqual(LONG_LINE, extent.SourceLength);

        // Far outside the viewport, where no slice is laid out.
        double x = extent.GetXForOffset(LONG_LINE - 1);
        Assert.IsGreaterThan(box.Bounds.Width, x, "The line must reach past the viewport to be sliced.");
        Assert.AreEqual(LONG_LINE - 1, extent.GetOffsetForX(x));
    }

    [TestMethod]
    public void TheMappingsAreClampedAndMonotonic()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        ITextViewHost host = CreateBox(new string('x', LONG_LINE));
        var extent = host.GetLineExtent(0)!;

        Assert.AreEqual(0, extent.GetXForOffset(-5), 0.001);
        Assert.AreEqual(extent.Width, extent.GetXForOffset(LONG_LINE + 5), 0.001);
        Assert.AreEqual(0, extent.GetOffsetForX(-100));
        Assert.AreEqual(LONG_LINE, extent.GetOffsetForX(extent.Width * 2));

        double previous = -1;
        for (int offset = 0; offset <= LONG_LINE; offset += 137)
        {
            double x = extent.GetXForOffset(offset);
            Assert.IsGreaterThan(previous, x, $"x went backwards at offset {offset}.");
            previous = x;
        }
    }

    [TestMethod]
    public void AWrappingViewHasNoLineExtent()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var box = new MultiLineTextBox
        {
            Text = new string('x', LONG_LINE),
            FontFamily = "Consolas",
            FontSize = 13,
            Wrap = true
        };
        box.Measure(new Size(400, 200));
        box.Arrange(new Rect(0, 0, 400, 200));

        Assert.IsNull(((ITextViewHost)box).GetLineExtent(0),
            "A wrapping view measures a line in rows, so it has no x mapping for one.");
    }
}
