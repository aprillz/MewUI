using Aprillz.MewUI.Primitives;

namespace MewUI.Test.Primitives;

[TestClass]
public sealed class ThicknessTests
{
    [TestMethod]
    public void Constructor_Uniform_SetsSameValueToAll()
    {
        var thickness = new Thickness(10);

        Assert.AreEqual(10, thickness.Left);
        Assert.AreEqual(10, thickness.Top);
        Assert.AreEqual(10, thickness.Right);
        Assert.AreEqual(10, thickness.Bottom);
    }

    [TestMethod]
    public void Constructor_HorizontalVertical_SetsPairs()
    {
        var thickness = new Thickness(10, 20);

        Assert.AreEqual(10, thickness.Left);
        Assert.AreEqual(20, thickness.Top);
        Assert.AreEqual(10, thickness.Right);
        Assert.AreEqual(20, thickness.Bottom);
    }

    [TestMethod]
    public void Constructor_AllFour_SetsIndividualValues()
    {
        var thickness = new Thickness(1, 2, 3, 4);

        Assert.AreEqual(1, thickness.Left);
        Assert.AreEqual(2, thickness.Top);
        Assert.AreEqual(3, thickness.Right);
        Assert.AreEqual(4, thickness.Bottom);
    }

    [TestMethod]
    public void Zero_HasZeroThickness()
    {
        Assert.AreEqual(0, Thickness.Zero.Left);
        Assert.AreEqual(0, Thickness.Zero.Top);
        Assert.AreEqual(0, Thickness.Zero.Right);
        Assert.AreEqual(0, Thickness.Zero.Bottom);
    }

    [TestMethod]
    public void HorizontalThickness_ReturnsSumOfLeftAndRight()
    {
        var thickness = new Thickness(10, 20, 30, 40);

        Assert.AreEqual(40, thickness.HorizontalThickness);
    }

    [TestMethod]
    public void VerticalThickness_ReturnsSumOfTopAndBottom()
    {
        var thickness = new Thickness(10, 20, 30, 40);

        Assert.AreEqual(60, thickness.VerticalThickness);
    }

    [TestMethod]
    public void IsUniform_WithUniformValues_ReturnsTrue()
    {
        var thickness = new Thickness(10);

        Assert.IsTrue(thickness.IsUniform);
    }

    [TestMethod]
    public void IsUniform_WithDifferentValues_ReturnsFalse()
    {
        var thickness = new Thickness(10, 20, 30, 40);

        Assert.IsFalse(thickness.IsUniform);
    }

    [TestMethod]
    public void AddOperator_AddsThicknesses()
    {
        var t1 = new Thickness(1, 2, 3, 4);
        var t2 = new Thickness(10, 20, 30, 40);
        var result = t1 + t2;

        Assert.AreEqual(new Thickness(11, 22, 33, 44), result);
    }

    [TestMethod]
    public void SubtractOperator_SubtractsThicknesses()
    {
        var t1 = new Thickness(10, 20, 30, 40);
        var t2 = new Thickness(1, 2, 3, 4);
        var result = t1 - t2;

        Assert.AreEqual(new Thickness(9, 18, 27, 36), result);
    }

    [TestMethod]
    public void MultiplyOperator_ScalesThickness()
    {
        var thickness = new Thickness(10, 20, 30, 40);
        var result = thickness * 2;

        Assert.AreEqual(new Thickness(20, 40, 60, 80), result);
    }

    [TestMethod]
    public void Equality_SameValues_ReturnsTrue()
    {
        var t1 = new Thickness(10, 20, 30, 40);
        var t2 = new Thickness(10, 20, 30, 40);

        Assert.IsTrue(t1 == t2);
        Assert.IsTrue(t1.Equals(t2));
    }

    [TestMethod]
    public void Equality_DifferentValues_ReturnsFalse()
    {
        var t1 = new Thickness(10);
        var t2 = new Thickness(20);

        Assert.IsTrue(t1 != t2);
        Assert.IsFalse(t1.Equals(t2));
    }

    [TestMethod]
    public void ToString_Uniform_ReturnsSimpleFormat()
    {
        var thickness = new Thickness(10);

        Assert.AreEqual("Thickness(10)", thickness.ToString());
    }

    [TestMethod]
    public void ToString_NonUniform_ReturnsFullFormat()
    {
        var thickness = new Thickness(10, 20, 30, 40);

        Assert.AreEqual("Thickness(10, 20, 30, 40)", thickness.ToString());
    }
}
