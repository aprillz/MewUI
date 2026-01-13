using Aprillz.MewUI.Primitives;

namespace MewUI.Test.Primitives;

[TestClass]
public sealed class ColorTests
{
    [TestMethod]
    public void Constructor_WithArgb_SetsComponentsCorrectly()
    {
        var color = new Color(128, 255, 100, 50);

        Assert.AreEqual((byte)128, color.A);
        Assert.AreEqual((byte)255, color.R);
        Assert.AreEqual((byte)100, color.G);
        Assert.AreEqual((byte)50, color.B);
    }

    [TestMethod]
    public void Constructor_WithRgb_SetsAlphaTo255()
    {
        var color = new Color(255, 100, 50);

        Assert.AreEqual((byte)255, color.A);
        Assert.AreEqual((byte)255, color.R);
        Assert.AreEqual((byte)100, color.G);
        Assert.AreEqual((byte)50, color.B);
    }

    [TestMethod]
    public void FromHex_With6Digits_ParsesCorrectly()
    {
        var color = Color.FromHex("#FF8040");

        Assert.AreEqual((byte)255, color.A);
        Assert.AreEqual((byte)255, color.R);
        Assert.AreEqual((byte)128, color.G);
        Assert.AreEqual((byte)64, color.B);
    }

    [TestMethod]
    public void FromHex_With8Digits_ParsesCorrectly()
    {
        var color = Color.FromHex("#80FF8040");

        Assert.AreEqual((byte)128, color.A);
        Assert.AreEqual((byte)255, color.R);
        Assert.AreEqual((byte)128, color.G);
        Assert.AreEqual((byte)64, color.B);
    }

    [TestMethod]
    public void FromHex_WithoutHashSign_ParsesCorrectly()
    {
        var color = Color.FromHex("FF0000");

        Assert.AreEqual((byte)255, color.R);
        Assert.AreEqual((byte)0, color.G);
        Assert.AreEqual((byte)0, color.B);
    }

    [TestMethod]
    public void FromHex_WithInvalidFormat_ThrowsArgumentException()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Color.FromHex("#FFF"));
    }

    [TestMethod]
    public void ToArgb_ReturnsCorrectValue()
    {
        var color = new Color(128, 255, 100, 50);
        uint expected = 0x80FF6432;

        Assert.AreEqual(expected, color.ToArgb());
    }

    [TestMethod]
    public void ToCOLORREF_ReturnsBgrFormat()
    {
        var color = new Color(255, 100, 50);
        uint expected = ((uint)50 << 16) | ((uint)100 << 8) | 255;

        Assert.AreEqual(expected, color.ToCOLORREF());
    }

    [TestMethod]
    public void WithAlpha_ReturnsNewColorWithModifiedAlpha()
    {
        var color = new Color(255, 100, 50);
        var modified = color.WithAlpha(128);

        Assert.AreEqual((byte)128, modified.A);
        Assert.AreEqual(color.R, modified.R);
        Assert.AreEqual(color.G, modified.G);
        Assert.AreEqual(color.B, modified.B);
    }

    [TestMethod]
    public void Lerp_AtZero_ReturnsOriginalColor()
    {
        var color1 = Color.Black;
        var color2 = Color.White;

        var result = color1.Lerp(color2, 0);

        Assert.AreEqual(color1, result);
    }

    [TestMethod]
    public void Lerp_AtOne_ReturnsTargetColor()
    {
        var color1 = Color.Black;
        var color2 = Color.White;

        var result = color1.Lerp(color2, 1);

        Assert.AreEqual(color2, result);
    }

    [TestMethod]
    public void Lerp_AtHalf_ReturnsMiddleColor()
    {
        var color1 = new Color(0, 0, 0);
        var color2 = new Color(200, 100, 50);

        var result = color1.Lerp(color2, 0.5);

        Assert.AreEqual((byte)100, result.R);
        Assert.AreEqual((byte)50, result.G);
        Assert.AreEqual((byte)25, result.B);
    }

    [TestMethod]
    public void Equality_SameValues_ReturnsTrue()
    {
        var color1 = new Color(128, 255, 100, 50);
        var color2 = new Color(128, 255, 100, 50);

        Assert.IsTrue(color1 == color2);
        Assert.IsTrue(color1.Equals(color2));
    }

    [TestMethod]
    public void Equality_DifferentValues_ReturnsFalse()
    {
        var color1 = Color.Red;
        var color2 = Color.Blue;

        Assert.IsTrue(color1 != color2);
        Assert.IsFalse(color1.Equals(color2));
    }

    [TestMethod]
    public void StaticColors_HaveCorrectValues()
    {
        Assert.AreEqual(new Color(0, 0, 0, 0), Color.Transparent);
        Assert.AreEqual(new Color(0, 0, 0), Color.Black);
        Assert.AreEqual(new Color(255, 255, 255), Color.White);
        Assert.AreEqual(new Color(255, 0, 0), Color.Red);
        Assert.AreEqual(new Color(0, 0, 255), Color.Blue);
    }
}
