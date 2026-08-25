using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;
using System.Diagnostics;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class TextEngineTier0Tests
{
    [TestMethod]
    public void CalendarStyleContent_UsesUnifiedFastPathAndContentCache()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var firstPass = new ITextLayout[31];
        for (int day = 1; day <= 31; day++)
        {
            string text = day.ToString();
            var request = CreateRequest(text, TextWrapping.NoWrap, double.PositiveInfinity);
            var layout = factory.TextEngine.GetOrCreateLayout(request, TextLayoutCachePolicy.Content);
            firstPass[day - 1] = layout;
            Assert.IsTrue(((ManagedTextLayout)layout).IsFastPath);
        }

        for (int day = 1; day <= 31; day++)
        {
            string text = day.ToString();
            var request = CreateRequest(text, TextWrapping.NoWrap, double.PositiveInfinity);
            var layout = factory.TextEngine.GetOrCreateLayout(request, TextLayoutCachePolicy.Content);
            Assert.AreSame(firstPass[day - 1], layout);
        }

        Assert.AreEqual(31, factory.TextEngine.ManagedCache.Count);
    }

    [TestMethod]
    public void FullPath_WrapsAndPreservesTextElementCaretBoundaries()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        const string text = "A😀e\u0301한 A😀e\u0301한";
        using var factory = new GdiGraphicsFactory();
        var layout = factory.TextEngine.CreateLayout(CreateRequest(text, TextWrapping.Wrap, 55));

        Assert.IsFalse(((ManagedTextLayout)layout).IsFastPath);
        Assert.IsGreaterThanOrEqualTo(2, layout.Lines.Count);

        var hit = new CharacterHit(0, 0);
        var boundaries = new List<int> { 0 };
        while (hit.InsertionIndex < text.Length)
        {
            hit = layout.GetNextLogicalCaret(hit, LogicalDirection.Forward, CaretMode.TextElement);
            boundaries.Add(hit.InsertionIndex);
        }

        CollectionAssert.Contains(boundaries, 3);
        CollectionAssert.Contains(boundaries, 5);
        CollectionAssert.DoesNotContain(boundaries, 2, "Caret split the UTF-16 surrogate pair.");
        CollectionAssert.DoesNotContain(boundaries, 4, "Caret split the combining sequence.");

        foreach (int boundary in boundaries)
        {
            var caret = layout.GetCaretBounds(new CharacterHit(boundary, 0));
            var roundTrip = layout.HitTestPoint(new Point(caret.X, caret.Y + caret.Height * 0.5));
            Assert.AreEqual(boundary, roundTrip.InsertionIndex);
        }
    }

    [TestMethod]
    public void Draw_UsesFrameTextSurfaceWithoutChangingLegacyCalls()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        const int width = 200;
        const int height = 48;
        using var factory = new GdiGraphicsFactory();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(width, height, 1));
        using var context = factory.CreateContext(surface);
        var layout = factory.TextEngine.CreateLayout(CreateRequest("Calendar 31", TextWrapping.NoWrap, 180));

        context.BeginFrame(surface);
        context.Clear(Color.Transparent);
        var textSurface = (ManagedTextRenderContext)context.Text;
        textSurface.Draw(layout, new Point(4, 4), new TextDrawOptions(Color.White));
        int realizationCount = textSurface.CachedLayoutCount;
        TextPaintSpan[] paint =
        [
            new(new TextRange(0, 8), Foreground: Color.Red),
            new(new TextRange(9, 2), Foreground: Color.Blue)
        ];
        textSurface.Draw(layout, new Point(4, 24), new TextDrawOptions(Color.White, paint));
        context.EndFrame();

        Assert.IsGreaterThanOrEqualTo(1, realizationCount);
        Assert.AreEqual(realizationCount, textSurface.CachedLayoutCount,
            "Changing paint spans/origin must not split or recreate geometry realization.");

        var pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int covered = 0;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 16)
            {
                covered++;
            }
        }
        Assert.IsGreaterThanOrEqualTo(5, covered);
    }

    [TestMethod]
    public void GeometryRuns_CannotSplitUnicodeTextElements()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        var request = CreateRequest("A😀e\u0301", TextWrapping.NoWrap, double.PositiveInfinity) with
        {
            Runs = [new GeometryStyleRun(2, 1, new TextRunStyle("Segoe UI", 18))]
        };

        Assert.ThrowsExactly<ArgumentException>(() => factory.TextEngine.CreateLayout(request));
    }

    [TestMethod]
    public void PaintDecoration_DrawsWithoutCreatingGeometryRuns()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(160, 48, 1));
        using var context = factory.CreateContext(surface);
        var layout = factory.TextEngine.CreateLayout(CreateRequest("composition", TextWrapping.NoWrap, 150));
        TextPaintSpan[] paint =
        [
            new(new TextRange(0, "composition".Length),
                Foreground: Color.White,
                Decoration: TextDecoration.Underline | TextDecoration.Strikethrough)
        ];

        context.BeginFrame(surface);
        context.Clear(Color.Transparent);
        var renderContext = (ManagedTextRenderContext)context.Text;
        renderContext.Draw(layout, new Point(4, 4), new TextDrawOptions(Color.White, paint));
        int realizationCount = renderContext.CachedLayoutCount;
        renderContext.Draw(layout, new Point(4, 24), new TextDrawOptions(Color.White));
        context.EndFrame();

        Assert.AreEqual(realizationCount, renderContext.CachedLayoutCount,
            "Paint decorations must not create geometry realizations.");
        var pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int covered = 0;
        for (int index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] > 16) covered++;
        }
        Assert.IsGreaterThan(20, covered);
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public void TenMegabyteSingleLine_OwnerFastPathDoesNotBuildContentKeyOrClustersToDraw()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        string text = new('a', 10_000_000);
        using var factory = new GdiGraphicsFactory();
        var owner = new object();
        var request = CreateRequest(text, TextWrapping.NoWrap, double.PositiveInfinity) with
        {
            Revision = 1
        };
        var stopwatch = Stopwatch.StartNew();
        var layout = (ManagedTextLayout)factory.TextEngine.GetOrCreateLayout(
            request,
            TextLayoutCachePolicy.Owner,
            owner);
        long layoutMilliseconds = stopwatch.ElapsedMilliseconds;

        Assert.IsTrue(layout.IsFastPath);
        Assert.IsFalse(layout.Snapshot.HasMaterializedContentKey,
            "Owner caching hashed/copied the entire 10MB line into a content key.");
        Assert.IsFalse(layout.HasMaterializedColumns,
            "Fast-path layout eagerly built columns for the whole 10MB line.");

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(320, 48, 1));
        using var context = factory.CreateContext(surface);
        stopwatch.Restart();
        context.BeginFrame(surface);
        context.Clear(Color.Transparent);
        context.IntersectClip(new Rect(0, 0, 320, 48));
        context.Text.Draw(layout, new Point(0, 0), new TextDrawOptions(Color.White, Owner: owner));
        context.EndFrame();
        long drawMilliseconds = stopwatch.ElapsedMilliseconds;
        Console.WriteLine($"10MB engine layout={layoutMilliseconds}ms draw={drawMilliseconds}ms");
        Assert.IsLessThan(750L, layoutMilliseconds,
            $"10MB Fast Path layout regressed to {layoutMilliseconds}ms.");
        Assert.IsLessThan(250L, drawMilliseconds,
            $"A clipped 10MB Fast Path draw regressed to {drawMilliseconds}ms.");

        Assert.IsFalse(layout.HasMaterializedColumns,
            "Drawing an undecorated fast-path line built columns for the whole line.");

        Rect endCaret = layout.GetCaretBounds(new CharacterHit(text.Length, 0));
        Assert.AreEqual(layout.MeasuredSize.Width, endCaret.X, 0.01);
        Assert.AreEqual(text.Length, layout.HitTestPoint(
            new Point(endCaret.X, endCaret.Y + endCaret.Height * 0.5)).InsertionIndex);
        int nearStart = layout.HitTestPoint(new Point(12, endCaret.Height * 0.5)).InsertionIndex;
        Assert.IsGreaterThan(0, nearStart);
        Assert.IsLessThan(text.Length, nearStart);
        Assert.IsFalse(layout.HasMaterializedColumns,
            "Fast-path end caret and hit-test built columns for the whole line.");
    }

    [TestMethod]
    public void RealizationCache_BoundsDistinctLayouts()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(320, 48, 1));
        using var context = factory.CreateContext(surface);
        var renderContext = (ManagedTextRenderContext)context.Text;
        context.BeginFrame(surface);
        for (int index = 0; index < 192; index++)
        {
            string text = $"cache entry {index}";
            var layout = factory.TextEngine.CreateLayout(CreateRequest(text, TextWrapping.NoWrap, 300));
            renderContext.Draw(layout, Point.Zero, new TextDrawOptions(Color.White));
        }
        context.EndFrame();

        Assert.AreEqual(128, renderContext.CachedLayoutCount,
            "Text run realizations grew beyond the bounded cache capacity.");
    }

    private static TextLayoutRequest CreateRequest(string text, TextWrapping wrapping, double maxWidth)
        => new()
        {
            Text = text.AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle("Segoe UI", 16),
            Paragraph = new TextParagraphStyle
            {
                MaxWidth = maxWidth,
                Wrapping = wrapping
            }
        };
}
