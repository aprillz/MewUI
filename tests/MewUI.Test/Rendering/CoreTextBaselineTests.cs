extern alias MewVGMacOS;

using Aprillz.MewUI;

using CoreTextFont = MewVGMacOS::Aprillz.MewUI.Rendering.CoreText.CoreTextFont;
using CoreTextText = MewVGMacOS::Aprillz.MewUI.Rendering.CoreText.CoreTextText;
using MewVGMetalMeasurementContext = MewVGMacOS::Aprillz.MewUI.Rendering.MewVG.MewVGMetalMeasurementContext;
using TextBitmap = MewVGMacOS::Aprillz.MewUI.Rendering.TextBitmap;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class CoreTextBaselineTests
{
    [TestMethod]
    public void MeasurementBaselineMatchesRasterFontAtEveryScale()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("CoreText is macOS-only.");
            return;
        }

        foreach (uint dpi in new uint[] { 96, 144, 192 })
        {
            using var font = CoreTextFont.Create(
                ".AppleSystemUIFont",
                12,
                dpi,
                FontWeight.Normal,
                italic: false,
                underline: false,
                strikethrough: false);
            using var measurement = new MewVGMetalMeasurementContext(dpi);

            double rasterBaseline = CoreTextText.GetRasterBaseline(font, dpi);
            double layoutBaseline = measurement.GetRasterBaseline(font);
            Console.Error.WriteLine(
                $"CoreText {dpi} DPI: font ascent={font.Ascent:F3}, layout baseline={layoutBaseline:F3}, " +
                $"raster baseline={rasterBaseline:F3}");
            Assert.AreEqual(rasterBaseline, layoutBaseline, 0.001,
                $"CoreText {dpi} DPI uses different layout and raster baselines.");

            double dpiScale = dpi / 96.0;
            int heightPx = Math.Max(1, (int)Math.Ceiling((font.Ascent + font.Descent) * dpiScale));
            var bitmap = CoreTextText.Rasterize(
                font, "H", widthPx: 32 * (int)Math.Ceiling(dpiScale), heightPx, dpi, Color.Black,
                TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap);
            int firstInkRow = FindFirstInkRow(bitmap);
            Assert.IsGreaterThanOrEqualTo(0, firstInkRow, $"The capital produced no pixels at {dpi} DPI.");

            double topTrimPx = (rasterBaseline - font.CapHeight) * dpiScale;
            Assert.AreEqual(topTrimPx, firstInkRow, 1.1,
                $"The CoreText raster diverges from the layout baseline at {dpi} DPI.");
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
