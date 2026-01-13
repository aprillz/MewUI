using Aprillz.MewUI.Primitives;

namespace MewUI.Test.Primitives;

[TestClass]
public sealed class PointTests
{
    [TestMethod]
    public void Constructor_SetsXAndY()
    {
        var point = new Point(10, 20);

        Assert.AreEqual(10, point.X);
        Assert.AreEqual(20, point.Y);
    }

    [TestMethod]
    public void Zero_ReturnsOrigin()
    {
        Assert.AreEqual(0, Point.Zero.X);
        Assert.AreEqual(0, Point.Zero.Y);
    }

    [TestMethod]
    public void WithX_ReturnsNewPointWithModifiedX()
    {
        var point = new Point(10, 20);
        var modified = point.WithX(50);

        Assert.AreEqual(50, modified.X);
        Assert.AreEqual(20, modified.Y);
    }

    [TestMethod]
    public void WithY_ReturnsNewPointWithModifiedY()
    {
        var point = new Point(10, 20);
        var modified = point.WithY(50);

        Assert.AreEqual(10, modified.X);
        Assert.AreEqual(50, modified.Y);
    }

    [TestMethod]
    public void Offset_WithDoubles_ReturnsOffsetPoint()
    {
        var point = new Point(10, 20);
        var offset = point.Offset(5, 10);

        Assert.AreEqual(15, offset.X);
        Assert.AreEqual(30, offset.Y);
    }

    [TestMethod]
    public void Offset_WithVector_ReturnsOffsetPoint()
    {
        var point = new Point(10, 20);
        var vector = new Vector(5, 10);
        var offset = point.Offset(vector);

        Assert.AreEqual(15, offset.X);
        Assert.AreEqual(30, offset.Y);
    }

    [TestMethod]
    public void DistanceTo_ReturnsCorrectDistance()
    {
        var point1 = new Point(0, 0);
        var point2 = new Point(3, 4);

        Assert.AreEqual(5, point1.DistanceTo(point2));
    }

    [TestMethod]
    public void AddOperator_WithVector_ReturnsOffsetPoint()
    {
        var point = new Point(10, 20);
        var vector = new Vector(5, 10);
        var result = point + vector;

        Assert.AreEqual(new Point(15, 30), result);
    }

    [TestMethod]
    public void SubtractOperator_WithVector_ReturnsOffsetPoint()
    {
        var point = new Point(10, 20);
        var vector = new Vector(5, 10);
        var result = point - vector;

        Assert.AreEqual(new Point(5, 10), result);
    }

    [TestMethod]
    public void SubtractOperator_WithPoint_ReturnsVector()
    {
        var point1 = new Point(15, 30);
        var point2 = new Point(10, 20);
        var result = point1 - point2;

        Assert.AreEqual(new Vector(5, 10), result);
    }

    [TestMethod]
    public void MultiplyOperator_WithScalar_ReturnsScaledPoint()
    {
        var point = new Point(10, 20);
        var result = point * 2;

        Assert.AreEqual(new Point(20, 40), result);
    }

    [TestMethod]
    public void Equality_SameValues_ReturnsTrue()
    {
        var point1 = new Point(10, 20);
        var point2 = new Point(10, 20);

        Assert.IsTrue(point1 == point2);
        Assert.IsTrue(point1.Equals(point2));
    }

    [TestMethod]
    public void Equality_DifferentValues_ReturnsFalse()
    {
        var point1 = new Point(10, 20);
        var point2 = new Point(20, 10);

        Assert.IsTrue(point1 != point2);
        Assert.IsFalse(point1.Equals(point2));
    }

    [TestMethod]
    public void ToString_ReturnsFormattedString()
    {
        var point = new Point(10, 20);

        Assert.AreEqual("Point(10, 20)", point.ToString());
    }
}
