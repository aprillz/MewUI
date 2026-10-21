// Ported from Windows Terminal src/buffer/out/ut_textbuffer/TextAttributeTests.cpp (MIT).
// Not ported: TestRoundtripLegacy, TestRoundtripMetaBits, TestRoundtripExhaustive and TestRoundtripDefaultColors,
// which cover the console's 16-bit legacy attributes that this terminal does not keep.

using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Core;

using static Aprillz.MewUI.Terminal.Buffer.TextColor;

namespace MewUI.Terminal.Test.Buffer;

[TestClass]
public sealed class TextAttributeTests
{
    private static readonly uint _defaultFg = Rgb(1, 2, 3);
    private static readonly uint _defaultBg = Rgb(4, 5, 6);
    private const int DEFAULT_FG_INDEX = DEFAULT_FOREGROUND;
    private const int DEFAULT_BG_INDEX = DEFAULT_BACKGROUND;

    private readonly RenderSettings _renderSettings = CreateRenderSettings();

    private static RenderSettings CreateRenderSettings()
    {
        var settings = new RenderSettings();
        settings.SetColorAlias(ColorAlias.DefaultForeground, DEFAULT_FG_INDEX, _defaultFg);
        settings.SetColorAlias(ColorAlias.DefaultBackground, DEFAULT_BG_INDEX, _defaultBg);
        return settings;
    }

    [TestMethod]
    public void TestTextAttributeColorGetters()
    {
        var colorTable = _renderSettings.ColorTable.ToArray();
        uint red = Rgb(255, 0, 0);
        uint faintRed = Rgb(127, 0, 0);
        uint green = Rgb(0, 255, 0);
        var attr = new TextAttribute(red, green);

        Assert.IsFalse(attr.IsReverseVideo());
        Assert.AreEqual(red, attr.Foreground.GetColor(colorTable, DEFAULT_FG_INDEX));
        Assert.AreEqual(green, attr.Background.GetColor(colorTable, DEFAULT_BG_INDEX));
        Assert.AreEqual((red, green), _renderSettings.GetAttributeColors(attr));

        attr.SetReverseVideo(true);
        Assert.AreEqual(red, attr.Foreground.GetColor(colorTable, DEFAULT_FG_INDEX));
        Assert.AreEqual(green, attr.Background.GetColor(colorTable, DEFAULT_BG_INDEX));
        Assert.AreEqual((green, red), _renderSettings.GetAttributeColors(attr));

        attr.SetReverseVideo(false);
        attr.SetFaint(true);
        Assert.AreEqual(red, attr.Foreground.GetColor(colorTable, DEFAULT_FG_INDEX));
        Assert.AreEqual(green, attr.Background.GetColor(colorTable, DEFAULT_BG_INDEX));
        Assert.AreEqual((faintRed, green), _renderSettings.GetAttributeColors(attr));

        attr.SetReverseVideo(true);
        Assert.AreEqual(red, attr.Foreground.GetColor(colorTable, DEFAULT_FG_INDEX));
        Assert.AreEqual(green, attr.Background.GetColor(colorTable, DEFAULT_BG_INDEX));
        Assert.AreEqual((green, faintRed), _renderSettings.GetAttributeColors(attr));

        attr.SetReverseVideo(false);
        attr.SetFaint(false);
        attr.SetInvisible(true);
        Assert.AreEqual(red, attr.Foreground.GetColor(colorTable, DEFAULT_FG_INDEX));
        Assert.AreEqual(green, attr.Background.GetColor(colorTable, DEFAULT_BG_INDEX));
        Assert.AreEqual((green, green), _renderSettings.GetAttributeColors(attr));

        attr.SetReverseVideo(true);
        Assert.AreEqual(red, attr.Foreground.GetColor(colorTable, DEFAULT_FG_INDEX));
        Assert.AreEqual(green, attr.Background.GetColor(colorTable, DEFAULT_BG_INDEX));
        Assert.AreEqual((red, red), _renderSettings.GetAttributeColors(attr));
    }

