extern alias MewVGX11;

using Aprillz.MewUI;

using FreeTypeFont = MewVGX11::Aprillz.MewUI.Rendering.FreeType.FreeTypeFont;
using FreeTypeText = MewVGX11::Aprillz.MewUI.Rendering.FreeType.FreeTypeText;
using LinuxFontResolver = MewVGX11::Aprillz.MewUI.Rendering.FreeType.LinuxFontResolver;
using OpenGLMeasurementContext = MewVGX11::Aprillz.MewUI.Rendering.OpenGL.OpenGLMeasurementContext;
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

        string? path = LinuxFontResolver.ResolveFontPath("Noto Sans", FontWeight.Normal, italic: false);
        Assert.IsNotNull(path, "No usable Linux font was found.");

        foreach (uint dpi in new uint[] { 96, 144, 192 })
        {
            const double size = 16;
            int pixelHeight = (int)Math.Round(size * dpi / 96.0, MidpointRounding.AwayFromZero);
            using var font = new FreeTypeFont(
                "Noto Sans", size, FontWeight.Normal,
                italic: false, underline: false, strikethrough: false,
                path, pixelHeight);

            double dpiScale = pixelHeight / size;
            double ascentPx = font.Ascent * dpiScale;
            using var measurement = new OpenGLMeasurementContext(dpi);
            double rasterBaseline = FreeTypeText.GetRasterBaseline(font);
            Console.Error.WriteLine(
                $"FreeType {dpi} DPI: font ascent={font.Ascent:F3}, layout baseline=" +
                $"{measurement.GetRasterBaseline(font):F3}, raster baseline={rasterBaseline:F3}, " +
                $"device baseline={rasterBaseline * dpiScale:F3}px");
            Assert.AreEqual(rasterBaseline, measurement.GetRasterBaseline(font), 0.001,
                $"The text engine and FreeType rasterizer use different baseline rounding at {dpi} DPI.");
            Assert.AreEqual(Math.Round(ascentPx) / dpiScale, rasterBaseline, 0.001,
                $"The reported baseline does not land on FreeType's device-pixel baseline at {dpi} DPI.");
            var measured = FreeTypeText.Measure("Hg", font);
            Assert.IsGreaterThanOrEqualTo(
                (font.Ascent + font.Descent + font.InternalLeading) * dpiScale,
                measured.Height,
                $"The raster box must retain the complete font metric height at {dpi} DPI.");

            if (ascentPx - pixelHeight < 1)
            {
                Assert.Inconclusive($"The resolved font does not expose the tall-ascent regression case at {dpi} DPI.");
                return;
            }

            int height = Math.Max(1, (int)Math.Ceiling((font.Ascent + font.Descent) * dpiScale));
            var bitmap = FreeTypeText.Rasterize(
                "H", font, widthPx: 32 * (int)Math.Ceiling(dpiScale), height, Color.Black,
                TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap);

            int firstInkRow = FindFirstInkRow(bitmap);
            Assert.IsGreaterThanOrEqualTo(0, firstInkRow, $"The capital produced no pixels at {dpi} DPI.");

            // LineBoxTrim moves the untrimmed raster up by ascent - cap height. A capital must then
            // begin at the trimmed box's top (within one hinted pixel), not above it.
            double topTrimPx = (font.Ascent - font.CapHeight) * dpiScale;
            Assert.AreEqual(topTrimPx, firstInkRow, 1.1,
                $"The raster baseline diverges from the layout baseline at {dpi} DPI.");
        }
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
