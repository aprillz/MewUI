using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Rendering;

[TestClass]
public sealed class GeometryTests
{
    [TestMethod]
    public void PrimitiveGeometries_ProduceFrozenPaths()
    {
        var rectangle = new RectangleGeometry(new Rect(1, 2, 30, 40));
        var ellipse = new EllipseGeometry(new Rect(10, 20, 30, 40));
        var line = new LineGeometry(new Point(1, 2), new Point(3, 4));

        Assert.AreEqual(new Rect(1, 2, 30, 40), rectangle.GetPathGeometry().GetBounds());
        Assert.AreEqual(new Rect(10, 20, 30, 40), ellipse.GetPathGeometry().GetBounds());
        Assert.IsTrue(rectangle.GetPathGeometry().IsFrozen);
        Assert.IsTrue(ellipse.GetPathGeometry().IsFrozen);
        Assert.IsTrue(line.GetPathGeometry().IsFrozen);
    }

    [TestMethod]
    public void GeometryGroup_PreservesChildPathsAndFreezesChildren()
    {
        var rectangle = new RectangleGeometry(new Rect(0, 0, 10, 10));
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Add(rectangle);
        group.Add(new RectangleGeometry(new Rect(2, 2, 6, 6)));

        PathGeometry path = group.GetPathGeometry();

        Assert.AreEqual(FillRule.EvenOdd, path.FillRule);
        Assert.AreEqual(10, path.GetBounds().Width);

        group.Freeze();

        Assert.IsTrue(rectangle.IsFrozen);
        Assert.ThrowsExactly<InvalidOperationException>(() => group.Add(new LineGeometry()));
    }
}
