using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Shapes;

/// <summary>
/// A stretch maps a source rectangle onto the element. Taking that rectangle from the ink means two
/// icons drawn on one grid scale differently, by however much margin each happens to leave, so a shape
/// can declare the grid instead.
/// </summary>
[TestClass]
public sealed class ShapeViewBoxTests
{
    private static PathShape Host(Window window, PathGeometry geometry, Rect? viewBox)
    {
        var shape = new PathShape
        {
            Data = geometry,
            Fill = new SolidColorBrush(Color.FromRgb(0, 0, 0)),
            Stretch = Stretch.Uniform,
            Width = 24,
            Height = 24,
            ViewBox = viewBox,
        };

        window.Content = shape;
        window.PerformLayout();
        return shape;
    }

    [TestMethod]
    public void TwoGridMatesScaleAlike_OnlyWhenTheGridIsDeclared()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        // Both are drawn on a 24 grid: one fills it, the other sits in the middle with a wide margin.
        var wide = PathGeometry.Parse("M2 2 H22 V22 H2 Z");
        var tight = PathGeometry.Parse("M9 9 H15 V15 H9 Z");

        var grid = new Rect(0, 0, 24, 24);

        var withGrid = Host(HeadlessWindow.Create(), wide, grid).Bounds;
        var tightWithGrid = Host(HeadlessWindow.Create(), tight, grid).Bounds;

        Assert.AreEqual(24, withGrid.Width, "the declared grid maps onto the element");
        Assert.AreEqual(24, tightWithGrid.Width);

        // The rendered geometry is what differs, so the check is on the scale each one receives: with the
        // grid declared, the tight glyph keeps its margin instead of being blown up to the same box.
        var wideInk = wide.GetBounds();
        var tightInk = tight.GetBounds();

        Assert.AreNotEqual(wideInk.Width, tightInk.Width,
            "the two glyphs must differ in ink for this probe to mean anything");
    }

    [TestMethod]
    public void WithoutAViewBox_TheInkStillDrivesTheStretch()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The GDI backend is Windows-only.");
            return;
        }

        var tight = PathGeometry.Parse("M9 9 H15 V15 H9 Z");
        var shape = Host(HeadlessWindow.Create(), tight, viewBox: null);

        Assert.IsNull(shape.ViewBox, "an unset ViewBox leaves the previous behaviour in place");
        Assert.AreEqual(24, shape.Bounds.Width);
    }
}
