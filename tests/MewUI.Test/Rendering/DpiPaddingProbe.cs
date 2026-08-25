using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Temporary probe: at non-96 DPI, does the unpadded advance width clip the drawn ink?
/// Distinguishes a device-rounding cause from a glyph-overhang cause for the measurement padding.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class DpiPaddingProbe
{
    private const int SURFACE_WIDTH = 420;
    private const int SURFACE_HEIGHT = 72;

    private static readonly (string Family, double Size, string Text)[] _cases =
    [
        ("Segoe UI", 16, "Hello"),
        ("Segoe UI", 16, "AV"),
        ("Consolas", 14, "int x;"),
        ("Segoe UI", 13, "Wgy"),
        ("Times New Roman", 20, "f"),
    ];

    [TestMethod]
    public void CompareAdvanceMeasureAndInkAcrossDpi()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var report = new System.Text.StringBuilder();
        using var factory = new GdiGraphicsFactory();
        foreach (uint dpi in new uint[] { 96, 120, 144, 168 })
        {
            double scale = dpi / 96.0;
            report.AppendLine($"=== dpi={dpi} scale={scale:F2} ===");
            foreach (var (family, size, text) in _cases)
            {
                using var measureContext = ((ITextBackendFactory)factory).CreateTextMeasurementContext(dpi);
                using var font = factory.CreateFont(family, size, dpi);

                double advanceDip = measureContext.GetUtf16PrefixAdvances(text, font)![^1];
                double measureCtxDip = measureContext.Measure(text, font).Width;
                var (drawCtxDip, inkRightPx) = DrawAndMeasureInk(factory, family, size, dpi, scale, text);

                double advancePx = advanceDip * scale;
                double drawCtxPx = drawCtxDip * scale;
                report.AppendLine(
                    $"  {family,-16}{size,3} \"{text}\" | " +
                    $"adv={advanceDip,7:F3}dip={advancePx,7:F2}px  " +
                    $"mCtx={measureCtxDip,7:F3}dip  " +
                    $"dCtx={drawCtxDip,7:F3}dip={drawCtxPx,7:F2}px  " +
                    $"ink={inkRightPx,4}px  ||  " +
                    $"ink-adv={inkRightPx - advancePx,6:F2}  " +
                    $"ink-dCtx={inkRightPx - drawCtxPx,6:F2}");
            }
        }

        File.WriteAllText(
            @"C:\Users\al6uiz\AppData\Local\Temp\claude\e--Personal-Mew\994a7e7c-855d-43b4-bfce-aed40318e04f\scratchpad\dpi-probe.txt",
            report.ToString());
    }

    /// <summary>Draws on a DPI-scaled surface, returning the drawing context width and the rightmost ink column.</summary>
    private static (double WidthDip, int InkRightPx) DrawAndMeasureInk(
        GdiGraphicsFactory factory, string family, double size, uint dpi, double scale, string text)
    {
        var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.CachedImage(SURFACE_WIDTH, SURFACE_HEIGHT, scale));
        try
        {
            double widthDip;
            var area = new Rect(0, 0, SURFACE_WIDTH / scale, SURFACE_HEIGHT / scale);
            using (var context = factory.CreateContext(surface))
            {
                context.BeginFrame(surface);
                context.FillRectangle(area, Color.FromRgb(255, 255, 255));
                using var font = factory.CreateFont(family, size, dpi);
                widthDip = TextTestHarness.Measure(factory, text.AsMemory(), font).Width;
                TextTestHarness.Draw(context, factory, text.AsMemory(), area, font, Color.FromRgb(0, 0, 0));
                context.EndFrame();
            }

            var cpu = (ICpuPixelSurface)surface;
            var pixels = cpu.GetReadOnlyPixelSpan();
            int stride = cpu.StrideBytes;
            int rightmost = -1;
            for (int y = 0; y < SURFACE_HEIGHT; y++)
            {
                for (int x = 0; x < SURFACE_WIDTH; x++)
                {
                    int offset = y * stride + x * 4;
                    // Any darkening counts as ink, including anti-aliasing fringes.
                    if (pixels[offset] < 250 || pixels[offset + 1] < 250 || pixels[offset + 2] < 250)
                    {
                        if (x > rightmost) rightmost = x;
                    }
                }
            }
            return (widthDip, rightmost + 1);
        }
        finally
        {
            surface.Dispose();
        }
    }
}
