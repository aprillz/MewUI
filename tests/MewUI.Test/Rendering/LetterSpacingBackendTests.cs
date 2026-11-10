extern alias gdibackend;
extern alias MewVGWin32;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

using GdiDirectWriteFonts = gdibackend::Aprillz.MewUI.Rendering.DirectWrite.DirectWriteFontFactory;
using MewVGDirectWriteFonts = MewVGWin32::Aprillz.MewUI.Rendering.DirectWrite.DirectWriteFontFactory;
using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

/// <summary>
/// A run drawn with letter spacing puts every glyph where the backend's own advances for that spacing
/// say it is, on every Windows backend, text face and scale: the layout reads those advances, so a run
/// drawn at its natural advances would leave the carets and the selection off the glyphs.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LetterSpacingBackendTests
{
    private const string FAMILY = "Segoe UI";
    private const string TEXT = "HHHH";
    private const double FONT_SIZE = 20;
    private const double ORIGIN = 12;
    private const double NATURAL_ROW = 8;
    private const double SPACED_ROW = 68;
    private const double ROW_HEIGHT = 48;
    private const int WIDTH = 260;
    private const int HEIGHT = 128;

    // Whole device pixels at every scale under test, so a backend that spaces by whole pixels has nothing to
    // round; the negative one tightens without closing the gap between two capitals.
    private static readonly double[] _spacings = [8, -2];

    private static IEnumerable<object[]> Faces()
    {
        yield return ["Direct2D", false];
        yield return ["Gdi", false];
        yield return ["Gdi", true];
        yield return ["MewVGWin32", false];
        yield return ["MewVGWin32", true];
    }

    private static IEnumerable<object[]> Backends()
    {
        yield return ["Direct2D"];
        yield return ["Gdi"];
        yield return ["MewVGWin32"];
    }

    [TestMethod]
    [DynamicData(nameof(Faces))]
    public void ARunDrawnWithLetterSpacing_PutsItsGlyphsAtTheAdvancesMeasuredForIt(string backend, bool directWriteFace)
    {
        RunOnBackend(backend, factory =>
        {
            foreach (double scale in new[] { 1.0, 1.5, 2.0 })
            {
                uint dpi = (uint)Math.Round(96 * scale);
                using var fonts = CreateFace(backend, directWriteFace, factory, dpi, out IFont font);
                foreach (double spacing in _spacings)
                {
                    string label = $"{backend}{(directWriteFace ? " DirectWrite face" : string.Empty)} x{scale} spacing {spacing}";
                    double[] natural;
                    double[] spaced;
                    using (var measure = ((ITextBackendFactory)factory).CreateTextMeasurementContext(dpi))
                    {
                        natural = measure.GetUtf16PrefixAdvances(TEXT, font)!;
                        spaced = measure.GetUtf16PrefixAdvances(TEXT, font, spacing)!;
                    }

                    for (int index = 0; index < TEXT.Length; index++)
                    {
                        Assert.AreEqual((index + 1) * spacing, spaced[index] - natural[index], 0.51 / scale,
                            $"{label}: the advance after glyph {index} does not carry the spacing of the glyphs before it.");
                    }

                    var (naturalBands, spacedBands) = DrawRows(factory, scale, context =>
                    {
                        var runs = (ITextBackendRenderContext)context;
                        using var naturalRun = runs.CreateRun(TEXT, font, natural[^1], ROW_HEIGHT)!;
                        using var spacedRun = runs.CreateRun(TEXT, font, spaced[^1], ROW_HEIGHT, spacing)!;
                        runs.DrawRun(naturalRun, new Point(ORIGIN, NATURAL_ROW), Color.Black, null);
                        runs.DrawRun(spacedRun, new Point(ORIGIN, SPACED_ROW), Color.Black, null);
                    });

                    AssertGlyphsMoved(label, scale, naturalBands, spacedBands,
                        index => index == 0 ? 0 : spaced[index - 1] - natural[index - 1]);
                }
            }
        });
    }

    [TestMethod]
    [DynamicData(nameof(Backends))]
    public void ALayoutWithLetterSpacing_DrawsItsGlyphsAtItsCarets(string backend)
    {
        RunOnBackend(backend, factory =>
        {
            foreach (double scale in new[] { 1.0, 1.5, 2.0 })
            {
                foreach (double spacing in _spacings)
                {
                    string label = $"{backend} x{scale} spacing {spacing}";
                    var natural = Layout(factory, scale, 0);
                    var spaced = Layout(factory, scale, spacing);

                    Assert.AreEqual(TEXT.Length * spacing, spaced.MeasuredSize.Width - natural.MeasuredSize.Width, 0.51 / scale,
                        $"{label}: the layout is not wider by the spacing after every glyph.");

                    var (naturalBands, spacedBands) = DrawRows(factory, scale, context =>
                    {
                        context.Text.Draw(natural, new Point(ORIGIN, NATURAL_ROW), new TextDrawOptions(Color.Black));
                        context.Text.Draw(spaced, new Point(ORIGIN, SPACED_ROW), new TextDrawOptions(Color.Black));
                    });

                    AssertGlyphsMoved(label, scale, naturalBands, spacedBands,
                        index => spaced.GetCaretBounds(new CharacterHit(index, 0)).X
                            - natural.GetCaretBounds(new CharacterHit(index, 0)).X);
                }
            }
        });
    }

    private static ITextLayout Layout(IGraphicsFactory factory, double scale, double letterSpacing)
        => factory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = TEXT.AsMemory(),
            Dpi = (uint)Math.Round(96 * scale),
            DefaultStyle = new TextRunStyle(FAMILY, FONT_SIZE),
            Paragraph = new TextParagraphStyle { MaxWidth = 1000, LetterSpacing = letterSpacing },
        });

    private static void AssertGlyphsMoved(
        string label, double scale, List<(int Left, int Right)> natural, List<(int Left, int Right)> spaced, Func<int, double> expectedShift)
    {
        Assert.HasCount(TEXT.Length, natural, $"{label}: the natural run did not draw one stem group per glyph.");
        Assert.HasCount(TEXT.Length, spaced, $"{label}: the spaced run did not draw one stem group per glyph.");
        for (int index = 0; index < TEXT.Length; index++)
        {
            Assert.AreEqual(expectedShift(index) * scale, spaced[index].Left - natural[index].Left, 1.0,
                $"{label}: glyph {index} was not drawn where its advance puts it.");
            Assert.AreEqual(natural[index].Right - natural[index].Left, spaced[index].Right - spaced[index].Left, 1.0,
                $"{label}: glyph {index} lost ink to its run box.");
        }
    }

    /// <summary>Draws into the two rows and returns the columns each glyph inked in either, in device pixels.</summary>
    private static (List<(int Left, int Right)> Natural, List<(int Left, int Right)> Spaced) DrawRows(
        IGraphicsFactory factory, double scale, Action<IGraphicsContext> draw)
    {
        int widthPx = (int)Math.Ceiling(WIDTH * scale);
        int heightPx = (int)Math.Ceiling(HEIGHT * scale);
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(widthPx, heightPx, scale, hasAlpha: false));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.White);
            draw(context);
            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        return (
            InkBands(pixels, cpu.StrideBytes, widthPx, (int)(NATURAL_ROW * scale), (int)((NATURAL_ROW + ROW_HEIGHT) * scale)),
            InkBands(pixels, cpu.StrideBytes, widthPx, (int)(SPACED_ROW * scale), (int)((SPACED_ROW + ROW_HEIGHT) * scale)));
    }

    private static List<(int Left, int Right)> InkBands(ReadOnlySpan<byte> pixels, int stride, int widthPx, int top, int bottom)
    {
        var bands = new List<(int Left, int Right)>();
        int start = -1;
        for (int x = 0; x <= widthPx; x++)
        {
            bool inked = false;
            for (int y = top; x < widthPx && y < bottom && !inked; y++)
            {
                inked = pixels[(y * stride) + (x * 4) + 1] < 128;
            }

            if (inked && start < 0)
            {
                start = x;
            }
            else if (!inked && start >= 0)
            {
                bands.Add((start, x));
                start = -1;
            }
        }

        return bands;
    }

    private static IDisposable? CreateFace(string backend, bool directWriteFace, IGraphicsFactory factory, uint dpi, out IFont font)
    {
        if (!directWriteFace)
        {
            font = factory.CreateFont(FAMILY, FONT_SIZE, dpi);
            return font as IDisposable;
        }

        // Each backend compiles its own copy of the DirectWrite sources, so the face has to come from that copy.
        if (backend == "Gdi")
        {
            var fonts = new GdiDirectWriteFonts();
            font = fonts.CreateFont(FAMILY, FONT_SIZE, FontWeight.Normal, false, false, false, dpi);
            return fonts;
        }
        else
        {
            var fonts = new MewVGDirectWriteFonts();
            font = fonts.CreateFont(FAMILY, FONT_SIZE, FontWeight.Normal, false, false, false, dpi);
            return fonts;
        }
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
}
