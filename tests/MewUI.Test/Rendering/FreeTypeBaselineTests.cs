extern alias MewVGX11;

using Aprillz.MewUI;

using FreeTypeFont = MewVGX11::Aprillz.MewUI.Rendering.FreeType.FreeTypeFont;
using FreeTypeText = MewVGX11::Aprillz.MewUI.Rendering.FreeType.FreeTypeText;
using LinuxFontResolver = MewVGX11::Aprillz.MewUI.Rendering.FreeType.LinuxFontResolver;
using TextBitmap = MewVGX11::Aprillz.MewUI.Rendering.TextBitmap;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class FreeTypeBaselineTests
{
    [TestMethod]
    public void Rasterize_CapitalBaselineMatchesLayoutAscent()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("FreeType rasterization is Linux-only.");
            return;
        }

        const double size = 16;
        const int pixelHeight = 16;
        string? path = LinuxFontResolver.ResolveFontPath("Noto Sans", FontWeight.Normal, italic: false);
        Assert.IsNotNull(path, "No usable Linux font was found.");

        using var font = new FreeTypeFont(
            "Noto Sans", size, FontWeight.Normal,
            italic: false, underline: false, strikethrough: false,
            path, pixelHeight);

        double dpiScale = pixelHeight / size;
        double ascentPx = font.Ascent * dpiScale;
        var measured = FreeTypeText.Measure("Hg", font);
        Assert.IsGreaterThanOrEqualTo(
            (font.Ascent + font.Descent + font.InternalLeading) * dpiScale,
            measured.Height,
            "The raster box must retain the complete font metric height.");

        if (ascentPx - pixelHeight < 1)
        {
            Assert.Inconclusive("The resolved font does not expose the tall-ascent regression case.");
            return;
        }

        int height = Math.Max(1, (int)Math.Ceiling((font.Ascent + font.Descent) * dpiScale));
        var bitmap = FreeTypeText.Rasterize(
            "H", font, widthPx: 32, height, Color.Black,
            TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap);

        int firstInkRow = FindFirstInkRow(bitmap);
        Assert.IsGreaterThanOrEqualTo(0, firstInkRow, "The capital produced no pixels.");

        // LineBoxTrim moves the untrimmed raster up by ascent - cap height. A capital must then
        // begin at the trimmed box's top (within one hinted pixel), not above it.
        double topTrimPx = (font.Ascent - font.CapHeight) * dpiScale;
        Assert.AreEqual(topTrimPx, firstInkRow, 1.1,
            "The raster baseline diverges from the layout baseline and clips cap-height text.");
    }

    private static int FindFirstInkRow(TextBitmap bitmap)
    {
        for (int y = 0; y < bitmap.HeightPx; y++)
        {
            int row = y * bitmap.WidthPx * 4;
            for (int x = 0; x < bitmap.WidthPx; x++)
            {
                if (bitmap.Data[row + (x * 4) + 3] != 0)
                {
                    return y;
                }
            }
        }

        return -1;
    }
}