    [TestMethod]
    public void TestReverseDefaultColors()
    {
        var colorTable = _renderSettings.ColorTable.ToArray();
        uint red = Rgb(255, 0, 0);
        uint green = Rgb(0, 255, 0);
        TextAttribute attr = default;

        Assert.IsFalse(attr.IsReverseVideo());
        Assert.AreEqual(_defaultFg, attr.Foreground.GetColor(colorTable, DEFAULT_FG_INDEX));
        Assert.AreEqual(_defaultBg, attr.Background.GetColor(colorTable, DEFAULT_BG_INDEX));
        Assert.AreEqual((_defaultFg, _defaultBg), _renderSettings.GetAttributeColors(attr));

        attr.SetReverseVideo(true);
        Assert.IsTrue(attr.IsReverseVideo());
        Assert.AreEqual(_defaultFg, attr.Foreground.GetColor(colorTable, DEFAULT_FG_INDEX));
        Assert.AreEqual(_defaultBg, attr.Background.GetColor(colorTable, DEFAULT_BG_INDEX));
        Assert.AreEqual((_defaultBg, _defaultFg), _renderSettings.GetAttributeColors(attr));

        attr.SetForeground(red);
        Assert.IsTrue(attr.IsReverseVideo());
        Assert.AreEqual(red, attr.Foreground.GetColor(colorTable, DEFAULT_FG_INDEX));
        Assert.AreEqual(_defaultBg, attr.Background.GetColor(colorTable, DEFAULT_BG_INDEX));
        Assert.AreEqual((_defaultBg, red), _renderSettings.GetAttributeColors(attr));

        attr.Invert();
        Assert.IsFalse(attr.IsReverseVideo());
        attr.SetDefaultForeground();
        attr.SetBackground(green);
        Assert.AreEqual(_defaultFg, attr.Foreground.GetColor(colorTable, DEFAULT_FG_INDEX));
        Assert.AreEqual(green, attr.Background.GetColor(colorTable, DEFAULT_BG_INDEX));
        Assert.AreEqual((_defaultFg, green), _renderSettings.GetAttributeColors(attr));
    }

    [TestMethod]
    public void TestIntenseAsBright()
    {
        var colorTable = _renderSettings.ColorTable.ToArray();
        uint darkBlack = colorTable[0];
        uint brightBlack = colorTable[8];
        uint darkGreen = colorTable[2];
        const RenderSettings.Mode INTENSE_IS_BRIGHT = RenderSettings.Mode.IntenseIsBright;

        TextAttribute attr = default;

        Assert.IsFalse(attr.IsIntense());
        Assert.AreEqual(_defaultFg, attr.Foreground.GetColor(colorTable, DEFAULT_FG_INDEX));
        Assert.AreEqual(_defaultBg, attr.Background.GetColor(colorTable, DEFAULT_BG_INDEX));
        _renderSettings.SetRenderMode(INTENSE_IS_BRIGHT, true);
        Assert.AreEqual((_defaultFg, _defaultBg), _renderSettings.GetAttributeColors(attr));
        _renderSettings.SetRenderMode(INTENSE_IS_BRIGHT, false);
        Assert.AreEqual((_defaultFg, _defaultBg), _renderSettings.GetAttributeColors(attr));

        attr.SetIntense(true);
        Assert.IsTrue(attr.IsIntense());
        _renderSettings.SetRenderMode(INTENSE_IS_BRIGHT, true);
        Assert.AreEqual((_defaultFg, _defaultBg), _renderSettings.GetAttributeColors(attr));
        _renderSettings.SetRenderMode(INTENSE_IS_BRIGHT, false);
        Assert.AreEqual((_defaultFg, _defaultBg), _renderSettings.GetAttributeColors(attr));

        attr.SetIndexedForeground(DARK_BLACK);
        Assert.IsTrue(attr.IsIntense());
        _renderSettings.SetRenderMode(INTENSE_IS_BRIGHT, true);
        Assert.AreEqual((brightBlack, _defaultBg), _renderSettings.GetAttributeColors(attr), "bright black when intense is bright");
        _renderSettings.SetRenderMode(INTENSE_IS_BRIGHT, false);
        Assert.AreEqual((darkBlack, _defaultBg), _renderSettings.GetAttributeColors(attr), "dark black when intense is not bright");

        attr.SetIndexedBackground(DARK_GREEN);
        Assert.IsTrue(attr.IsIntense());
        _renderSettings.SetRenderMode(INTENSE_IS_BRIGHT, true);
        Assert.AreEqual((brightBlack, darkGreen), _renderSettings.GetAttributeColors(attr), "the background is unaffected");
        _renderSettings.SetRenderMode(INTENSE_IS_BRIGHT, false);
        Assert.AreEqual((darkBlack, darkGreen), _renderSettings.GetAttributeColors(attr));

        attr.SetIntense(false);
        Assert.IsFalse(attr.IsIntense());
        _renderSettings.SetRenderMode(INTENSE_IS_BRIGHT, true);
        Assert.AreEqual((darkBlack, darkGreen), _renderSettings.GetAttributeColors(attr), "not intense: nothing changes");
        _renderSettings.SetRenderMode(INTENSE_IS_BRIGHT, false);
        Assert.AreEqual((darkBlack, darkGreen), _renderSettings.GetAttributeColors(attr));

        attr.SetIntense(true);
        attr.SetIndexedForeground(BRIGHT_BLACK);
        Assert.IsTrue(attr.IsIntense());
        _renderSettings.SetRenderMode(INTENSE_IS_BRIGHT, true);
        Assert.AreEqual((brightBlack, darkGreen), _renderSettings.GetAttributeColors(attr), "already bright: nothing changes");
        _renderSettings.SetRenderMode(INTENSE_IS_BRIGHT, false);
        Assert.AreEqual((brightBlack, darkGreen), _renderSettings.GetAttributeColors(attr));
    }
}
