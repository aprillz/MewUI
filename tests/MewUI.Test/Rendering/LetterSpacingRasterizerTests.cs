extern alias MewVGMacOS;
extern alias MewVGX11;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

using CoreTextFont = MewVGMacOS::Aprillz.MewUI.Rendering.CoreText.CoreTextFont;
using CoreTextText = MewVGMacOS::Aprillz.MewUI.Rendering.CoreText.CoreTextText;
using FreeTypeFont = MewVGX11::Aprillz.MewUI.Rendering.FreeType.FreeTypeFont;
using FreeTypeText = MewVGX11::Aprillz.MewUI.Rendering.FreeType.FreeTypeText;
using LinuxFontResolver = MewVGX11::Aprillz.MewUI.Rendering.FreeType.LinuxFontResolver;
using MewVGMetalMeasurementContext = MewVGMacOS::Aprillz.MewUI.Rendering.MewVG.MewVGMetalMeasurementContext;
using OpenGLMeasurementContext = MewVGX11::Aprillz.MewUI.Rendering.OpenGL.OpenGLMeasurementContext;

namespace MewUI.Test.Rendering;

/// <summary>
/// The FreeType and CoreText rasterizers draw a run with letter spacing where their own advances for
/// that spacing say its glyphs are, the same agreement <see cref="LetterSpacingBackendTests"/> holds
/// the Windows backends to.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class LetterSpacingRasterizerTests
{
    private const string TEXT = "HHHH";
    private const double FONT_SIZE = 20;
    private const double SPACING = 8;

    [TestMethod]
    public void FreeType_DrawsASpacedRunAtTheAdvancesMeasuredForIt()
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
            int pixelHeight = (int)Math.Round(FONT_SIZE * dpi / 96.0, MidpointRounding.AwayFromZero);
            double scale = pixelHeight / FONT_SIZE;
            using var font = new FreeTypeFont(
                "Noto Sans", FONT_SIZE, FontWeight.Normal,
                italic: false, underline: false, strikethrough: false,
                path, pixelHeight);
            using var measurement = new OpenGLMeasurementContext(dpi);
            ITextAdvanceSource advances = measurement;
            double[] natural = advances.GetUtf16PrefixAdvances(TEXT, font, 0);
            double[] spaced = advances.GetUtf16PrefixAdvances(TEXT, font, SPACING);
            Console.Error.WriteLine(
                $"FreeType {dpi} DPI: natural=[{string.Join(", ", natural)}], spaced=[{string.Join(", ", spaced)}]");
            for (int index = 0; index < TEXT.Length; index++)
            {
                Assert.AreEqual((index + 1) * SPACING, spaced[index] - natural[index], 0.51 / scale,
                    $"FreeType {dpi} DPI: the advance after glyph {index} does not carry the spacing of the glyphs before it.");
            }

            int widthPx = (int)Math.Ceiling(spaced[^1] * scale) + 8;
            int heightPx = (int)Math.Ceiling((font.Ascent + font.Descent) * scale) + 4;
            var naturalBitmap = FreeTypeText.Rasterize(
                TEXT, font, widthPx, heightPx, Color.Black, TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap);
            var spacedBitmap = FreeTypeText.Rasterize(
                TEXT, font, widthPx, heightPx, Color.Black, TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap,
                letterSpacingPx: FreeTypeText.ToLetterSpacingPx(SPACING, scale));

            AssertGlyphsMoved(
                $"FreeType {dpi} DPI", scale,
                InkBands(naturalBitmap.Data, naturalBitmap.WidthPx, naturalBitmap.HeightPx),
                InkBands(spacedBitmap.Data, spacedBitmap.WidthPx, spacedBitmap.HeightPx),
                natural, spaced);
        }
    }

    [TestMethod]
    public void CoreText_DrawsASpacedRunAtTheAdvancesMeasuredForIt()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Assert.Inconclusive("CoreText is macOS-only.");
            return;
        }

        foreach (uint dpi in new uint[] { 96, 144, 192 })
        {
            double scale = dpi / 96.0;
            using var font = CoreTextFont.Create(
                ".AppleSystemUIFont", FONT_SIZE, dpi, FontWeight.Normal,
                italic: false, underline: false, strikethrough: false);
            using var measurement = new MewVGMetalMeasurementContext(dpi);
            ITextAdvanceSource advances = measurement;
            double[] natural = advances.GetUtf16PrefixAdvances(TEXT, font, 0);
            double[] spaced = advances.GetUtf16PrefixAdvances(TEXT, font, SPACING);
            Console.Error.WriteLine(
                $"CoreText {dpi} DPI: natural=[{string.Join(", ", natural)}], spaced=[{string.Join(", ", spaced)}]");

            // CoreText reports the edge between two glyphs in the middle of the space it kerns them apart by,
            // and none after the last glyph, so its advances are checked for what the layout needs of them:
            // the run is wider by the spacing between its glyphs, and every glyph is drawn inside the
            // column its advances give it.
            Assert.IsGreaterThanOrEqualTo((TEXT.Length - 1) * SPACING - 0.51 / scale, spaced[^1] - natural[^1],
                $"CoreText {dpi} DPI: the run is not wider by the spacing between its glyphs.");

            int widthPx = (int)Math.Ceiling((spaced[^1] + SPACING) * scale) + 8;
            int heightPx = (int)Math.Ceiling((font.Ascent + font.Descent) * scale) + 4;
            var naturalBitmap = CoreTextText.Rasterize(
                font, TEXT, widthPx, heightPx, dpi, Color.Black, TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap);
            var spacedBitmap = CoreTextText.Rasterize(
                font, TEXT, widthPx, heightPx, dpi, Color.Black, TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap,
                letterSpacingPx: SPACING * scale);
            var naturalBands = InkBands(naturalBitmap.Data, naturalBitmap.WidthPx, naturalBitmap.HeightPx);
            var spacedBands = InkBands(spacedBitmap.Data, spacedBitmap.WidthPx, spacedBitmap.HeightPx);

            Assert.HasCount(TEXT.Length, naturalBands, $"CoreText {dpi} DPI: the natural run did not draw one stem group per glyph.");
            Assert.HasCount(TEXT.Length, spacedBands, $"CoreText {dpi} DPI: the spaced run did not draw one stem group per glyph.");
            for (int index = 0; index < TEXT.Length; index++)
            {
                Assert.AreEqual(index * SPACING * scale, spacedBands[index].Left - naturalBands[index].Left, 1.0,
                    $"CoreText {dpi} DPI: glyph {index} was not moved by the spacing of the glyphs before it.");
                double columnLeft = (index == 0 ? 0 : spaced[index - 1]) * scale;
                double columnRight = spaced[index] * scale;
                Assert.IsGreaterThanOrEqualTo(columnLeft - 1.0, spacedBands[index].Left,
                    $"CoreText {dpi} DPI: glyph {index} starts before the column its advances give it.");
                Assert.IsLessThanOrEqualTo(columnRight + 1.0, spacedBands[index].Right,
                    $"CoreText {dpi} DPI: glyph {index} ends past the column its advances give it.");
            }
        }
    }

    /// <summary>
    /// The whole path of a spaced layout on the backend that owns the rasterizer: the text engine reads
    /// the advances, the context keys and rasterizes the run, and the glyphs land in the columns the
    /// layout reports, each one a spacing further along than the one before.
    /// </summary>
    [TestMethod]
    [DataRow("X11")]
    [DataRow("MacOS")]
    public void ALayoutWithLetterSpacing_DrawsItsGlyphsInsideItsColumns(string platform)
    {
        bool runsHere = platform == "X11" ? OperatingSystem.IsLinux() : OperatingSystem.IsMacOS();
        if (!runsHere)
        {
            Assert.Inconclusive($"The MewVG {platform} backend does not run on this platform.");
            return;
        }

        const double ORIGIN = 12;
        const int NATURAL_ROW = 8;
        const int SPACED_ROW = 68;
        const int ROW_HEIGHT = 48;
        const int WIDTH = 260;
        const int HEIGHT = 128;

        IGraphicsFactory factory;
        if (platform == "X11")
        {
            // No window precedes the frames, so the backend renders on a surfaceless EGL device.
            Application.RendersWithoutDisplay = true;
            MewVGX11::Aprillz.MewUI.MewVGX11Backend.Register();
            factory = new MewVGX11::Aprillz.MewUI.Rendering.MewVG.MewVGX11GraphicsFactory();
        }
        else
        {
            factory = new MewVGMacOS::Aprillz.MewUI.Rendering.MewVG.MewVGMacOSGraphicsFactory();
        }

        using var disposable = factory as IDisposable;
        using var backgroundScope = factory.AcquireBackgroundRenderScope();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(WIDTH, HEIGHT));
        ITextLayout Layout(double letterSpacing) => factory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = TEXT.AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle("sans-serif", FONT_SIZE),
            Paragraph = new TextParagraphStyle { MaxWidth = 1000, LetterSpacing = letterSpacing },
        });
        var natural = Layout(0);
        var spaced = Layout(SPACING);

        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.White);
            context.Text.Draw(natural, new Point(ORIGIN, NATURAL_ROW), new TextDrawOptions(Color.Black));
            context.Text.Draw(spaced, new Point(ORIGIN, SPACED_ROW), new TextDrawOptions(Color.Black));
            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        var naturalBands = DarkBands(pixels, cpu.StrideBytes, WIDTH, NATURAL_ROW, NATURAL_ROW + ROW_HEIGHT);
        var spacedBands = DarkBands(pixels, cpu.StrideBytes, WIDTH, SPACED_ROW, SPACED_ROW + ROW_HEIGHT);
        Console.Error.WriteLine(
            $"{platform}: natural width={natural.MeasuredSize.Width:F2}, spaced width={spaced.MeasuredSize.Width:F2}, " +
            $"natural bands=[{string.Join(", ", naturalBands)}], spaced bands=[{string.Join(", ", spacedBands)}]");

        Assert.IsGreaterThanOrEqualTo((TEXT.Length - 1) * SPACING - 0.51, spaced.MeasuredSize.Width - natural.MeasuredSize.Width,
            $"{platform}: the layout is not wider by the spacing between its glyphs.");
        Assert.HasCount(TEXT.Length, naturalBands, $"{platform}: the natural layout did not draw one stem group per glyph.");
        Assert.HasCount(TEXT.Length, spacedBands, $"{platform}: the spaced layout did not draw one stem group per glyph.");
        for (int index = 0; index < TEXT.Length; index++)
        {
            Assert.AreEqual(index * SPACING, spacedBands[index].Left - naturalBands[index].Left, 1.5,
                $"{platform}: glyph {index} was not moved by the spacing of the glyphs before it.");
            double columnLeft = ORIGIN + spaced.GetCaretBounds(new CharacterHit(index, 0)).X;
            double columnRight = ORIGIN + spaced.GetCaretBounds(new CharacterHit(index + 1, 0)).X;
            Assert.IsGreaterThanOrEqualTo(columnLeft - 1.5, spacedBands[index].Left,
                $"{platform}: glyph {index} starts before the column the layout gives it.");
            Assert.IsLessThanOrEqualTo(columnRight + 1.5, spacedBands[index].Right,
                $"{platform}: glyph {index} ends past the column the layout gives it.");
        }
    }

    /// <summary>Columns each glyph darkened between two rows of a BGRA surface drawn on white.</summary>
    private static List<(int Left, int Right)> DarkBands(ReadOnlySpan<byte> pixels, int stride, int widthPx, int top, int bottom)
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

    private static void AssertGlyphsMoved(
        string label, double scale, List<(int Left, int Right)> natural, List<(int Left, int Right)> spaced,
        double[] naturalAdvances, double[] spacedAdvances)
    {
        Assert.HasCount(TEXT.Length, natural, $"{label}: the natural run did not draw one stem group per glyph.");
        Assert.HasCount(TEXT.Length, spaced, $"{label}: the spaced run did not draw one stem group per glyph.");
        for (int index = 0; index < TEXT.Length; index++)
        {
            double expectedShift = index == 0 ? 0 : spacedAdvances[index - 1] - naturalAdvances[index - 1];
            Assert.AreEqual(expectedShift * scale, spaced[index].Left - natural[index].Left, 1.0,
                $"{label}: glyph {index} was not drawn where its advance puts it.");
            Assert.AreEqual(natural[index].Right - natural[index].Left, spaced[index].Right - spaced[index].Left, 1.0,
                $"{label}: glyph {index} lost ink to its run box.");
        }
    }

    /// <summary>Columns each glyph inked, from the alpha of a BGRA text bitmap.</summary>
    private static List<(int Left, int Right)> InkBands(byte[] pixels, int widthPx, int heightPx)
    {
        var bands = new List<(int Left, int Right)>();
        int start = -1;
        for (int x = 0; x <= widthPx; x++)
        {
            bool inked = false;
            for (int y = 0; x < widthPx && y < heightPx && !inked; y++)
            {
                inked = pixels[(((y * widthPx) + x) * 4) + 3] > 128;
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
}
