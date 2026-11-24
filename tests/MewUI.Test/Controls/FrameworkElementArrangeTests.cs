using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class FrameworkElementArrangeTests
{
    [TestMethod]
    public void Arrange_AutoStretch_FillsAvailableSlot()
    {
        var element = new Border();

        element.Measure(new Size(100, 80));
        element.Arrange(new Rect(0, 0, 100, 80));

        Assert.AreEqual(new Rect(0, 0, 100, 80), element.Bounds);
    }

    [TestMethod]
    public void Arrange_ExplicitSizeLargerThanSlot_PreservesSize()
    {
        var element = new Border
        {
            Width = 150,
            Height = 120,
        };

        element.Measure(new Size(100, 80));
        element.Arrange(new Rect(0, 0, 100, 80));

        Assert.AreEqual(new Rect(0, 0, 150, 120), element.Bounds);
    }

    [TestMethod]
    public void Arrange_NonStretchDesiredSizeLargerThanSlot_PreservesSize()
    {
        var element = new Border
        {
            MinWidth = 150,
            MinHeight = 120,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        element.Measure(new Size(100, 80));
        element.Arrange(new Rect(0, 0, 100, 80));

        Assert.AreEqual(new Rect(0, 0, 150, 120), element.Bounds);
    }

    [TestMethod]
    public void Arrange_SlotLargerThanMeasureConstraint_RestoresClippedDesiredSize()
    {
        var element = new FixedContentElement(new Size(20, 48)) { VerticalAlignment = VerticalAlignment.Top };

        element.Measure(new Size(100, 30));
        element.Arrange(new Rect(0, 0, 100, 70));

        Assert.AreEqual(30, element.DesiredSize.Height);
        Assert.AreEqual(48, element.Bounds.Height);
    }

    [TestMethod]
    public void Arrange_SlotBetweenConstraintAndDesiredSize_StaysInsideSlot()
    {
        var element = new FixedContentElement(new Size(20, 48)) { VerticalAlignment = VerticalAlignment.Top };

        element.Measure(new Size(100, 30));
        element.Arrange(new Rect(0, 0, 100, 40));

        Assert.AreEqual(40, element.Bounds.Height);
    }

    [TestMethod]
    public void Arrange_RowSpanOverAutoAndStarRows_KeepsChildNaturalHeight()
    {
        var spanning = new FixedContentElement(new Size(20, 48)) { VerticalAlignment = VerticalAlignment.Top };
        var grid = new Grid()
            .Rows("Auto,Auto,*")
            .Children(
                new Border { Height = 20 }.Row(0),
                new Border { Height = 20 }.Row(1),
                spanning.Row(0).RowSpan(3));

        grid.Measure(new Size(100, 70));
        grid.Arrange(new Rect(0, 0, 100, 70));

        Assert.AreEqual(48, spanning.Bounds.Height);
    }

    // Measures to the same size whatever the constraint, as a line of text that cannot wrap does.
    private sealed class FixedContentElement(Size contentSize) : FrameworkElement
    {
        protected override Size MeasureContent(Size availableSize) => contentSize;
    }
}
