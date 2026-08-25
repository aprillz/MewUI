using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Test.Rendering;

/// <summary>
/// An inline object cuts the line it sits on into separate text runs, and a run is drawn at a whole
/// device pixel. Adding one must therefore not move the glyphs around it: an editor that turns
/// whitespace markers on would otherwise shift the whole line under the caret.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class InlineRunSplitStabilityTests
{
    // A control character stands where the box element would, as the editor's sample line has it.
    private const string TEXT = "before\u0002the C0 block is named";
    private const int BOX = 6;

    /// <summary>
    /// A box around a control character measures its name plus padding, which is a fractional device
    /// pixel at 150%. The padding an editor uses, in DIPs.
    /// </summary>
    private const double BOX_PADDING = 3.0;
    private const int WIDTH_PX = 900;
    private const int HEIGHT_PX = 40;

    private sealed class ZeroInkInline(double width) : IInlineTextObject
    {
        public InlineMetrics Measure() => new(width, 16, 12);
        public void Draw(ITextRenderContext context, Point origin) { }
    }

    [TestMethod]
    public void Direct2DDrawsTheSameGlyphsWhenInlinesSplitTheLine()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D is Windows-only.");
        }

        using var factory = new Aprillz.MewUI.Rendering.Direct2D.Direct2DGraphicsFactory();
        AssertStable(factory);
    }

    [TestMethod]
    public void GdiDrawsTheSameGlyphsWhenInlinesSplitTheLine()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
        }

        using var factory = new Aprillz.MewUI.Rendering.Gdi.GdiGraphicsFactory();
        AssertStable(factory);
    }

    /// <summary>
    /// Cluster boundaries land on whole device pixels on every backend. A fractional boundary lets
    /// each run round on its own, which is what makes splitting move the glyphs.
    /// </summary>
    [TestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    public void ClusterBoundariesLandOnWholePixels(double scale)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows-only backends.");
        }

        using var factory = new Aprillz.MewUI.Rendering.Gdi.GdiGraphicsFactory();
        var layout = CreateLayout(factory, scale, "Segoe UI", 12, withBox: true, markers: true);
        for (int index = 0; index <= TEXT.Length; index++)
        {
            double devicePixels = layout.GetCaretBounds(new CharacterHit(index, 0)).X * scale;
            Assert.AreEqual(Math.Round(devicePixels), devicePixels, 0.001,
                $"Caret {index} sits between device pixels.");
        }
    }

    private static void AssertStable(IGraphicsFactory factory)
    {
        var moved = new List<string>();
        foreach (double scale in new[] { 1.0, 1.25, 1.5 })
        {
            foreach (string family in new[] { "Consolas", "Segoe UI" })
            {
                // Whole device pixels: putting a layout there is the caller's part of the bargain,
                // which the text controls keep by rounding their scroll offset. Off the grid every
                // run rounds on its own and no amount of engine work makes splitting invisible.
                foreach (double originPx in new[] { 0.0, 3.0, 4.0 })
                {
                    foreach (double translatePx in new[] { 0.0, 1.0 })
                    {
                        // Without the box the unmarked line has no inline at all, which is the
                        // layout the engine draws through its fast path; turning markers on moves it
                        // to the cluster path, so the two renderers have to agree as well.
                        foreach (bool withBox in new[] { true, false })
                        {
                            double originX = originPx / scale;
                            double translateX = translatePx / scale;
                            var whole = InkColumns(factory, scale, family, originX, translateX, withBox, markers: false);
                            var split = InkColumns(factory, scale, family, originX, translateX, withBox, markers: true);
                            int differing = whole.Except(split).Count() + split.Except(whole).Count();
                            // A run realized on its own can anti-alias its outer edge differently,
                            // which shows up as a single faint column. A glyph that actually moved
                            // costs at least the column it left and the one it took.
                            if (differing > 1)
                            {
                                moved.Add(
                                    $"scale={scale:0.##} {family} origin={originPx}px translate={translatePx}px " +
                                    $"box={withBox}: {differing} columns");
                            }
                        }
                    }
                }
            }
        }

        Assert.IsEmpty(moved, $"Inlines moved the surrounding glyphs: {string.Join("; ", moved)}");
    }

    private static ITextLayout CreateLayout(
        IGraphicsFactory factory, double scale, string family, double size, bool withBox, bool markers)
    {
        uint dpi = (uint)Math.Round(scale * 96);
        var style = new TextRunStyle(family, size);
        var paragraph = new TextParagraphStyle { Wrapping = TextWrapping.NoWrap, MaxWidth = double.PositiveInfinity };
        var engine = factory.TextEngine;

        double spaceWidth = engine.CreateLayout(new TextLayoutRequest
        {
            Text = " ".AsMemory(),
            Dpi = dpi,
            DefaultStyle = style,
            Paragraph = paragraph
        }).MeasuredSize.Width;

        double boxWidth = engine.CreateLayout(new TextLayoutRequest
        {
            Text = "STX".AsMemory(),
            Dpi = dpi,
            DefaultStyle = style,
            Paragraph = paragraph
        }).MeasuredSize.Width + BOX_PADDING;

        var inlines = new List<InlineRun>();
        if (withBox)
        {
            inlines.Add(new InlineRun(BOX, 1, new ZeroInkInline(boxWidth)));
        }
        if (markers)
        {
            for (int index = 0; index < TEXT.Length; index++)
            {
                if (TEXT[index] == ' ')
                {
                    inlines.Add(new InlineRun(index, 1, new ZeroInkInline(spaceWidth)));
                }
            }
        }
        return engine.CreateLayout(new TextLayoutRequest
        {
            Text = TEXT.AsMemory(),
            Dpi = dpi,
            DefaultStyle = style,
            Paragraph = paragraph,
            Inlines = inlines
        });
    }

    private static List<int> InkColumns(
        IGraphicsFactory factory, double scale, string family, double originX, double translateX, bool withBox, bool markers)
    {
        var layout = CreateLayout(factory, scale, family, 12, withBox, markers);

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH_PX, HEIGHT_PX, scale));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.FromArgb(255, 255, 255, 255));
            var options = new TextDrawOptions(Color.FromArgb(255, 0, 0, 0));
            context.Translate(translateX, 0);
            context.Text.Draw(layout, new Point(originX, 2), in options);
            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        int stride = cpu.StrideBytes;
        var columns = new List<int>();
        for (int x = 0; x < WIDTH_PX; x++)
        {
            for (int y = 0; y < HEIGHT_PX; y++)
            {
                if (pixels[y * stride + x * 4 + 1] < 160)
                {
                    columns.Add(x);
                    break;
                }
            }
        }
        return columns;
    }
}
