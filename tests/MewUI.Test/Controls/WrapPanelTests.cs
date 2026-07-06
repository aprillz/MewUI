using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class WrapPanelTests
{
    [TestMethod]
    public void Arrange_HorizontalLine_UsesLineHeightForChildAlignment()
    {
        var small = new FixedSizeElement(new Size(20, 10))
        {
            VerticalAlignment = VerticalAlignment.Center,
        };
        var tall = new FixedSizeElement(new Size(20, 30));
        var panel = new WrapPanel();
        panel.AddRange(small, tall);

        panel.Measure(new Size(100, 100));
        panel.Arrange(new Rect(0, 0, 100, 100));

        Assert.AreEqual(new Rect(0, 10, 20, 10), small.Bounds);
        Assert.AreEqual(new Rect(20, 0, 20, 30), tall.Bounds);
    }

    private sealed class FixedSizeElement(Size desiredSize) : FrameworkElement
    {
        protected override Size MeasureContent(Size availableSize) => desiredSize;
    }
}
