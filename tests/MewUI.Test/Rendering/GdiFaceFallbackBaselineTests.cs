extern alias gdibackend;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;

using GdiDirectWriteFonts = gdibackend::Aprillz.MewUI.Rendering.DirectWrite.DirectWriteFontFactory;
using TextFace = gdibackend::Aprillz.MewUI.Rendering.Win32.IWin32TextFace;

namespace MewUI.Test.Rendering;

/// <summary>
/// A glyph the font lacks is drawn from a fallback font, but the line keeps the requested font's
/// height and baseline, as the FreeType and CoreText engines and WPF do. The DirectWrite path pins
/// its line spacing to the font for that, and the GDI backend draws the run where the layout put it.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class GdiFaceFallbackBaselineTests
{
    // Consolas has no Hangul, so the second string takes a fallback font with a larger ascent.
    private const string LATIN = "ab";
    private const string MIXED = "a가";

    [TestMethod]
    public void FallbackGlyph_KeepsTheFontLineHeightAndBaseline()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Win32 text engines are Windows-only.");
            return;
        }

        using var fonts = new GdiDirectWriteFonts();
        using var font = fonts.CreateFont("Consolas", 14, FontWeight.Normal, false, false, false, 96, gridFitMetrics: true);
        var face = (TextFace)font;

        var latin = face.Measure(LATIN, double.PositiveInfinity, TextWrapping.NoWrap, 1);
        var mixed = face.Measure(MIXED, double.PositiveInfinity, TextWrapping.NoWrap, 1);
        Assert.AreEqual(latin.Height, mixed.Height, 0.01, "a fallback glyph grew the measured line");

        Assert.IsTrue(face.TryGetRunBaseline(LATIN, 1, out double latinBaseline));
        Assert.IsTrue(face.TryGetRunBaseline(MIXED, 1, out double mixedBaseline));
        Assert.AreEqual(font.Ascent, latinBaseline, 0.01, "text in the font's own glyphs sits on the font baseline");
        Assert.AreEqual(font.Ascent, mixedBaseline, 0.01, "a fallback glyph moved the line's baseline");
    }

    [TestMethod]
    public void Direct2DEngine_FallbackGlyphKeepsTheManagedLineHeight()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("DirectWrite is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        var latin = factory.TextEngine.CreateLayout(CreateRequest(LATIN));
        var mixed = factory.TextEngine.CreateLayout(CreateRequest(MIXED));

        Assert.AreEqual(latin.Lines[0].Bounds.Height, mixed.Lines[0].Bounds.Height, 0.01, "a fallback glyph grew the managed line");
        Assert.AreEqual(latin.Lines[0].Baseline, mixed.Lines[0].Baseline, 0.01, "a fallback glyph moved the managed baseline");
    }

    [TestMethod]
    public void GdiFaceRun_DrawsMixedTextOnTheSameBaselineAsLatinText()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Win32 text engines are Windows-only.");
            return;
        }

        const int WIDTH = 160;
        const int HEIGHT = 60;
        using var factory = new GdiGraphicsFactory();
        using var fonts = new GdiDirectWriteFonts();
        using var font = fonts.CreateFont("Consolas", 14, FontWeight.Normal, false, false, false, 96, gridFitMetrics: true);
        var face = (TextFace)font;
        int latinColumns = (int)Math.Floor(face.Measure("a", double.PositiveInfinity, TextWrapping.NoWrap, 1).Width);

        int latinBottom = BottomInkRow(factory, font, LATIN, WIDTH, HEIGHT, latinColumns, out double latinOffset);
        int mixedBottom = BottomInkRow(factory, font, MIXED, WIDTH, HEIGHT, latinColumns, out double mixedOffset);

        Assert.AreEqual(latinBottom, mixedBottom, 1, "the Latin glyph moved when a fallback glyph joined the run");
        Assert.AreEqual(0, latinOffset, 0.01, "text in the font's own glyphs needs no origin offset");
        Assert.AreEqual(0, mixedOffset, 0.5, "with the line pinned to the font, the mixed run needs no more than pixel rounding");
    }

    private static TextLayoutRequest CreateRequest(string text)
        => new()
        {
            Text = text.AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle("Consolas", 14),
        };

    private static int BottomInkRow(GdiGraphicsFactory factory, IFont font, string text, int width, int height, int columns, out double originOffset)
    {
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(width, height, 1.0, hasAlpha: false));
        using var context = factory.CreateContext(surface);
        var backend = (ITextBackendRenderContext)context;
        context.BeginFrame(surface);
        try
        {
            context.Clear(Color.White);
            using var run = backend.CreateRun(text, font, 120, 40);
            Assert.IsNotNull(run);
            originOffset = ((GraphicsContextBase)context).CreateBackendTextLayout(text, new BackendTextFormat
            {
                Font = font,
                HorizontalAlignment = TextAlignment.Left,
                VerticalAlignment = TextAlignment.Top,
                Wrapping = TextWrapping.NoWrap,
                Trimming = TextTrimming.None,
            }, new BackendTextLayoutConstraints(new Rect(0, 0, 120, 40)))!.RasterOriginOffsetY;
            backend.DrawRun(run, new Point(4, 8), Color.Black, null);
        }
        finally
        {
            context.EndFrame();
        }

        var pixels = new byte[width * height * 4];
        Assert.IsTrue(factory.TryReadPixels(surface, pixels, width * 4), "the surface could not be read back");

        int bottom = -1;
        for (int y = 0; y < height; y++)
        {
            for (int x = 4; x < 4 + columns && x < width; x++)
            {
                int offset = (y * width + x) * 4;
                if (pixels[offset] < 128)
                {
                    bottom = y;
                    break;
                }
            }
        }

        Assert.IsGreaterThanOrEqualTo(0, bottom, $"'{text}' produced no pixels in the Latin columns");
        return bottom;
    }
}
