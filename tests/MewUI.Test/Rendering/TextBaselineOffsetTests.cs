using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// <see cref="TextRunStyle.BaselineOffset"/> shifts a run against the line's baseline. With B the
/// run's raster baseline, H its aligned height and S the shift, a line's ascent is max(B + S), its
/// descent max(H - B - S), and the run's raster top sits at lineBaseline - S - B.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TextBaselineOffsetTests
{
    private const string FAMILY = "Segoe UI";

    [TestMethod]
    public void ZeroOffset_LeavesLayoutAndDrawIdentical()
    {
        if (!SkipUnlessWindows())
        {
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var plain = Layout(factory, CreateRequest("ab"));
        var zero = Layout(factory, CreateRequest("ab") with
        {
            Runs = [new GeometryStyleRun(1, 1, new TextRunStyle(FAMILY, 16) { BaselineOffset = 0 })]
        });

        Assert.AreEqual(plain.Lines[0].Bounds, zero.Lines[0].Bounds);
        Assert.AreEqual(plain.Lines[0].Baseline, zero.Lines[0].Baseline);
        Assert.AreEqual(plain.MeasuredSize, zero.MeasuredSize);

        var plainDraws = Draw(plain, new Point(3, 5));
        var zeroDraws = Draw(zero, new Point(3, 5));
        Assert.AreEqual(plainDraws.Sum(draw => draw.Origin.Y * draw.Text.Length) / 2, zeroDraws.Sum(draw => draw.Origin.Y * draw.Text.Length) / 2, 0.001);
        foreach (var draw in zeroDraws)
        {
            Assert.AreEqual(plainDraws[0].Origin.Y, draw.Origin.Y, 0.001);
            Assert.AreEqual(plainDraws[0].Height, draw.Height, 0.001);
        }
    }

    [TestMethod]
    public void PositionalShapeIsUnchangedAndTheOffsetTakesPartInEquality()
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var style = new TextRunStyle(FAMILY, 16, FontWeight.Bold, true, TextDecoration.Underline, culture, "en")
        {
            BaselineOffset = 3
        };

        var (family, size, weight, italic, decoration, styleCulture, language) = style;

        Assert.AreEqual(FAMILY, family);
        Assert.AreEqual(16, size);
        Assert.AreEqual(FontWeight.Bold, weight);
        Assert.IsTrue(italic);
        Assert.AreEqual(TextDecoration.Underline, decoration);
        Assert.AreSame(culture, styleCulture);
        Assert.AreEqual("en", language);
        Assert.AreEqual(0, new TextRunStyle(FAMILY, 16).BaselineOffset);
        Assert.AreEqual(0, TextRunStyle.Default.BaselineOffset);
        Assert.AreEqual(style, style with { });
        Assert.AreEqual(style.GetHashCode(), (style with { }).GetHashCode());
        Assert.AreNotEqual(style, style with { BaselineOffset = 0 });
        Assert.AreNotEqual(
            new GeometryStyleRun(0, 1, style),
            new GeometryStyleRun(0, 1, style with { BaselineOffset = -3 }));
    }

    [TestMethod]
    [DataRow(4.0, "Gdi", 96)]
    [DataRow(-4.0, "Gdi", 96)]
    [DataRow(0.375, "Gdi", 96)]
    [DataRow(1e6, "Gdi", 96)]
    [DataRow(4.0, "Gdi", 120)]
    [DataRow(-4.0, "Gdi", 144)]
    [DataRow(0.375, "Gdi", 192)]
    [DataRow(4.0, "Direct2D", 96)]
    [DataRow(-4.0, "Direct2D", 120)]
    [DataRow(0.375, "Direct2D", 144)]
    [DataRow(-1e6, "Direct2D", 192)]
    public void MixedOffsets_FollowTheContributionFormula(double offset, string backend, int dpi)
    {
        if (!SkipUnlessWindows())
        {
            return;
        }

        using IGraphicsFactory factory = backend == "Direct2D"
            ? new Aprillz.MewUI.Rendering.Direct2D.Direct2DGraphicsFactory()
            : new GdiGraphicsFactory();
        var request = CreateRequest("ab") with
        {
            Dpi = (uint)dpi,
            Runs = [new GeometryStyleRun(1, 1, new TextRunStyle(FAMILY, 24) { BaselineOffset = offset })]
        };
        var layout = (ManagedTextLayout)factory.TextEngine.CreateLayout(request);
        ManagedTextRun[] runs = layout.GetRunsForTest(0).ToArray();
        Assert.HasCount(2, runs);
        Assert.AreEqual(0, runs[0].BaselineOffset);
        Assert.AreEqual(offset, runs[1].BaselineOffset);

        double ascent = Math.Max(runs[0].Baseline, runs[1].Baseline + offset);
        double descent = Math.Max(
            Math.Max(0, runs[0].Font.Ascent + runs[0].Font.Descent - runs[0].Baseline),
            Math.Max(0, runs[1].Font.Ascent + runs[1].Font.Descent - runs[1].Baseline - offset));
        double contentHeight = ascent + descent;
        TextLayoutLineMetrics line = layout.Lines[0];
        Console.Error.WriteLine(
            $"offset={offset}: B0={runs[0].Baseline:F3} B1={runs[1].Baseline:F3} " +
            $"line baseline={line.Baseline:F3} height={line.Bounds.Height:F3} contentHeight={contentHeight:F3}");
        Assert.IsTrue(double.IsFinite(line.Bounds.Height) && double.IsFinite(line.Baseline));
        Assert.IsGreaterThanOrEqualTo(contentHeight - 0.001, line.Bounds.Height);
        double halfLeading = Math.Max(0, line.Bounds.Height - contentHeight) / 2;
        Assert.AreEqual(ascent + halfLeading, line.Baseline, 0.001, "line baseline is not the shifted ascent");

        var origin = new Point(7, 11);
        var draws = Draw(layout, origin);
        Assert.HasCount(2, draws);
        double lineBaselineY = origin.Y + line.Bounds.Y + line.Baseline;
        Assert.AreEqual(lineBaselineY - runs[0].Baseline, draws[0].Origin.Y, 0.001, "unshifted run left the line baseline");
        Assert.AreEqual(lineBaselineY - offset - runs[1].Baseline, draws[1].Origin.Y, 0.001, "shifted run is not at lineBaseline - S - B");
    }

    [TestMethod]
    [DataRow(5.0)]
    [DataRow(-5.0)]
    public void ShiftedRunRealizationStillCoversItsRaster(double offset)
    {
        if (!SkipUnlessWindows())
        {
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var plain = Layout(factory, CreateRequest("ab") with
        {
            Runs = [new GeometryStyleRun(1, 1, new TextRunStyle(FAMILY, 24))]
        });
        var shifted = Layout(factory, CreateRequest("ab") with
        {
            Runs = [new GeometryStyleRun(1, 1, new TextRunStyle(FAMILY, 24) { BaselineOffset = offset })]
        });

        var plainDraws = Draw(plain, Point.Zero);
        var shiftedDraws = Draw(shifted, Point.Zero);
        ManagedTextRun[] runs = shifted.GetRunsForTest(0).ToArray();
        for (int index = 0; index < 2; index++)
        {
            Assert.AreEqual(plainDraws[index].Width, shiftedDraws[index].Width, 0.001);
            Assert.IsGreaterThanOrEqualTo(runs[index].MeasuredHeight - 0.001, shiftedDraws[index].Height,
                $"run {index} realized shorter than its raster once shifted by {offset}");
        }
        // The unshifted neighbour keeps the box it always had: from its raster top to the line bottom.
        TextLayoutLineMetrics line = shifted.Lines[0];
        Assert.AreEqual(line.Bounds.Bottom - shiftedDraws[0].Origin.Y, shiftedDraws[0].Height, 0.001);
    }

    [TestMethod]
    public void OffsetChangesNeitherAdvancesNorCaretColumns()
    {
        if (!SkipUnlessWindows())
        {
            return;
        }

        using var factory = new GdiGraphicsFactory();
        const string text = "한글 text 🙂 end";
        var plain = Layout(factory, CreateRequest(text) with
        {
            Runs = [new GeometryStyleRun(3, 4, new TextRunStyle(FAMILY, 16))]
        });
        var shifted = Layout(factory, CreateRequest(text) with
        {
            Runs = [new GeometryStyleRun(3, 4, new TextRunStyle(FAMILY, 16) { BaselineOffset = -6 })]
        });

        for (int insertion = 0; insertion <= text.Length; insertion++)
        {
            Assert.AreEqual(
                plain.GetCaretBounds(new CharacterHit(insertion, 0)).X,
                shifted.GetCaretBounds(new CharacterHit(insertion, 0)).X,
                0.001,
                $"caret column {insertion} moved");
        }
        for (double x = 0; x < plain.MeasuredSize.Width; x += 3.5)
        {
            Assert.AreEqual(plain.HitTestPoint(new Point(x, 2)), shifted.HitTestPoint(new Point(x, 2)));
        }
        Assert.AreEqual(plain.MeasuredSize.Width, shifted.MeasuredSize.Width, 0.001);
    }

    [TestMethod]
    public void RangeAndCaretBoundsKeepTheLineHeight()
    {
        if (!SkipUnlessWindows())
        {
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var layout = Layout(factory, CreateRequest("abc") with
        {
            Runs = [new GeometryStyleRun(1, 1, new TextRunStyle(FAMILY, 16) { BaselineOffset = 6 })]
        });
        TextLayoutLineMetrics line = layout.Lines[0];
        var bounds = new List<Rect>();
        layout.GetRangeBounds(1, 1, bounds);
        Assert.HasCount(1, bounds);
        Assert.AreEqual(line.Bounds.Y, bounds[0].Y, 0.001);
        Assert.AreEqual(line.Bounds.Height, bounds[0].Height, 0.001);
        var caret = layout.GetCaretBounds(new CharacterHit(1, 0));
        Assert.AreEqual(line.Bounds.Height, caret.Height, 0.001);
    }

    [TestMethod]
    public void OffsetReusesTheFontAndOnlyChangesTheCacheKey()
    {
        if (!SkipUnlessWindows())
        {
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var layout = Layout(factory, CreateRequest("ab") with
        {
            Runs = [new GeometryStyleRun(1, 1, new TextRunStyle(FAMILY, 16) { BaselineOffset = 3 })]
        });
        ManagedTextRun[] runs = layout.GetRunsForTest(0).ToArray();
        Assert.AreSame(runs[0].Font, runs[1].Font, "a shifted run created a font of its own");

        var engine = factory.TextEngine;
        var first = engine.GetOrCreateLayout(CreateRequest("ab") with
        {
            DefaultStyle = new TextRunStyle(FAMILY, 16) { BaselineOffset = 2 }
        }, TextLayoutCachePolicy.Content);
        var same = engine.GetOrCreateLayout(CreateRequest("ab") with
        {
            DefaultStyle = new TextRunStyle(FAMILY, 16) { BaselineOffset = 2 }
        }, TextLayoutCachePolicy.Content);
        var other = engine.GetOrCreateLayout(CreateRequest("ab") with
        {
            DefaultStyle = new TextRunStyle(FAMILY, 16) { BaselineOffset = 2.5 }
        }, TextLayoutCachePolicy.Content);
        Assert.AreSame(first, same);
        Assert.AreNotSame(first, other);

        var owner = new object();
        var ownerFirst = engine.GetOrCreateLayout(CreateRequest("ab") with
        {
            DefaultStyle = new TextRunStyle(FAMILY, 16) { BaselineOffset = 2 }
        }, TextLayoutCachePolicy.Owner, owner);
        var ownerChanged = engine.GetOrCreateLayout(CreateRequest("ab") with
        {
            DefaultStyle = new TextRunStyle(FAMILY, 16) { BaselineOffset = -2 }
        }, TextLayoutCachePolicy.Owner, owner);
        Assert.AreNotSame(ownerFirst, ownerChanged, "owner cache kept a layout whose offset changed");
    }

    [TestMethod]
    [DataRow(double.NaN)]
    [DataRow(double.PositiveInfinity)]
    [DataRow(double.NegativeInfinity)]
    [DataRow(double.MaxValue)]
    public void InvalidOffsetIsRejected(double offset)
    {
        if (!SkipUnlessWindows())
        {
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => factory.TextEngine.CreateLayout(
            CreateRequest("ab") with { DefaultStyle = new TextRunStyle(FAMILY, 16) { BaselineOffset = offset } }));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => factory.TextEngine.CreateLayout(
            CreateRequest("ab") with
            {
                Runs = [new GeometryStyleRun(0, 1, new TextRunStyle(FAMILY, 16) { BaselineOffset = offset })]
            }));
    }

    [TestMethod]
    public void DefaultStyleOffset_LeavesTheFastPathAndShiftsEveryRun()
    {
        if (!SkipUnlessWindows())
        {
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var plain = Layout(factory, CreateRequest("abc"));
        Assert.IsTrue(plain.IsFastPath, "fixture is not a fast-path layout");
        var shifted = Layout(factory, CreateRequest("abc") with
        {
            DefaultStyle = new TextRunStyle(FAMILY, 16) { BaselineOffset = 4 }
        });
        Assert.IsFalse(shifted.IsFastPath, "a default offset must not take the single-run fast path");

        TextLayoutLineMetrics line = shifted.Lines[0];
        ManagedTextRun run = shifted.GetRunsForTest(0)[0];
        Assert.AreEqual(4, run.BaselineOffset);
        // A lone shifted run keeps its box: the line baseline moves down by the shift instead.
        Assert.AreEqual(plain.Lines[0].Bounds.Height, line.Bounds.Height, 0.001);
        Assert.AreEqual(plain.Lines[0].Baseline + 4, line.Baseline, 0.001);
        var draws = Draw(shifted, Point.Zero);
        Assert.AreEqual(line.Bounds.Y + line.Baseline - 4 - run.Baseline, draws[0].Origin.Y, 0.001);
    }

    [TestMethod]
    public void EmptyNewlineAndTabOnlyLinesStayFinite()
    {
        if (!SkipUnlessWindows())
        {
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var style = new TextRunStyle(FAMILY, 16) { BaselineOffset = 5 };
        var reference = Layout(factory, CreateRequest("\n\t\n") with
        {
            DefaultStyle = new TextRunStyle(FAMILY, 16)
        });
        foreach (string text in new[] { "", "\n", "\t", "\n\t\n" })
        {
            var layout = Layout(factory, CreateRequest(text) with { DefaultStyle = style });
            Assert.IsNotEmpty(layout.Lines);
            foreach (var line in layout.Lines)
            {
                Assert.IsTrue(double.IsFinite(line.Bounds.Height) && line.Bounds.Height > 0, $"'{text}' produced an empty line box");
                Assert.IsTrue(double.IsFinite(line.Baseline), $"'{text}' produced a non-finite baseline");
            }
        }

        // A shift no larger than the font's descent leaves every box the same size, since the
        // descent side just gives the ascent side what it took.
        double descent = reference.GetDefaultFont().Descent;
        double uniform = Math.Min(2, descent);
        var shiftedLines = Layout(factory, CreateRequest("\n\t\n") with
        {
            DefaultStyle = new TextRunStyle(FAMILY, 16) { BaselineOffset = uniform }
        }).Lines;
        for (int index = 0; index < reference.Lines.Count; index++)
        {
            Assert.AreEqual(reference.Lines[index].Bounds.Height, shiftedLines[index].Bounds.Height, 0.001,
                $"line {index} of a uniformly shifted layout changed height");
            Assert.AreEqual(reference.Lines[index].Baseline + uniform, shiftedLines[index].Baseline, 0.001,
                $"line {index} of a uniformly shifted layout did not move its baseline by the shift");
        }
    }

    [TestMethod]
    public void InlineObject_DrawsAtTheShiftedBaseline()
    {
        if (!SkipUnlessWindows())
        {
            return;
        }

        var inline = new RecordingInlineObject(new InlineMetrics(12, 10, 4));
        using var factory = new GdiGraphicsFactory();
        var layout = Layout(factory, CreateRequest("a#") with
        {
            Inlines = [new InlineRun(1, 1, inline)],
            Runs = [new GeometryStyleRun(1, 1, new TextRunStyle(FAMILY, 16) { BaselineOffset = 3 })]
        });
        using var graphics = new RecordingTextBackendContext();
        using var renderer = new ManagedTextRenderContext(graphics);
        var origin = new Point(7, 11);
        renderer.Draw(layout, origin, new TextDrawOptions(Color.White));

        Assert.AreEqual(1, inline.MeasureCalls);
        TextLayoutLineMetrics line = layout.Lines[0];
        Assert.AreEqual(origin.Y + line.Bounds.Y + line.Baseline - 3 - inline.Metrics.Baseline,
            inline.DrawOrigin!.Value.Y, 0.001);
    }

    [TestMethod]
    public void Ellipsis_FollowsTheLastRunsShift()
    {
        if (!SkipUnlessWindows())
        {
            return;
        }

        const string text = "abcdefghijk";
        using var factory = new GdiGraphicsFactory();
        var layout = Layout(factory, CreateRequest(text) with
        {
            Runs = [new GeometryStyleRun(0, text.Length, new TextRunStyle(FAMILY, 24) { BaselineOffset = -3 })],
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = 48,
                Wrapping = TextWrapping.NoWrap,
                Trimming = TextTrimming.CharacterEllipsis
            }
        });
        Assert.IsTrue(layout.ManagedLines[0].IsTrimmed);
        var draws = Draw(layout, new Point(7, 11));
        DrawCall ellipsis = draws.Single(draw => draw.Text == "...");
        DrawCall last = draws.Last(draw => draw.Text != "...");
        Assert.AreEqual(last.Origin.Y, ellipsis.Origin.Y, 0.001, "the ellipsis left the shifted run's baseline");
    }

    [TestMethod]
    public void RunDecoration_FollowsTheShiftedBaseline()
    {
        if (!SkipUnlessWindows())
        {
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var plain = Layout(factory, CreateRequest("ab") with
        {
            Runs = [new GeometryStyleRun(1, 1, new TextRunStyle(FAMILY, 16, Decoration: TextDecoration.Underline))]
        });
        var raised = Layout(factory, CreateRequest("ab") with
        {
            Runs = [new GeometryStyleRun(1, 1, new TextRunStyle(FAMILY, 16, Decoration: TextDecoration.Underline) { BaselineOffset = 6 })]
        });

        using var plainGraphics = new RecordingTextBackendContext();
        using var plainRenderer = new ManagedTextRenderContext(plainGraphics);
        plainRenderer.Draw(plain, Point.Zero, new TextDrawOptions(Color.White));
        using var raisedGraphics = new RecordingTextBackendContext();
        using var raisedRenderer = new ManagedTextRenderContext(raisedGraphics);
        raisedRenderer.Draw(raised, Point.Zero, new TextDrawOptions(Color.White));

        Assert.HasCount(1, plainGraphics.Fills);
        Assert.HasCount(1, raisedGraphics.Fills);
        double plainBaselineY = plain.Lines[0].Bounds.Y + plain.Lines[0].Baseline;
        double raisedBaselineY = raised.Lines[0].Bounds.Y + raised.Lines[0].Baseline - 6;
        Assert.AreEqual(plainGraphics.Fills[0].Y - plainBaselineY, raisedGraphics.Fills[0].Y - raisedBaselineY, 1.001,
            "the underline did not move with the run's baseline");
    }

    [TestMethod]
    public void LineBoxTrim_None_KeepsRaisedInkInsideTheBox()
    {
        if (!SkipUnlessWindows())
        {
            return;
        }

        using var factory = new GdiGraphicsFactory();
        foreach (var trim in new[] { LineBoxTrim.None, LineBoxTrim.Cap, LineBoxTrim.CapAndBaseline })
        {
            var layout = Layout(factory, CreateRequest("ab") with
            {
                Runs = [new GeometryStyleRun(1, 1, new TextRunStyle(FAMILY, 16) { BaselineOffset = 8 })],
                Paragraph = new TextParagraphStyle { MaxWidth = double.PositiveInfinity, LineBoxTrim = trim }
            });
            TextLayoutLineMetrics line = layout.Lines[0];
            var draws = Draw(layout, Point.Zero);
            double rasterTop = draws[1].Origin.Y;
            Console.Error.WriteLine($"{trim}: box top={line.Bounds.Y:F3} raised raster top={rasterTop:F3}");
            if (trim == LineBoxTrim.None)
            {
                Assert.IsGreaterThanOrEqualTo(line.Bounds.Y - 0.001, rasterTop, "None trim let raised ink leave the box");
            }
            else
            {
                Assert.IsLessThan(line.Bounds.Y, rasterTop, "cap trim is expected to let raised ink overflow the box");
            }
        }
    }

    private static bool SkipUnlessWindows()
    {
        if (OperatingSystem.IsWindows())
        {
            return true;
        }

        Assert.Inconclusive("GDI is Windows-only.");
        return false;
    }

    private static ManagedTextLayout Layout(GdiGraphicsFactory factory, TextLayoutRequest request)
        => (ManagedTextLayout)factory.TextEngine.CreateLayout(request);

    private static List<DrawCall> Draw(ManagedTextLayout layout, Point origin)
    {
        using var graphics = new RecordingTextBackendContext();
        using var renderer = new ManagedTextRenderContext(graphics);
        renderer.Draw(layout, origin, new TextDrawOptions(Color.White));
        return graphics.Draws;
    }

    private static TextLayoutRequest CreateRequest(string text)
        => new()
        {
            Text = text.AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle(FAMILY, 16),
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = double.PositiveInfinity,
                Wrapping = TextWrapping.NoWrap
            }
        };

    private sealed class RecordingInlineObject(InlineMetrics metrics) : IInlineTextObject
    {
        public InlineMetrics Metrics { get; } = metrics;
        public Point? DrawOrigin { get; private set; }
        public int MeasureCalls { get; private set; }

        public InlineMetrics Measure()
        {
            MeasureCalls++;
            return Metrics;
        }

        public void Draw(ITextRenderContext context, Point origin) => DrawOrigin = origin;
    }

    private sealed class RecordingTextBackendContext : NoOpGraphicsContext, ITextBackendRenderContext
    {
        public List<DrawCall> Draws { get; } = [];
        public List<Rect> Fills { get; } = [];

        public ITextBackendRun CreateRun(ReadOnlySpan<char> text, IFont font, double width, double height)
            => new RecordingRun(text.ToString(), width, height);

        public void DrawRun(ITextBackendRun run, Point origin, Color color, object? owner)
        {
            var recording = (RecordingRun)run;
            Draws.Add(new DrawCall(recording.Text, origin, recording.Width, recording.Height));
        }

        public override void FillRectangle(Rect rect, Color color) => Fills.Add(rect);
    }

    private sealed class RecordingRun(string text, double width, double height) : ITextBackendRun
    {
        public string Text { get; } = text;
        public double Width { get; } = width;
        public double Height { get; } = height;
        public nint NativeHandle => 0;
        public TextInkOverhang Ink => TextInkOverhang.None;
        public void Dispose() { }
    }

    private readonly record struct DrawCall(string Text, Point Origin, double Width, double Height);
}
