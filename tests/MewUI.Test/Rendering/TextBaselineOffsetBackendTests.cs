extern alias MewVGWin32;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

/// <summary>
/// A shifted run has to land its ink at the shifted position on every Windows backend and scale, without
/// its realization cutting off the part that moved. Two identical glyphs are drawn side by side with the
/// second one shifted: their ink tops must differ by the shift in device pixels, and the shifted glyph
/// must keep the ink height of the unshifted one.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TextBaselineOffsetBackendTests
{
    private const string FAMILY = "Segoe UI";
    private const double FONT_SIZE = 20;
    private const double ORIGIN = 20;

    private static IEnumerable<object[]> Backends()
    {
        yield return ["Direct2D"];
        yield return ["Gdi"];
        yield return ["MewVGWin32"];
    }

    [TestMethod]
    [DynamicData(nameof(Backends))]
    public void ShiftedGlyphInk_MovesByTheOffsetAndKeepsItsHeight(string backend)
    {
        RunOnBackend(backend, factory =>
        {
            foreach (double scale in new[] { 1.0, 1.25, 1.5, 2.0 })
            {
                foreach (double offset in new[] { 0.0, 6.0, -4.0, 2.5 })
                {
                    var (first, second) = MeasureInk(factory, scale, offset);
                    double expectedShiftPx = offset * scale;
                    Console.WriteLine(
                        $"[{backend} x{scale} S={offset}] first top={first.Top} bottom={first.Bottom}; " +
                        $"second top={second.Top} bottom={second.Bottom}; expected shift={expectedShiftPx:F2}px");
                    Assert.IsGreaterThanOrEqualTo(0, first.Top, $"{backend} x{scale} S={offset}: no ink for the first glyph.");
                    Assert.IsGreaterThanOrEqualTo(0, second.Top, $"{backend} x{scale} S={offset}: no ink for the shifted glyph.");
                    Assert.AreEqual(-expectedShiftPx, second.Top - first.Top, 1.0,
                        $"{backend} x{scale} S={offset}: the shifted glyph's ink did not move by the offset.");
                    Assert.AreEqual(first.Bottom - first.Top, second.Bottom - second.Top, 1.0,
                        $"{backend} x{scale} S={offset}: the shifted glyph lost ink to its realization box.");
                }
            }
        });
    }

    private static void RunOnBackend(string backend, Action<IGraphicsFactory> body)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows backends only.");
            return;
        }

        switch (backend)
        {
            case "Direct2D":
            {
                using var factory = new Aprillz.MewUI.Rendering.Direct2D.Direct2DGraphicsFactory();
                body(factory);
                break;
            }
            case "Gdi":
            {
                using var factory = new Aprillz.MewUI.Rendering.Gdi.GdiGraphicsFactory();
                body(factory);
                break;
            }
            default:
            {
                using var factory = new MewVGWin32GraphicsFactory();
                using var backgroundScope = factory.AcquireBackgroundRenderScope();
                body(factory);
                break;
            }
        }
    }

    private readonly record struct InkBox(int Top, int Bottom);

    /// <summary>Draws "HH" with the second glyph shifted and returns each glyph's ink rows in device pixels.</summary>
    private static (InkBox First, InkBox Second) MeasureInk(IGraphicsFactory factory, double scale, double offset)
    {
        const int WIDTH_PX = 240;
        const int HEIGHT_PX = 160;
        var layout = factory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = "HH".AsMemory(),
            Dpi = (uint)Math.Round(96 * scale),
            DefaultStyle = new TextRunStyle(FAMILY, FONT_SIZE),
            Runs = [new GeometryStyleRun(1, 1, new TextRunStyle(FAMILY, FONT_SIZE) { BaselineOffset = offset })],
            Paragraph = new TextParagraphStyle { MaxWidth = 1000 },
            Transient = true
        });
        double splitX = layout.GetCaretBounds(new CharacterHit(1, 0)).X;

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH_PX, HEIGHT_PX, scale));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.White);
            var options = new TextDrawOptions(Color.Black, Transient: true);
            context.Text.Draw(layout, new Point(ORIGIN, ORIGIN), in options);
            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        int stride = cpu.StrideBytes;
        int columns = stride / 4;
        int splitPx = (int)Math.Round((ORIGIN + splitX) * scale);
        return (Scan(pixels, stride, 0, splitPx), Scan(pixels, stride, splitPx, columns));
    }

    private static InkBox Scan(ReadOnlySpan<byte> pixels, int stride, int left, int right)
    {
        int rows = pixels.Length / stride;
        int top = -1;
        int bottom = -1;
        for (int row = 0; row < rows; row++)
        {
            for (int column = left; column < right; column++)
            {
                int index = row * stride + column * 4;
                if (pixels[index] < 250 || pixels[index + 1] < 250 || pixels[index + 2] < 250)
                {
                    if (top < 0)
                    {
                        top = row;
                    }
                    bottom = row;
                    break;
                }
            }
        }

        return new InkBox(top, bottom);
    }
}
