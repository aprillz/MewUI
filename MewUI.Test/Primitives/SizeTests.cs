using Aprillz.MewUI.Primitives;

namespace MewUI.Test.Primitives;

[TestClass]
public sealed class SizeTests
{
    [TestMethod]
    public void Constructor_SetsWidthAndHeight()
    {
        var size = new Size(100, 200);

        Assert.AreEqual(100, size.Width);
        Assert.AreEqual(200, size.Height);
    }

    [TestMethod]
    public void Constructor_WithNegativeValues_ClampsToZero()
    {
        var size = new Size(-10, -20);

        Assert.AreEqual(0, size.Width);
        Assert.AreEqual(0, size.Height);
    }

    [TestMethod]
    public void Empty_HasZeroDimensions()
    {
        Assert.AreEqual(0, Size.Empty.Width);
        Assert.AreEqual(0, Size.Empty.Height);
        Assert.IsTrue(Size.Empty.IsEmpty);
    }

    [TestMethod]
    public void Infinity_HasPositiveInfinityDimensions()
    {
        Assert.AreEqual(double.PositiveInfinity, Size.Infinity.Width);
        Assert.AreEqual(double.PositiveInfinity, Size.Infinity.Height);
    }

    [TestMethod]
    public void IsEmpty_WithNonZeroSize_ReturnsFalse()
    {
        var size = new Size(100, 200);

        Assert.IsFalse(size.IsEmpty);
    }

    [TestMethod]
    public void WithWidth_ReturnsNewSizeWithModifiedWidth()
    {
        var size = new Size(100, 200);
        var modified = size.WithWidth(50);

        Assert.AreEqual(50, modified.Width);
        Assert.AreEqual(200, modified.Height);
    }

    [TestMethod]
    public void WithHeight_ReturnsNewSizeWithModifiedHeight()
    {
        var size = new Size(100, 200);
        var modified = size.WithHeight(50);

        Assert.AreEqual(100, modified.Width);
        Assert.AreEqual(50, modified.Height);
    }

    [TestMethod]
    public void Constrain_LimitsToConstraint()
    {
        var size = new Size(200, 300);
        var constraint = new Size(100, 150);
        var constrained = size.Constrain(constraint);

        Assert.AreEqual(100, constrained.Width);
        Assert.AreEqual(150, constrained.Height);
    }

    [TestMethod]
    public void Constrain_WhenSmallerThanConstraint_ReturnsSameSize()
    {
        var size = new Size(50, 75);
        var constraint = new Size(100, 150);
        var constrained = size.Constrain(constraint);

        Assert.AreEqual(50, constrained.Width);
        Assert.AreEqual(75, constrained.Height);
    }

    [TestMethod]
    public void Deflate_ReducesSizeByThickness()
    {
        var size = new Size(100, 100);
        var thickness = new Thickness(10, 20, 10, 20);
        var deflated = size.Deflate(thickness);

        Assert.AreEqual(80, deflated.Width);
        Assert.AreEqual(60, deflated.Height);
    }

    [TestMethod]
    public void Inflate_IncreasesSizeByThickness()
    {
        var size = new Size(100, 100);
        var thickness = new Thickness(10, 20, 10, 20);
        var inflated = size.Inflate(thickness);

        Assert.AreEqual(120, inflated.Width);
        Assert.AreEqual(140, inflated.Height);
    }

    [TestMethod]
    public void AddOperator_AddsSizes()
    {
        var size1 = new Size(100, 200);
        var size2 = new Size(50, 75);
        var result = size1 + size2;

        Assert.AreEqual(new Size(150, 275), result);
    }

    [TestMethod]
    public void SubtractOperator_SubtractsSizes()
    {
        var size1 = new Size(100, 200);
        var size2 = new Size(50, 75);
        var result = size1 - size2;

        Assert.AreEqual(new Size(50, 125), result);
    }

    [TestMethod]
    public void MultiplyOperator_ScalesSize()
    {
        var size = new Size(100, 200);
        var result = size * 2;

        Assert.AreEqual(new Size(200, 400), result);
    }

    [TestMethod]
    public void DivideOperator_ScalesSize()
    {
        var size = new Size(100, 200);
        var result = size / 2;

        Assert.AreEqual(new Size(50, 100), result);
    }

    [TestMethod]
    public void Equality_SameValues_ReturnsTrue()
    {
        var size1 = new Size(100, 200);
        var size2 = new Size(100, 200);

        Assert.IsTrue(size1 == size2);
        Assert.IsTrue(size1.Equals(size2));
    }

    [TestMethod]
    public void Equality_DifferentValues_ReturnsFalse()
    {
        var size1 = new Size(100, 200);
        var size2 = new Size(200, 100);

        Assert.IsTrue(size1 != size2);
        Assert.IsFalse(size1.Equals(size2));
    }

    [TestMethod]
    public void ToString_ReturnsFormattedString()
    {
        var size = new Size(100, 200);

        Assert.AreEqual("Size(100, 200)", size.ToString());
    }
}
