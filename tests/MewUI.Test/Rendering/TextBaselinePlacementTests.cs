using Aprillz.MewUI;
using Aprillz.MewUI.Native.DirectWrite;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class TextBaselinePlacementTests
{
    [TestMethod]
    public void Direct2D_NativeRunBaselineMatchesFontAscent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("DirectWrite is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(160, 48, 1));
        using var context = factory.CreateContext(surface);
        context.BeginFrame(surface);
        try
        {
            foreach (string family in new[] { "Segoe UI", "Consolas" })
            {
                using var font = factory.CreateFont(family, 16, 96);
                using var run = ((ITextBackendRenderContext)context).CreateRun("Hg", font, 120, 40);
                Assert.IsNotNull(run);
                var captured = DWriteGlyphRunExtractor.Capture(run.NativeHandle);
                Assert.IsNotEmpty(captured);
                double nativeBaseline = captured[0].BaselineOriginY;
                var layout = (ManagedTextLayout)factory.TextEngine.CreateLayout(
                    CreateRequest("Hg") with { DefaultStyle = new TextRunStyle(family, 16) });
                double managedBaseline = layout.GetRunsForTest(0)[0].Baseline;
                Console.Error.WriteLine(
                    $"{family}: font ascent={font.Ascent:F3}, managed baseline={managedBaseline:F3}, " +
                    $"native baseline={nativeBaseline:F3}");
                Assert.AreEqual(nativeBaseline, managedBaseline, 0.01,
                    $"{family} managed run baseline differs from its DirectWrite realization.");
            }
        }
        finally
        {
            context.EndFrame();
        }
    }

    [TestMethod]
    public void WindowsBackends_ReportDistinctMixedRunBaselines()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Windows text backends are unavailable.");
            return;
        }

        using var gdi = new GdiGraphicsFactory();
        AssertMixedRunMetrics(gdi);
        using var direct2D = new Direct2DGraphicsFactory();
        AssertMixedRunMetrics(direct2D);
    }

    [TestMethod]
    public void FullPath_MixedFontSizes_DrawOnTheLineBaseline()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var request = CreateRequest("ab") with
        {
            Runs = [new GeometryStyleRun(1, 1, new TextRunStyle("Segoe UI", 24))]
        };
        var layout = (ManagedTextLayout)factory.TextEngine.CreateLayout(request);
        ManagedTextRun[] runs = layout.GetRunsForTest(0).ToArray();
        Assert.HasCount(2, runs);
        Assert.AreNotEqual(runs[0].Baseline, runs[1].Baseline,
            "The fixture did not produce distinct run baselines.");

        using var graphics = new RecordingTextBackendContext();
        using var renderer = new ManagedTextRenderContext(graphics);
        var origin = new Point(7, 11);
        renderer.Draw(layout, origin, new TextDrawOptions(Color.White));

        Assert.HasCount(2, graphics.Draws);
        TextLayoutLineMetrics line = layout.Lines[0];
        for (int index = 0; index < runs.Length; index++)
        {
            double expectedTop = origin.Y + line.Bounds.Y + line.Baseline - runs[index].Baseline;
            Assert.AreEqual(expectedTop, graphics.Draws[index].Origin.Y, 0.001,
                $"Run {index} does not meet the line baseline.");
        }
    }

    [TestMethod]
    public void FullPath_InlineObject_DrawsOnTheLineBaseline()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var inline = new RecordingInlineObject(new InlineMetrics(12, 10, 4));
        using var factory = new GdiGraphicsFactory();
        var request = CreateRequest("a#") with
        {
            Inlines = [new InlineRun(1, 1, inline)]
        };
        var layout = (ManagedTextLayout)factory.TextEngine.CreateLayout(request);
        var origin = new Point(7, 11);
        using var graphics = new RecordingTextBackendContext();
        using var renderer = new ManagedTextRenderContext(graphics);
        renderer.Draw(layout, origin, new TextDrawOptions(Color.White));

        Assert.IsNotNull(inline.DrawOrigin);
        TextLayoutLineMetrics line = layout.Lines[0];
        double expectedTop = origin.Y + line.Bounds.Y + line.Baseline - inline.Metrics.Baseline;
        Assert.AreEqual(expectedTop, inline.DrawOrigin.Value.Y, 0.001,
            "The inline object does not meet the line baseline.");
    }

    [TestMethod]
    public void FullPath_TallInlineObject_FitsInsideItsLineBox()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var inline = new RecordingInlineObject(new InlineMetrics(160, 80, 80));
        using var factory = new GdiGraphicsFactory();
        var layout = (ManagedTextLayout)factory.TextEngine.CreateLayout(
            CreateRequest("#") with { Inlines = [new InlineRun(0, 1, inline)] });
        using var graphics = new RecordingTextBackendContext();
        using var renderer = new ManagedTextRenderContext(graphics);
        renderer.Draw(layout, Point.Zero, new TextDrawOptions(Color.White));

        TextLayoutLineMetrics line = layout.Lines[0];
        Assert.AreEqual(80, line.Bounds.Height, 0.001);
        Assert.AreEqual(80, line.Baseline, 0.001);
        Assert.AreEqual(line.Bounds.Y, inline.DrawOrigin!.Value.Y, 0.001);
        Assert.IsLessThanOrEqualTo(
            line.Bounds.Bottom + 0.001,
            inline.DrawOrigin.Value.Y + inline.Metrics.Height);
    }

    [TestMethod]
    public void FullPath_MixedTextAndTallInlineObject_PreservesBothSidesOfBaseline()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var inline = new RecordingInlineObject(new InlineMetrics(80, 80, 80));
        using var factory = new GdiGraphicsFactory();
        var layout = (ManagedTextLayout)factory.TextEngine.CreateLayout(
            CreateRequest("a#") with { Inlines = [new InlineRun(1, 1, inline)] });
        using var graphics = new RecordingTextBackendContext();
        using var renderer = new ManagedTextRenderContext(graphics);
        renderer.Draw(layout, Point.Zero, new TextDrawOptions(Color.White));

        TextLayoutLineMetrics line = layout.Lines[0];
        Assert.AreEqual(line.Bounds.Y, inline.DrawOrigin!.Value.Y, 0.001);
        Assert.IsGreaterThan(80, line.Bounds.Height);
        Assert.IsLessThanOrEqualTo(
            line.Bounds.Bottom + 0.001,
            inline.DrawOrigin.Value.Y + inline.Metrics.Height);
    }

    [TestMethod]
    public void FullPath_ExplicitLineHeight_SplitsSpaceAroundInlineObject()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        var inline = new RecordingInlineObject(new InlineMetrics(80, 80, 80));
        using var factory = new GdiGraphicsFactory();
        var request = CreateRequest("#") with
        {
            Inlines = [new InlineRun(0, 1, inline)],
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = double.PositiveInfinity,
                Wrapping = TextWrapping.NoWrap,
                LineHeight = 100
            }
        };
        var layout = (ManagedTextLayout)factory.TextEngine.CreateLayout(request);
        using var graphics = new RecordingTextBackendContext();
        using var renderer = new ManagedTextRenderContext(graphics);
        renderer.Draw(layout, Point.Zero, new TextDrawOptions(Color.White));

        TextLayoutLineMetrics line = layout.Lines[0];
        Assert.AreEqual(100, line.Bounds.Height, 0.001);
        Assert.AreEqual(90, line.Baseline, 0.001);
        Assert.AreEqual(10, inline.DrawOrigin!.Value.Y, 0.001);
        Assert.AreEqual(10, line.Bounds.Bottom - (inline.DrawOrigin.Value.Y + inline.Metrics.Height), 0.001);
    }

    [TestMethod]
    public void FullPath_Ellipsis_DrawsOnTheLineBaseline()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        const string text = "abcdefghijk";
        using var factory = new GdiGraphicsFactory();
        var request = CreateRequest(text) with
        {
            Runs = [new GeometryStyleRun(0, text.Length, new TextRunStyle("Segoe UI", 24))],
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = 48,
                Wrapping = TextWrapping.NoWrap,
                Trimming = TextTrimming.CharacterEllipsis,
                LineBoxTrim = LineBoxTrim.CapAndBaseline
            }
        };
        var layout = (ManagedTextLayout)factory.TextEngine.CreateLayout(request);
        Assert.IsTrue(layout.ManagedLines[0].IsTrimmed);

        var origin = new Point(7, 11);
        using var graphics = new RecordingTextBackendContext();
        using var renderer = new ManagedTextRenderContext(graphics);
        renderer.Draw(layout, origin, new TextDrawOptions(Color.White));

        DrawCall ellipsis = graphics.Draws.Single(draw => draw.Text == "...");
        TextLayoutLineMetrics line = layout.Lines[0];
        double expectedTop = origin.Y + line.Bounds.Y + line.Baseline - ellipsis.Baseline;
        Assert.AreEqual(expectedTop, ellipsis.Origin.Y, 0.001,
            "The trimming ellipsis does not meet the line baseline.");
    }

    private static TextLayoutRequest CreateRequest(string text)
        => new()
        {
            Text = text.AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle("Segoe UI", 16),
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = double.PositiveInfinity,
                Wrapping = TextWrapping.NoWrap
            }
        };

    private static void AssertMixedRunMetrics(IGraphicsFactory factory)
    {
        var request = CreateRequest("ab") with
        {
            Runs = [new GeometryStyleRun(1, 1, new TextRunStyle("Segoe UI", 24))]
        };
        var layout = (ManagedTextLayout)factory.TextEngine.CreateLayout(request);
        ManagedTextRun[] runs = layout.GetRunsForTest(0).ToArray();

        Assert.HasCount(2, runs);
        Assert.AreNotEqual(runs[0].Baseline, runs[1].Baseline,
            $"{factory.Backend} did not produce distinct run baselines.");
        Console.Error.WriteLine(
            $"{factory.Backend}: line baseline={layout.Lines[0].Baseline:F3}; " +
            $"run0 baseline={runs[0].Baseline:F3}, height={runs[0].MeasuredHeight:F3}; " +
            $"run1 baseline={runs[1].Baseline:F3}, height={runs[1].MeasuredHeight:F3}");
    }

    private sealed class RecordingInlineObject(InlineMetrics metrics) : IInlineTextObject
    {
        public InlineMetrics Metrics { get; } = metrics;
        public Point? DrawOrigin { get; private set; }

        public InlineMetrics Measure() => Metrics;

        public void Draw(ITextRenderContext context, Point origin) => DrawOrigin = origin;
    }

    private sealed class RecordingTextBackendContext : NoOpGraphicsContext, ITextBackendRenderContext
    {
        public List<DrawCall> Draws { get; } = [];

        public ITextBackendRun CreateRun(ReadOnlySpan<char> text, IFont font, double width, double height)
            => new RecordingRun(text.ToString(), font.Ascent);

        public void DrawRun(ITextBackendRun run, Point origin, Color color, object? owner)
        {
            var recording = (RecordingRun)run;
            Draws.Add(new DrawCall(recording.Text, origin, recording.Baseline));
        }
    }

    private sealed class RecordingRun(string text, double baseline) : ITextBackendRun
    {
        public string Text { get; } = text;
        public double Baseline { get; } = baseline;
        public nint NativeHandle => 0;
        public TextInkOverhang Ink => TextInkOverhang.None;
        public void Dispose() { }
    }

    private readonly record struct DrawCall(string Text, Point Origin, double Baseline);
}
