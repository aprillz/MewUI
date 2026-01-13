using Aprillz.MewUI.Primitives;

namespace MewUI.Test.Primitives;

[TestClass]
public sealed class VectorTests
{
    [TestMethod]
    public void Constructor_SetsXAndY()
    {
        var vector = new Vector(10, 20);

        Assert.AreEqual(10, vector.X);
        Assert.AreEqual(20, vector.Y);
    }

    [TestMethod]
    public void Zero_ReturnsZeroVector()
    {
        Assert.AreEqual(0, Vector.Zero.X);
        Assert.AreEqual(0, Vector.Zero.Y);
    }

    [TestMethod]
    public void One_ReturnsOneVector()
    {
        Assert.AreEqual(1, Vector.One.X);
        Assert.AreEqual(1, Vector.One.Y);
    }

    [TestMethod]
    public void Length_ReturnsCorrectValue()
    {
        var vector = new Vector(3, 4);

        Assert.AreEqual(5, vector.Length);
    }

    [TestMethod]
    public void LengthSquared_ReturnsCorrectValue()
    {
        var vector = new Vector(3, 4);

        Assert.AreEqual(25, vector.LengthSquared);
    }

    [TestMethod]
    public void Normalize_ReturnsUnitVector()
    {
        var vector = new Vector(3, 4);
        var normalized = vector.Normalize();

        Assert.AreEqual(1, normalized.Length, 0.0001);
        Assert.AreEqual(0.6, normalized.X, 0.0001);
        Assert.AreEqual(0.8, normalized.Y, 0.0001);
    }

    [TestMethod]
    public void Normalize_ZeroVector_ReturnsZero()
    {
        var vector = Vector.Zero;
        var normalized = vector.Normalize();

        Assert.AreEqual(Vector.Zero, normalized);
    }

    [TestMethod]
    public void Negate_ReturnsNegatedVector()
    {
        var vector = new Vector(10, 20);
        var negated = vector.Negate();

        Assert.AreEqual(new Vector(-10, -20), negated);
    }

    [TestMethod]
    public void AddOperator_AddsVectors()
    {
        var v1 = new Vector(10, 20);
        var v2 = new Vector(5, 10);
        var result = v1 + v2;

        Assert.AreEqual(new Vector(15, 30), result);
    }

    [TestMethod]
    public void SubtractOperator_SubtractsVectors()
    {
        var v1 = new Vector(10, 20);
        var v2 = new Vector(5, 10);
        var result = v1 - v2;

        Assert.AreEqual(new Vector(5, 10), result);
    }

    [TestMethod]
    public void MultiplyOperator_ScalesVector()
    {
        var vector = new Vector(10, 20);
        var result = vector * 2;

        Assert.AreEqual(new Vector(20, 40), result);
    }

    [TestMethod]
    public void DivideOperator_ScalesVector()
    {
        var vector = new Vector(10, 20);
        var result = vector / 2;

        Assert.AreEqual(new Vector(5, 10), result);
    }

    [TestMethod]
    public void NegateOperator_ReturnsNegatedVector()
    {
        var vector = new Vector(10, 20);
        var result = -vector;

        Assert.AreEqual(new Vector(-10, -20), result);
    }

    [TestMethod]
    public void Dot_ReturnsCorrectValue()
    {
        var v1 = new Vector(1, 2);
        var v2 = new Vector(3, 4);

        Assert.AreEqual(11, Vector.Dot(v1, v2));
    }

    [TestMethod]
    public void Cross_ReturnsCorrectValue()
    {
        var v1 = new Vector(1, 2);
        var v2 = new Vector(3, 4);

        Assert.AreEqual(-2, Vector.Cross(v1, v2));
    }

    [TestMethod]
    public void Equality_SameValues_ReturnsTrue()
    {
        var v1 = new Vector(10, 20);
        var v2 = new Vector(10, 20);

        Assert.IsTrue(v1 == v2);
        Assert.IsTrue(v1.Equals(v2));
    }

    [TestMethod]
    public void Equality_DifferentValues_ReturnsFalse()
    {
        var v1 = new Vector(10, 20);
        var v2 = new Vector(20, 10);

        Assert.IsTrue(v1 != v2);
        Assert.IsFalse(v1.Equals(v2));
    }

    [TestMethod]
    public void ToString_ReturnsFormattedString()
    {
        var vector = new Vector(10, 20);

        Assert.AreEqual("Vector(10, 20)", vector.ToString());
    }
}
