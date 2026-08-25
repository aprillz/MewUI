using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class TextViewHostTests
{
    [TestMethod]
    public void MultiLineTextBox_ExposesItsDocumentThroughTheHostContract()
    {
        var box = new MultiLineTextBox { Text = "hello" };
        ITextViewHost host = box;

        Assert.AreSame(box.Document, host.Document);
        Assert.AreSame(box.Extensions, host.Extensions);
        Assert.AreEqual("hello", host.Document.GetText(0, host.Document.TextLength));
    }

    [TestMethod]
    public void MultiLineTextBox_RaisesDocumentChangedOnEveryEdit()
    {
        var box = new MultiLineTextBox();
        ITextViewHost host = box;
        int raised = 0;
        host.DocumentChanged += sender =>
        {
            Assert.AreSame(host, sender);
            raised++;
        };

        box.Document.Insert(0, "abc");
        box.Document.Remove(0, 1);

        Assert.AreEqual(2, raised);
    }

    [TestMethod]
    public void SyntaxViewer_RaisesDocumentChangedWhenTextReplacesTheDocument()
    {
        var viewer = new SyntaxViewer();
        ITextViewHost host = viewer;
        int raised = 0;
        IReadOnlyTextDocument? observed = null;
        host.DocumentChanged += sender =>
        {
            observed = sender.Document;
            raised++;
        };

        viewer.Text = "line one\nline two";

        Assert.AreEqual(1, raised);
        Assert.AreSame(viewer.Document, observed);
        Assert.AreEqual(2, viewer.Document.LineCount);
    }

    /// <summary>
    /// A line outside the viewport is laid out on demand. Without this the only line measurements
    /// available are the ones already on screen.
    /// </summary>
    [TestMethod]
    public void MultiLineTextBox_LaysOutALineOutsideTheViewport()
    {
        var box = new MultiLineTextBox
        {
            Text = string.Join('\n', Enumerable.Range(1, 200).Select(number => $"line {number}"))
        };
        box.Measure(new Size(200, 60));
        box.Arrange(new Rect(0, 0, 200, 60));
        ITextViewHost host = box;
        var target = box.Document.GetLineByNumber(150);

        Assert.IsFalse(
            host.VisibleTextLines.Any(line => line.LogicalLine.LineNumber == target.LineNumber),
            "The line was already on screen, so the test proves nothing.");

        var layout = host.GetLineLayout(target.Offset);

        Assert.IsNotNull(layout);
        Assert.AreEqual(target.LineNumber, layout.LogicalLine.LineNumber);
        Assert.IsGreaterThan(0, layout.Height);
    }

    /// <summary>
    /// The horizontal extent follows the widest line laid out, which is what a host sizes its
    /// horizontal scrolling against.
    /// </summary>
    [TestMethod]
    public void MultiLineTextBox_ReportsTheWidthOfTheWidestLaidOutLine()
    {
        var box = new MultiLineTextBox { Wrap = false, Text = "short" };
        box.Measure(new Size(400, 100));
        box.Arrange(new Rect(0, 0, 400, 100));
        ITextViewHost host = box;
        double narrow = host.ExtentWidth;

        box.Text = "short\nthis line is a good deal longer than the first one";
        box.Measure(new Size(400, 100));
        box.Arrange(new Rect(0, 0, 400, 100));

        Assert.IsGreaterThan(0, narrow);
        Assert.IsGreaterThan(narrow, host.ExtentWidth);
    }

    /// <summary>
    /// The offset decides which line comes back, including one inside the line rather than at its
    /// start, which is what a caller asking about a position passes.
    /// </summary>
    [TestMethod]
    public void GetLineLayout_TakesTheLineTheOffsetFallsIn()
    {
        var box = new MultiLineTextBox { Text = "one\ntwo\nthree" };
        box.Measure(new Size(200, 60));
        box.Arrange(new Rect(0, 0, 200, 60));
        ITextViewHost host = box;

        var layout = host.GetLineLayout(box.Document.GetLineByNumber(2).Offset + 2);

        Assert.IsNotNull(layout);
        Assert.AreEqual(2, layout.LogicalLine.LineNumber);
    }
}
