using Aprillz.MewUI.Primitives;

namespace MewUI.Test.Primitives;

[TestClass]
public sealed class RectTests
{
    [TestMethod]
    public void Constructor_WithComponents_SetsProperties()
    {
        var rect = new Rect(10, 20, 100, 200);

        Assert.AreEqual(10, rect.X);
        Assert.AreEqual(20, rect.Y);
        Assert.AreEqual(100, rect.Width);
        Assert.AreEqual(200, rect.Height);
    }

    [TestMethod]
    public void Constructor_WithNegativeSize_ClampsToZero()
    {
        var rect = new Rect(10, 20, -100, -200);

        Assert.AreEqual(0, rect.Width);
        Assert.AreEqual(0, rect.Height);
    }

    [TestMethod]
    public void Constructor_WithPointAndSize_SetsProperties()
    {
        var rect = new Rect(new Point(10, 20), new Size(100, 200));

        Assert.AreEqual(10, rect.X);
        Assert.AreEqual(20, rect.Y);
        Assert.AreEqual(100, rect.Width);
        Assert.AreEqual(200, rect.Height);
    }

    [TestMethod]
    public void Constructor_WithSize_SetsPositionToZero()
    {
        var rect = new Rect(new Size(100, 200));

        Assert.AreEqual(0, rect.X);
        Assert.AreEqual(0, rect.Y);
        Assert.AreEqual(100, rect.Width);
        Assert.AreEqual(200, rect.Height);
    }

    [TestMethod]
    public void EdgeProperties_ReturnCorrectValues()
    {
        var rect = new Rect(10, 20, 100, 200);

        Assert.AreEqual(10, rect.Left);
        Assert.AreEqual(20, rect.Top);
        Assert.AreEqual(110, rect.Right);
        Assert.AreEqual(220, rect.Bottom);
    }

    [TestMethod]
    public void CornerProperties_ReturnCorrectPoints()
    {
        var rect = new Rect(10, 20, 100, 200);

        Assert.AreEqual(new Point(10, 20), rect.TopLeft);
        Assert.AreEqual(new Point(110, 20), rect.TopRight);
        Assert.AreEqual(new Point(10, 220), rect.BottomLeft);
        Assert.AreEqual(new Point(110, 220), rect.BottomRight);
    }

    [TestMethod]
    public void Center_ReturnsCorrectPoint()
    {
        var rect = new Rect(0, 0, 100, 200);

        Assert.AreEqual(new Point(50, 100), rect.Center);
    }

    [TestMethod]
    public void Size_ReturnsCorrectSize()
    {
        var rect = new Rect(10, 20, 100, 200);

        Assert.AreEqual(new Size(100, 200), rect.Size);
    }

    [TestMethod]
    public void Position_ReturnsCorrectPoint()
    {
        var rect = new Rect(10, 20, 100, 200);

        Assert.AreEqual(new Point(10, 20), rect.Position);
    }

    [TestMethod]
    public void IsEmpty_WithZeroWidth_ReturnsTrue()
    {
        var rect = new Rect(10, 20, 0, 200);

        Assert.IsTrue(rect.IsEmpty);
    }

    [TestMethod]
    public void IsEmpty_WithZeroHeight_ReturnsTrue()
    {
        var rect = new Rect(10, 20, 100, 0);

        Assert.IsTrue(rect.IsEmpty);
    }

    [TestMethod]
    public void IsEmpty_WithNonZeroSize_ReturnsFalse()
    {
        var rect = new Rect(10, 20, 100, 200);

        Assert.IsFalse(rect.IsEmpty);
    }

    [TestMethod]
    public void Contains_PointInside_ReturnsTrue()
    {
        var rect = new Rect(0, 0, 100, 100);
        var point = new Point(50, 50);

        Assert.IsTrue(rect.Contains(point));
    }

    [TestMethod]
    public void Contains_PointOutside_ReturnsFalse()
    {
        var rect = new Rect(0, 0, 100, 100);
        var point = new Point(150, 50);

        Assert.IsFalse(rect.Contains(point));
    }

    [TestMethod]
    public void Contains_PointOnEdge_ReturnsTrueForTopLeft()
    {
        var rect = new Rect(0, 0, 100, 100);
        var point = new Point(0, 0);

        Assert.IsTrue(rect.Contains(point));
    }

