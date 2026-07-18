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
}
