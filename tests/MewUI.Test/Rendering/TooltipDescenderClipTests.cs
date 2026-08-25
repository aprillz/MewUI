using System;
using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;
using Aprillz.MewUI.Rendering.Direct2D;

namespace MewUI.Test.Rendering;

/// <summary>
/// Reproduces the tooltip symptom: the same text, drawn into a content-tight line box at different
/// sub-pixel Y positions, loses its descender tails at some positions and not others. A reference
/// draw into a box with room to spare gives the true ink bottom; any position whose ink bottom falls
/// short of it is clipped.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TooltipDescenderClipTests
{
    private const string TEXT = "Data Binding";
    private const double DPI_SCALE = 1.25;
    private const double BOX_HEIGHT_DIP = 16;   // what the tooltip's TextBlock gets arranged to

    [TestMethod]
    public void TightLineBox_KeepsDescender_AtEverySubPixelPosition()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();

        var clipped = new System.Collections.Generic.List<string>();
        for (int step = 0; step < 10; step++)
        {
            double yOffset = step * 0.1;
            // Tight box, centred - exactly how the tooltip's TextBlock is arranged.
            int bottom = InkBottomRow(factory, yOffset, BOX_HEIGHT_DIP, TextAlignment.Center);
            // Same origin, but with room below and top-aligned, so nothing can clip: the true ink bottom.
            int reference = InkBottomRow(factory, yOffset, BOX_HEIGHT_DIP + 8, TextAlignment.Top);
            Console.WriteLine($"[descender] yOffset={yOffset:0.0} tight={bottom} reference={reference}");
            if (bottom < reference)
            {
                clipped.Add($"y={yOffset:0.0} (tight={bottom}, reference={reference})");
            }
        }

        Assert.IsEmpty(clipped,
            $"descender clipped at {clipped.Count} of 10 sub-pixel positions: {string.Join(", ", clipped)}");
    }

    private static int InkBottomRow(IGraphicsFactory factory, double yOffsetDip, double boxHeightDip, TextAlignment verticalAlignment)
    {
        const int widthPx = 240;
        const int heightPx = 80;

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(widthPx, heightPx, DPI_SCALE));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.FromArgb(255, 255, 255, 255));

            using var font = factory.CreateFont("Segoe UI", 12, (uint)Math.Round(DPI_SCALE * 96));
            var box = new Rect(2, 4 + yOffsetDip, 200, boxHeightDip);
            var layout = TextTestHarness.CreateLayout(factory, TEXT.AsMemory(), font, box);
            TextTestHarness.Draw(context, layout, box, Color.FromArgb(255, 0, 0, 0), verticalAlignment);

            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        int stride = cpu.StrideBytes;

        int bottom = -1;
        for (int y = 0; y < heightPx; y++)
        {
            for (int x = 0; x < widthPx; x++)
            {
                if (pixels[y * stride + x * 4 + 1] < 200)
                {
                    bottom = y;
                    break;
                }
            }
        }

        return bottom;
    }
}