    [TestMethod]
    public void Contains_RectInside_ReturnsTrue()
    {
        var outer = new Rect(0, 0, 100, 100);
        var inner = new Rect(25, 25, 50, 50);

        Assert.IsTrue(outer.Contains(inner));
    }

    [TestMethod]
    public void Contains_RectPartiallyInside_ReturnsFalse()
    {
        var outer = new Rect(0, 0, 100, 100);
        var partial = new Rect(50, 50, 100, 100);

        Assert.IsFalse(outer.Contains(partial));
    }

    [TestMethod]
    public void IntersectsWith_OverlappingRects_ReturnsTrue()
    {
        var rect1 = new Rect(0, 0, 100, 100);
        var rect2 = new Rect(50, 50, 100, 100);

        Assert.IsTrue(rect1.IntersectsWith(rect2));
    }

    [TestMethod]
    public void IntersectsWith_NonOverlappingRects_ReturnsFalse()
    {
        var rect1 = new Rect(0, 0, 100, 100);
        var rect2 = new Rect(200, 200, 100, 100);

        Assert.IsFalse(rect1.IntersectsWith(rect2));
    }

    [TestMethod]
    public void Intersect_ReturnsOverlappingArea()
    {
        var rect1 = new Rect(0, 0, 100, 100);
        var rect2 = new Rect(50, 50, 100, 100);
        var intersection = rect1.Intersect(rect2);

        Assert.AreEqual(new Rect(50, 50, 50, 50), intersection);
    }

    [TestMethod]
    public void Intersect_NoOverlap_ReturnsEmpty()
    {
        var rect1 = new Rect(0, 0, 100, 100);
        var rect2 = new Rect(200, 200, 100, 100);
        var intersection = rect1.Intersect(rect2);

        Assert.AreEqual(Rect.Empty, intersection);
    }

    [TestMethod]
    public void Union_ReturnsCombinedBounds()
    {
        var rect1 = new Rect(0, 0, 100, 100);
        var rect2 = new Rect(50, 50, 100, 100);
        var union = rect1.Union(rect2);

        Assert.AreEqual(new Rect(0, 0, 150, 150), union);
    }

    [TestMethod]
    public void Offset_ReturnsMovedRect()
    {
        var rect = new Rect(10, 20, 100, 200);
        var offset = rect.Offset(5, 10);

        Assert.AreEqual(new Rect(15, 30, 100, 200), offset);
    }

    [TestMethod]
    public void Inflate_WithDoubles_ReturnsExpandedRect()
    {
        var rect = new Rect(50, 50, 100, 100);
        var inflated = rect.Inflate(10, 20);

        Assert.AreEqual(40, inflated.X);
        Assert.AreEqual(30, inflated.Y);
        Assert.AreEqual(120, inflated.Width);
        Assert.AreEqual(140, inflated.Height);
    }

    [TestMethod]
    public void Deflate_WithThickness_ReturnsContractedRect()
    {
        var rect = new Rect(0, 0, 100, 100);
        var thickness = new Thickness(10);
        var deflated = rect.Deflate(thickness);

        Assert.AreEqual(new Rect(10, 10, 80, 80), deflated);
    }

    [TestMethod]
    public void WithMethods_ReturnModifiedRects()
    {
        var rect = new Rect(10, 20, 100, 200);

        Assert.AreEqual(50, rect.WithX(50).X);
        Assert.AreEqual(50, rect.WithY(50).Y);
        Assert.AreEqual(50, rect.WithWidth(50).Width);
        Assert.AreEqual(50, rect.WithHeight(50).Height);
    }

    [TestMethod]
    public void Equality_SameValues_ReturnsTrue()
    {
        var rect1 = new Rect(10, 20, 100, 200);
        var rect2 = new Rect(10, 20, 100, 200);

        Assert.IsTrue(rect1 == rect2);
        Assert.IsTrue(rect1.Equals(rect2));
    }

    [TestMethod]
    public void Equality_DifferentValues_ReturnsFalse()
    {
        var rect1 = new Rect(10, 20, 100, 200);
        var rect2 = new Rect(20, 10, 200, 100);

        Assert.IsTrue(rect1 != rect2);
        Assert.IsFalse(rect1.Equals(rect2));
    }

    [TestMethod]
    public void ToString_ReturnsFormattedString()
    {
        var rect = new Rect(10, 20, 100, 200);

        Assert.AreEqual("Rect(10, 20, 100, 200)", rect.ToString());
    }
}
