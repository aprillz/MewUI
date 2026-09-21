extern alias direct2d;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

using DWriteRendering = direct2d::Aprillz.MewUI.Rendering.DirectWrite;
using TextBitmap = direct2d::Aprillz.MewUI.Rendering.TextBitmap;

namespace MewUI.Test.Rendering;

[TestClass]
public sealed class DirectWriteTextRasterizerTests
{
    private const int WIDTH_PX = 160;
    private const int HEIGHT_PX = 40;

    [TestMethod]
    public void Rasterize_PutsInkInsideTheRequestedBox()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("DirectWrite is Windows-only.");
            return;
        }

        using var fonts = new DWriteRendering.DirectWriteFontFactory();
        var bitmap = Rasterize(fonts, "Hamburgefonstiv", TextAlignment.Left);

        Assert.AreEqual(WIDTH_PX, bitmap.WidthPx);
        Assert.AreEqual(HEIGHT_PX, bitmap.HeightPx);
        Assert.AreEqual(WIDTH_PX * HEIGHT_PX * 4, bitmap.Data.Length);
        Assert.IsGreaterThan(0, InkedPixels(bitmap), "The run should leave coverage in the box.");
    }

    [TestMethod]
    public void Rasterize_HonoursHorizontalAlignment()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("DirectWrite is Windows-only.");
            return;
        }

        using var fonts = new DWriteRendering.DirectWriteFontFactory();
        int leftEdge = FirstInkedColumn(Rasterize(fonts, "Hg", TextAlignment.Left));
        int rightEdge = FirstInkedColumn(Rasterize(fonts, "Hg", TextAlignment.Right));

        Assert.IsGreaterThanOrEqualTo(0, leftEdge);
        Assert.IsGreaterThan(leftEdge, rightEdge,
            "Trailing alignment should push the run towards the right edge of the box.");
    }

    [TestMethod]
    public void Rasterize_TintsCoverageWithTheRequestedColour()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("DirectWrite is Windows-only.");
            return;
        }

        using var fonts = new DWriteRendering.DirectWriteFontFactory();
        var bitmap = Rasterize(fonts, "Hg", TextAlignment.Left, Color.FromArgb(255, 255, 0, 0));

        for (int i = 0; i < bitmap.Data.Length; i += 4)
        {
            if (bitmap.Data[i + 3] == 0)
            {
                continue;
            }

            Assert.AreEqual(0, bitmap.Data[i], "Blue channel should stay clear for a red run.");
            Assert.AreEqual(0, bitmap.Data[i + 1], "Green channel should stay clear for a red run.");
            Assert.AreEqual(255, bitmap.Data[i + 2], "Red channel should carry the requested colour.");
            return;
        }

        Assert.Fail("The run produced no coverage to tint.");
    }

    [TestMethod]
    public void Rasterize_PaintsColourEmojiFromItsOwnLayers()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("DirectWrite is Windows-only.");
            return;
        }

        using var fonts = new DWriteRendering.DirectWriteFontFactory();
        var font = fonts.CreateFont("Segoe UI Emoji", 32, FontWeight.Normal, false, false, false, 96);

        // U+1F468 U+200D U+1F4BB, one glyph carrying many colour layers.
        var bitmap = DWriteRendering.DirectWriteTextRasterizer.Rasterize(
            fonts.Factory, font, "\U0001F468‍\U0001F4BB", 64, 48,
            Color.FromArgb(255, 255, 255, 255),
            TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap, TextTrimming.None,
            pixelsPerDip: 1f);

        var distinct = new HashSet<int>();
        for (int i = 0; i < bitmap.Data.Length; i += 4)
        {
            if (bitmap.Data[i + 3] != 0)
            {
                distinct.Add((bitmap.Data[i] << 16) | (bitmap.Data[i + 1] << 8) | bitmap.Data[i + 2]);
            }
        }

        Assert.IsGreaterThan(0, distinct.Count, "The emoji should leave coverage in the box.");
        Assert.IsGreaterThan(1, distinct.Count,
            "Colour layers should paint more than the single tint a monochrome run would produce.");
    }

    private static TextBitmap Rasterize(DWriteRendering.DirectWriteFontFactory fonts, string text,
        TextAlignment horizontalAlignment, Color? color = null)
    {
        var font = fonts.CreateFont("Segoe UI", 16, FontWeight.Normal, false, false, false, 96);
        return DWriteRendering.DirectWriteTextRasterizer.Rasterize(
            fonts.Factory, font, text, WIDTH_PX, HEIGHT_PX,
            color ?? Color.FromArgb(255, 255, 255, 255),
            horizontalAlignment, TextAlignment.Top,
            TextWrapping.NoWrap, TextTrimming.None, pixelsPerDip: 1f);
    }

    private static int InkedPixels(TextBitmap bitmap)
    {
        int count = 0;
        for (int i = 3; i < bitmap.Data.Length; i += 4)
        {
            if (bitmap.Data[i] != 0)
            {
                count++;
            }
        }

        return count;
    }

    private static int FirstInkedColumn(TextBitmap bitmap)
    {
        for (int x = 0; x < bitmap.WidthPx; x++)
        {
            for (int y = 0; y < bitmap.HeightPx; y++)
            {
                if (bitmap.Data[(((y * bitmap.WidthPx) + x) * 4) + 3] != 0)
                {
                    return x;
                }
            }
        }

        return -1;
    }
}
