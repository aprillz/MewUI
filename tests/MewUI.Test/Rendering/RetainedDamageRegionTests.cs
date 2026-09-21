using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Two small changes far apart are two small repaints. Joining them into one box would repaint
/// everything between them, which the cost measurements put at many times the price on a backend that
/// paints per pixel. Past a large share of the surface the frame is drawn whole instead.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedDamageRegionTests
{
    private const int WIDTH = 400;
    private const int HEIGHT = 300;

    private sealed class Tile : Control
    {
        internal int RecordCount { get; private set; }

        internal Color Fill { get; set; }

        protected override Size MeasureContent(Size availableSize) => new(30, 20);

        protected override void OnRender(IGraphicsContext context)
        {
            RecordCount++;
            context.FillRectangle(Bounds, Fill);
        }
    }

    [TestMethod]
    public void ChangesAtOppositeCorners_AreRepaintedAsSeparateAreas()
    {
        if (!TryCreate(out var factory, out var window, out var surface, out var tiles))
        {
            return;
        }

        using (factory)
        using (surface)
        {
            var topLeft = tiles[0];
            var bottomRight = tiles[^1];
            topLeft.Fill = Color.FromArgb(255, 250, 40, 40);
            topLeft.InvalidateVisual();
            bottomRight.Fill = Color.FromArgb(255, 40, 40, 250);
            bottomRight.InvalidateVisual();
            window.RetainedStatistics!.Reset();
            Frame(window, surface);

            // One box around both tiles would replay every tile between them.
            int replayed = window.RetainedStatistics!.ContentReplayCount;
            Assert.IsLessThanOrEqualTo(8, replayed, $"repainting two tiles replayed {replayed} recordings");

            var areas = window.LastRetainedDamageAreas;
            Assert.IsNotNull(window.LastRetainedDamage, "the frame was drawn whole");
            Assert.HasCount(2, areas, $"two distant changes became {areas.Count} area(s)");
            double repainted = areas.Sum(area => area.Width * area.Height);
            Assert.IsLessThan(
                WIDTH * HEIGHT * 0.05,
                repainted,
                $"two small tiles repainted {repainted} of {WIDTH * HEIGHT} layout units squared");
            AssertMatchesReference(factory, window, surface, "after changing two distant tiles");
        }
    }

    [TestMethod]
    public void ChangesOverMostOfTheSurface_AreDrawnAsAWholeFrame()
    {
        if (!TryCreate(out var factory, out var window, out var surface, out var tiles))
        {
            return;
        }

        using (factory)
        using (surface)
        {
            foreach (var tile in tiles)
            {
                tile.Fill = Color.FromArgb(255, 20, 160, 90);
                tile.InvalidateVisual();
            }

            Frame(window, surface);

            Assert.IsNull(window.LastRetainedDamage, "a frame that repaints nearly everything was still clipped into areas");
            AssertMatchesReference(factory, window, surface, "after changing every tile");
        }
    }

    private static bool TryCreate(out GdiGraphicsFactory factory, out Window window, out IRenderSurface surface, out Tile[] tiles)
    {
        factory = null!;
        window = null!;
        surface = null!;
        tiles = [];
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return false;
        }

        factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        // A grid of tiles that covers the surface edge to edge.
        const int COLUMNS = 10;
        const int ROWS = 10;
        var grid = new UniformGrid { Columns = COLUMNS, Rows = ROWS };
        tiles = new Tile[COLUMNS * ROWS];
        for (int index = 0; index < tiles.Length; index++)
        {
            tiles[index] = new Tile { Fill = Color.FromArgb(255, (byte)(60 + index), 120, (byte)(220 - index)) };
            grid.Children(tiles[index]);
        }

        window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = grid;
        window.PerformLayout();
        surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        for (int warmup = 0; warmup < 3; warmup++)
        {
            Frame(window, surface);
        }

        return true;
    }

    private static void Frame(Window window, IRenderSurface surface)
    {
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
    }

    private static void AssertMatchesReference(GdiGraphicsFactory factory, Window window, IRenderSurface actual, string label)
    {
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);

        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)reference).GetReadOnlyPixelSpan();
        ReadOnlySpan<byte> shown = ((ICpuPixelSurface)actual).GetReadOnlyPixelSpan();
        int differing = 0;
        int first = -1;
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            if (expected[offset] != shown[offset] ||
                expected[offset + 1] != shown[offset + 1] ||
                expected[offset + 2] != shown[offset + 2])
            {
                differing++;
                if (first < 0)
                {
                    first = offset / 4;
                }
            }
        }

        Assert.AreEqual(0, differing, $"{label}: {differing} pixels differ, first at ({first % WIDTH}, {first / WIDTH})");
    }
}
