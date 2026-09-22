extern alias gdibackend;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

using GdiDirectWriteFonts = gdibackend::Aprillz.MewUI.Rendering.DirectWrite.DirectWriteFontFactory;
using GdiMeasurement = gdibackend::Aprillz.MewUI.Rendering.Gdi.GdiMeasurementContext;
using RasterizeRequest = gdibackend::Aprillz.MewUI.Rendering.Win32.Win32TextRasterizeRequest;
using TextFace = gdibackend::Aprillz.MewUI.Rendering.Win32.IWin32TextFace;

namespace MewUI.Test.Rendering;

/// <summary>
/// The Win32 text engine's DirectWrite fonts are drawn by the GDI-classic rasterizer, so the baseline
/// and cap line a line box is trimmed to have to be the ones that rasterizer lands on, not the design
/// metrics scaled to the size.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class DirectWriteWin32BaselineTests
{
    [TestMethod]
    public void TrimmedCapAndBaseline_LandOnTheInkOfACapital()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Win32 text engines are Windows-only.");
            return;
        }

        using var fonts = new GdiDirectWriteFonts();
        foreach (uint dpi in new uint[] { 96, 120, 144 })
        {
            foreach (double size in new double[] { 12, 14, 18, 20 })
            {
                using var font = fonts.CreateFont("Segoe UI", size, FontWeight.Normal, false, false, false, dpi, gridFitMetrics: true);
                double scale = dpi / 96.0;
                double baseline = new GdiMeasurement(0, dpi).GetRasterBaseline(font);
                (int top, int bottom) = CapitalInk(font, scale);

                Assert.AreEqual(baseline * scale, bottom, 0.5,
                    $"{size} at {dpi} DPI: the baseline trim does not end where the capital does.");
                Assert.AreEqual((baseline - font.CapHeight) * scale, top, 0.5,
                    $"{size} at {dpi} DPI: the cap trim does not start where the capital does.");
            }
        }
    }

    private static (int Top, int Bottom) CapitalInk(IFont font, double scale)
    {
        int height = (int)Math.Ceiling((font.Ascent + font.Descent) * scale) + 4;
        Assert.IsTrue(((TextFace)font).TryRasterize("H", new RasterizeRequest(64, height, Color.Black,
            TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap, TextTrimming.None, scale), out var bitmap));

        int top = -1;
        int bottom = -1;
        for (int y = 0; y < bitmap.HeightPx; y++)
        {
            for (int x = 0; x < bitmap.WidthPx; x++)
            {
                if (bitmap.Data[((y * bitmap.WidthPx) + x) * 4 + 3] > 128)
                {
                    if (top < 0)
                    {
                        top = y;
                    }

                    bottom = y + 1;
                    break;
                }
            }
        }

        Assert.IsGreaterThanOrEqualTo(0, top, "The capital produced no pixels.");
        return (top, bottom);
    }
}
