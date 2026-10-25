using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Elements;

/// <summary>An element arranged while its measure is stale is measured first, so arranging never leaves the two out of step.</summary>
[TestClass]
public sealed class ArrangeMeasureContractTests
{
    private const int LINE_COUNT = 400;

    [TestMethod]
    public void TextViewRebuiltWhileMeasureDirty_StillShowsItsLines()
    {
        var (window, text) = CreateCodeBlock();
        window.PerformLayout();
        Assert.AreEqual(LINE_COUNT, text.VisibleTextLines.Count);

        // A host that arranges a child it did not measure leaves it in this state.
        text.InvalidateMeasure();
        text.Arrange(text.Bounds);
        text.InvalidateTextView();
        window.PerformLayout();

        Assert.AreEqual(LINE_COUNT, text.VisibleTextLines.Count);
        Assert.IsFalse(text.IsMeasureDirty);
        Assert.IsFalse(text.IsArrangeDirty);
    }

    [TestMethod]
    public void TextViewRebuiltWhenClean_StillShowsItsLines()
    {
        var (window, text) = CreateCodeBlock();
        window.PerformLayout();

        text.InvalidateTextView();
        window.PerformLayout();

        Assert.AreEqual(LINE_COUNT, text.VisibleTextLines.Count);
    }

    [TestMethod]
    public void TextViewRebuiltFromItsOwnLinesChanged_StillShowsItsLines()
    {
        var (window, text) = CreateCodeBlock();
        bool rebuilt = false;
        // The view raises LinesChanged while the box arranges it; rebuilding there drops the view being arranged.
        text.LinesChanged += _ =>
        {
            if (!rebuilt)
            {
                rebuilt = true;
                text.InvalidateTextView();
            }
        };

        window.PerformLayout();

        Assert.IsTrue(rebuilt, "the view raised no LinesChanged during layout");
        Assert.AreEqual(LINE_COUNT, text.VisibleTextLines.Count);
        Assert.IsFalse(text.IsMeasureDirty);
        Assert.IsFalse(text.IsArrangeDirty);
    }

    [TestMethod]
    public void Arrange_OfMeasureDirtyElement_MeasuresWithTheLastConstraintFirst()
    {
        var element = new SizedElement { ContentSize = new Size(30, 10) };
        element.Measure(new Size(200, 100));
        element.Arrange(new Rect(0, 0, 200, 100));
        int measures = element.MeasureCount;

        element.ContentSize = new Size(60, 20);
        element.InvalidateMeasure();
        element.Arrange(new Rect(0, 0, 200, 100));

        Assert.AreEqual(measures + 1, element.MeasureCount);
        Assert.AreEqual(new Size(200, 100), element.LastConstraint);
        Assert.AreEqual(new Size(60, 20), element.DesiredSize);
        Assert.IsFalse(element.IsMeasureDirty);
        Assert.IsFalse(element.IsArrangeDirty);
    }

    [TestMethod]
    public void Arrange_OfNeverMeasuredElement_MeasuresWithTheArrangedSize()
    {
        var element = new SizedElement { ContentSize = new Size(30, 10) };

        element.Arrange(new Rect(5, 5, 50, 20));

        Assert.AreEqual(1, element.MeasureCount);
        Assert.AreEqual(new Size(50, 20), element.LastConstraint);
        Assert.IsFalse(element.IsMeasureDirty);
    }

    [TestMethod]
    public void Arrange_OfCleanElement_DoesNotMeasureAgain()
    {
        var element = new SizedElement { ContentSize = new Size(30, 10) };
        element.Measure(new Size(200, 100));
        element.Arrange(new Rect(0, 0, 200, 100));
        int measures = element.MeasureCount;

        element.InvalidateArrange();
        element.Arrange(new Rect(0, 0, 200, 100));

        Assert.AreEqual(measures, element.MeasureCount);
    }

    [TestMethod]
    public void HostArrangingAnUnmeasuredChild_MeasuresAndArrangesIt()
    {
        var measured = new SizedElement { ContentSize = new Size(40, 10) };
        var unmeasured = new SizedElement { ContentSize = new Size(40, 10) };
        var host = new FirstChildMeasuringHost();
        host.Add(measured);
        host.Add(unmeasured);
        var window = HeadlessWindow.Create(200, 100);
        window.Content = host;
        window.PerformLayout();
        int measures = unmeasured.MeasureCount;

        unmeasured.ContentSize = new Size(70, 10);
        unmeasured.InvalidateMeasure();
        window.PerformLayout();

        Assert.AreEqual(measures + 1, unmeasured.MeasureCount);
        Assert.AreEqual(new Size(70, 10), unmeasured.DesiredSize);
        Assert.IsFalse(unmeasured.IsMeasureDirty);
        Assert.IsFalse(unmeasured.IsArrangeDirty);
    }

    private static (Window Window, MultiLineTextBox Text) CreateCodeBlock()
    {
        string code = string.Join('\n', Enumerable.Range(0, LINE_COUNT)
            .Select(index => $"public static string Line{index}(int value) => \"line {index}: \" + value.ToString();"));
        var text = new MultiLineTextBox
        {
            Text = code,
            IsReadOnly = true,
            SizeToDocument = true,
            Wrap = false,
            FontFamily = "Consolas",
            FontSize = 13,
            BorderThickness = 0,
            Padding = Thickness.Zero,
        };
        var codeHost = new ScrollViewer
        {
            HorizontalScroll = ScrollMode.Auto,
            VerticalScroll = ScrollMode.Disabled,
            Content = text,
        };
        var panel = new StackPanel();
        panel.Add(new TextBlock { Text = "header" });
        panel.Add(new Border { Child = codeHost, BorderThickness = 1 });
        panel.Add(new TextBlock { Text = "footer" });
        var page = new ScrollViewer
        {
            HorizontalScroll = ScrollMode.Disabled,
            VerticalScroll = ScrollMode.Auto,
            Content = panel,
        };
        var window = HeadlessWindow.Create(1000, 700);
        window.Content = page;
        return (window, text);
    }

    private sealed class SizedElement : FrameworkElement
    {
        public Size ContentSize { get; set; }

        public int MeasureCount { get; private set; }

        public Size LastConstraint { get; private set; }

        protected override Size MeasureContent(Size availableSize)
        {
            MeasureCount++;
            LastConstraint = availableSize;
            return ContentSize;
        }
    }

    // Measures only its first child but arranges every child, as a virtualizing host does with a child kept out of range.
    private sealed class FirstChildMeasuringHost : Panel
    {
        protected override Size MeasureContent(Size availableSize)
        {
            if (Count == 0 || this[0] is not UIElement first)
            {
                return Size.Empty;
            }

            first.Measure(availableSize);
            return first.DesiredSize;
        }

        protected override void ArrangeContent(Rect bounds)
        {
            double top = bounds.Y;
            for (int index = 0; index < Count; index++)
            {
                if (this[index] is UIElement child)
                {
                    child.Arrange(new Rect(bounds.X, top, bounds.Width, 10));
                    top += 10;
                }
            }
        }
    }
}
