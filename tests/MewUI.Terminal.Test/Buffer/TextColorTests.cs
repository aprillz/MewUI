// Ported from Windows Terminal src/buffer/out/ut_textbuffer/TextColorTests.cpp (MIT).

using Aprillz.MewUI.Terminal.Buffer;

using static Aprillz.MewUI.Terminal.Buffer.TextColor;

namespace MewUI.Terminal.Test.Buffer;

[TestClass]
public sealed class TextColorTests
{
    private static readonly uint _defaultFg = Rgb(1, 2, 3);
    private static readonly uint _defaultBg = Rgb(4, 5, 6);
    private const int DEFAULT_FG_INDEX = DEFAULT_FOREGROUND;
    private const int DEFAULT_BG_INDEX = DEFAULT_BACKGROUND;

    private readonly uint[] _colorTable = CreateColorTable();

    internal static uint[] CreateColorTable()
    {
        var table = new uint[TABLE_SIZE];
        table[0] = Rgb(12, 12, 12);
        table[1] = Rgb(0, 55, 218);
        table[2] = Rgb(19, 161, 14);
        table[3] = Rgb(58, 150, 221);
        table[4] = Rgb(197, 15, 31);
        table[5] = Rgb(136, 23, 152);
        table[6] = Rgb(193, 156, 0);
        table[7] = Rgb(204, 204, 204);
        table[8] = Rgb(118, 118, 118);
        table[9] = Rgb(59, 120, 255);
        table[10] = Rgb(22, 198, 12);
        table[11] = Rgb(97, 214, 214);
        table[12] = Rgb(231, 72, 86);
        table[13] = Rgb(180, 0, 158);
        table[14] = Rgb(249, 241, 165);
        table[15] = Rgb(242, 242, 242);
        table[DEFAULT_FG_INDEX] = _defaultFg;
        table[DEFAULT_BG_INDEX] = _defaultBg;
        return table;
    }

    [TestMethod]
    public void TestDefaultColor()
    {
        TextColor defaultColor = default;

        Assert.IsTrue(defaultColor.IsDefault());
        Assert.IsFalse(defaultColor.IsLegacy());
        Assert.IsFalse(defaultColor.IsRgb());

        Assert.AreEqual(_defaultFg, defaultColor.GetColor(_colorTable, DEFAULT_FG_INDEX, false));
        Assert.AreEqual(_defaultFg, defaultColor.GetColor(_colorTable, DEFAULT_FG_INDEX, true));
        Assert.AreEqual(_defaultBg, defaultColor.GetColor(_colorTable, DEFAULT_BG_INDEX, false));
        Assert.AreEqual(_defaultBg, defaultColor.GetColor(_colorTable, DEFAULT_BG_INDEX, true));
    }

    [TestMethod]
    public void TestDarkIndexColor()
    {
        var indexColor = new TextColor(7, false);

        Assert.IsFalse(indexColor.IsDefault());
        Assert.IsTrue(indexColor.IsLegacy());
        Assert.IsFalse(indexColor.IsRgb());

        Assert.AreEqual(_colorTable[7], indexColor.GetColor(_colorTable, DEFAULT_FG_INDEX, false));
        Assert.AreEqual(_colorTable[15], indexColor.GetColor(_colorTable, DEFAULT_FG_INDEX, true));
        Assert.AreEqual(_colorTable[7], indexColor.GetColor(_colorTable, DEFAULT_BG_INDEX, false));
        Assert.AreEqual(_colorTable[15], indexColor.GetColor(_colorTable, DEFAULT_BG_INDEX, true));
    }

