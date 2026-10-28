using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Rendering;

/// <summary>
/// Path data may start with the XAML fill rule prefix: F0 is even-odd, F1 is non-zero. Data without it
/// parses exactly as before, with the non-zero rule.
/// </summary>
[TestClass]
public sealed class PathGeometryParseTests
{
    private const string SQUARE = "M0,0 L10,0 L10,10 L0,10 Z";

    [TestMethod]
    [DataRow("F0 " + SQUARE, FillRule.EvenOdd)]
    [DataRow("F1 " + SQUARE, FillRule.NonZero)]
    [DataRow("  F0M0,0 L10,0 L10,10 L0,10 Z", FillRule.EvenOdd)]
    [DataRow("F 0 " + SQUARE, FillRule.EvenOdd)]
    [DataRow(SQUARE, FillRule.NonZero)]
    public void Prefix_SetsTheFillRule(string data, FillRule expected)
    {
        var path = PathGeometry.Parse(data);

        Assert.AreEqual(expected, path.FillRule);
        Assert.IsTrue(path.IsFrozen);
    }

    [TestMethod]
    [DataRow("F0 ")]
    [DataRow("F1 ")]
    public void Prefix_LeavesTheCommandsUnchanged(string prefix)
    {
        var plain = PathGeometry.Parse(SQUARE);
        var prefixed = PathGeometry.Parse(prefix + SQUARE);

        CollectionAssert.AreEqual(plain.Commands.ToArray(), prefixed.Commands.ToArray());
    }

    [TestMethod]
    public void NoPrefix_ParsesAsBefore()
    {
        var path = PathGeometry.Parse("m 5 5 h 10 v 10 h -10 z");

        Assert.AreEqual(FillRule.NonZero, path.FillRule);
        Assert.AreEqual(new Rect(5, 5, 10, 10), path.GetBounds());
    }

    [TestMethod]
    public void EvenOdd_ReachesTheFrozenPath()
    {
        var path = PathGeometry.Parse("F0 M0,0 L20,0 L20,20 L0,20 Z M5,5 L15,5 L15,15 L5,15 Z");

        Assert.AreEqual(FillRule.EvenOdd, path.GetPathGeometry().FillRule);
        Assert.ThrowsExactly<InvalidOperationException>(() => path.FillRule = FillRule.NonZero);
    }
}
