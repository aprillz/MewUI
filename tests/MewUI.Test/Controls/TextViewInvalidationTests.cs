using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class TextViewInvalidationTests
{
    private const string WIDE_TEXT =
        "the quick brown fox jumps over the lazy dog and then keeps running far beyond the visible width";

    private sealed class CountingClassifier : ITextClassifier
    {
        public int Calls { get; private set; }

        public void Classify(in TextClassificationContext context, IList<TextPaintSpan> output) => Calls++;
    }

    [TestMethod]
    public void InvalidateTextViewKeepsHorizontalScroll()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var box = new MultiLineTextBox { Wrap = false, Width = 120, Height = 60, Text = WIDE_TEXT };
        window.Content = box;
        window.PerformLayout();

        box.CaretPosition = box.Document.TextLength;
        window.PerformLayout();
        Assert.IsGreaterThan(0, box.HorizontalOffset, "Caret move should have scrolled right.");
        double scrolled = box.HorizontalOffset;

        // Extensions re-running against unchanged text must not move the reader.
        box.InvalidateTextView();
        window.PerformLayout();

        Assert.AreEqual(scrolled, box.HorizontalOffset, 0.5);
    }

    [TestMethod]
    public void MetricChangeStillResetsHorizontalScroll()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var box = new MultiLineTextBox { Wrap = false, Width = 120, Height = 60, Text = "\t" + WIDE_TEXT };
        window.Content = box;
        window.PerformLayout();

        box.CaretPosition = box.Document.TextLength;
        window.PerformLayout();
        Assert.IsGreaterThan(0, box.HorizontalOffset);

        box.TabSize = 8;
        window.PerformLayout();

        Assert.AreEqual(0, box.HorizontalOffset, 0.5);
    }

    [TestMethod]
    public void SyntaxViewerInvalidateTextViewRerunsClassifiersAndKeepsScroll()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create();
        var viewer = new SyntaxViewer
        {
            Wrap = false,
            Width = 200,
            Height = 60,
            Text = string.Join('\n', Enumerable.Range(0, 200).Select(index => $"line {index}"))
        };
        var classifier = new CountingClassifier();
        viewer.Extensions.Classifiers.Add(classifier);
        window.Content = viewer;
        window.PerformLayout();

        viewer.Select(viewer.Document.TextLength, 0);
        window.PerformLayout();
        Assert.IsGreaterThan(0, viewer.VerticalOffset, "Selecting the last line should have scrolled down.");
        double scrolled = viewer.VerticalOffset;
        int before = classifier.Calls;

        viewer.InvalidateTextView();
        window.PerformLayout();

        Assert.IsGreaterThan(before, classifier.Calls, "Invalidation must re-run classifiers.");
        Assert.AreEqual(scrolled, viewer.VerticalOffset, 0.5);
    }
}