    [TestMethod]
    public void TestBrightIndexColor()
    {
        var indexColor = new TextColor(15, false);

        Assert.IsFalse(indexColor.IsDefault());
        Assert.IsTrue(indexColor.IsLegacy());
        Assert.IsFalse(indexColor.IsRgb());

        Assert.AreEqual(_colorTable[15], indexColor.GetColor(_colorTable, DEFAULT_FG_INDEX, false));
        Assert.AreEqual(_colorTable[15], indexColor.GetColor(_colorTable, DEFAULT_FG_INDEX, true));
        Assert.AreEqual(_colorTable[15], indexColor.GetColor(_colorTable, DEFAULT_BG_INDEX, false));
        Assert.AreEqual(_colorTable[15], indexColor.GetColor(_colorTable, DEFAULT_BG_INDEX, true));
    }

    [TestMethod]
    public void TestRgbColor()
    {
        uint myColor = Rgb(7, 8, 9);
        var rgbColor = new TextColor(myColor);

        Assert.IsFalse(rgbColor.IsDefault());
        Assert.IsFalse(rgbColor.IsLegacy());
        Assert.IsTrue(rgbColor.IsRgb());

        Assert.AreEqual(myColor, rgbColor.GetColor(_colorTable, DEFAULT_FG_INDEX, false));
        Assert.AreEqual(myColor, rgbColor.GetColor(_colorTable, DEFAULT_FG_INDEX, true));
        Assert.AreEqual(myColor, rgbColor.GetColor(_colorTable, DEFAULT_BG_INDEX, false));
        Assert.AreEqual(myColor, rgbColor.GetColor(_colorTable, DEFAULT_BG_INDEX, true));
    }

    [TestMethod]
    public void TestChangeColor()
    {
        uint myColor = Rgb(7, 8, 9);
        var rgbColor = new TextColor(myColor);

        Assert.IsFalse(rgbColor.IsDefault());
        Assert.IsFalse(rgbColor.IsLegacy());
        Assert.IsTrue(rgbColor.IsRgb());

        Assert.AreEqual(myColor, rgbColor.GetColor(_colorTable, DEFAULT_FG_INDEX, false));
        Assert.AreEqual(myColor, rgbColor.GetColor(_colorTable, DEFAULT_FG_INDEX, true));
        Assert.AreEqual(myColor, rgbColor.GetColor(_colorTable, DEFAULT_BG_INDEX, false));
        Assert.AreEqual(myColor, rgbColor.GetColor(_colorTable, DEFAULT_BG_INDEX, true));

        rgbColor.SetDefault();
        Assert.AreEqual(_defaultFg, rgbColor.GetColor(_colorTable, DEFAULT_FG_INDEX, false));
        Assert.AreEqual(_defaultFg, rgbColor.GetColor(_colorTable, DEFAULT_FG_INDEX, true));
        Assert.AreEqual(_defaultBg, rgbColor.GetColor(_colorTable, DEFAULT_BG_INDEX, false));
        Assert.AreEqual(_defaultBg, rgbColor.GetColor(_colorTable, DEFAULT_BG_INDEX, true));

        rgbColor.SetIndex(7, false);
        Assert.AreEqual(_colorTable[7], rgbColor.GetColor(_colorTable, DEFAULT_FG_INDEX, false));
        Assert.AreEqual(_colorTable[15], rgbColor.GetColor(_colorTable, DEFAULT_FG_INDEX, true));
        Assert.AreEqual(_colorTable[7], rgbColor.GetColor(_colorTable, DEFAULT_BG_INDEX, false));
        Assert.AreEqual(_colorTable[15], rgbColor.GetColor(_colorTable, DEFAULT_BG_INDEX, true));

        rgbColor.SetIndex(15, false);
        Assert.AreEqual(_colorTable[15], rgbColor.GetColor(_colorTable, DEFAULT_FG_INDEX, false));
        Assert.AreEqual(_colorTable[15], rgbColor.GetColor(_colorTable, DEFAULT_FG_INDEX, true));
        Assert.AreEqual(_colorTable[15], rgbColor.GetColor(_colorTable, DEFAULT_BG_INDEX, false));
        Assert.AreEqual(_colorTable[15], rgbColor.GetColor(_colorTable, DEFAULT_BG_INDEX, true));
    }
}
