using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Filters;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A backdrop in the retained scene: a change under it repaints only as far as its blur reaches, a change
/// beside its box leaves it alone, one read whole is drawn again whole, and every frame comes out as the
/// frame drawn straight from the visuals. Runs on the backend that keeps its frame in memory.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedBackdropTests
{
    private const int WIDTH = 400;
    private const int HEIGHT = 300;
    private const int COLUMNS = 10;
    private const int ROWS = 10;
    private const double BLUR_RADIUS = 6;

    // The glass covers columns 2 to 7 and rows 2 to 6 of the tiles, partly.
    private static readonly Thickness _glassMargin = new(100, 60, 100, 90);

    // Inside the glass, away from its edges, and beside it.
    private const int TILE_UNDER_GLASS = 4 * COLUMNS + 4;
    private const int TILE_BESIDE_GLASS = 0;

    private sealed class Tile : Control
    {
        internal Color Fill { get; set; }

        protected override Size MeasureContent(Size availableSize) => new(40, 30);

        protected override void OnRender(IGraphicsContext context) => context.FillRectangle(Bounds, Fill);
    }

    /// <summary>A filter no executor knows.</summary>
    private sealed class UnknownFilter : ImageFilter
    {
        public override IReadOnlyList<ImageFilter?> Inputs => [null];
    }

    [TestMethod]
    public void AChangeUnderAKeptBackdrop_RepaintsOnlyWhatTheBlurReaches()
    {
        if (!TryCreate(out var scene))
        {
            return;
        }

        using (scene)
        {
            scene.Change(TILE_UNDER_GLASS);
            scene.Window.RetainedStatistics!.Reset();
            scene.Frame();

            Assert.IsNotNull(scene.Window.LastRetainedDirtyRect, "the frame was drawn whole");
            double repainted = scene.Window.LastRetainedDirtyRects.Sum(area => area.Width * area.Height);
            Assert.IsLessThan(scene.Glass.Bounds.Width * scene.Glass.Bounds.Height, repainted, "the whole glass was repainted");
            Assert.IsGreaterThan(40.0 * 30.0, repainted, "the repaint stopped at the tile, short of what the blur reaches");
            Assert.AreEqual(1, scene.Window.RetainedStatistics!.BackdropPassCount, "backdrops drawn");
            Assert.IsLessThan((long)(scene.Glass.Bounds.Width * scene.Glass.Bounds.Height), scene.Window.RetainedStatistics!.BackdropInputPixelCount,
                "the whole box was read again");
            scene.AssertMatchesReference("after a change under the glass");
        }
    }

    [TestMethod]
    public void AChangeBesideTheBox_LeavesTheBackdropAlone()
    {
        if (!TryCreate(out var scene))
        {
            return;
        }

        using (scene)
        {
            scene.Change(TILE_BESIDE_GLASS);
            scene.Window.RetainedStatistics!.Reset();
            scene.Frame();

            Assert.IsNotNull(scene.Window.LastRetainedDirtyRect, "the frame was drawn whole");
            Assert.AreEqual(0, scene.Window.RetainedStatistics!.BackdropPassCount, "a backdrop was drawn again");
            foreach (var area in scene.Window.LastRetainedDirtyRects)
            {
                Assert.IsFalse(area.IntersectsWith(scene.Glass.Bounds), $"the repaint {area} reached the glass");
            }

            scene.AssertMatchesReference("after a change beside the glass");
        }
    }

    [TestMethod]
    public void ABackdropInsideAFadedGroup_IsDrawnAgainWhole()
    {
        if (!TryCreate(out var scene, glassParentOpacity: 0.8))
        {
            return;
        }

        using (scene)
        {
            scene.Change(TILE_UNDER_GLASS);
            scene.Frame();

            Assert.IsNotNull(scene.Window.LastRetainedDirtyRect, "the frame was drawn whole");
            Assert.IsTrue(scene.Window.LastRetainedDirtyRects.Any(area => area.Contains(scene.Glass.Bounds)),
                $"a backdrop that reads its box whole was not repainted whole: {string.Join(", ", scene.Window.LastRetainedDirtyRects)}");
            scene.AssertMatchesReference("after a change under faded glass");
        }
    }

    [TestMethod]
    public void MovingTheBox_DrawsTheBackdropWhereItStandsNow()
    {
        if (!TryCreate(out var scene))
        {
            return;
        }

        using (scene)
        {
            scene.Glass.Margin = new Thickness(_glassMargin.Left + 13, _glassMargin.Top + 7, _glassMargin.Right - 13, _glassMargin.Bottom - 7);
            scene.Frame();
            scene.AssertMatchesReference("after the glass moved");

            scene.Change(TILE_UNDER_GLASS);
            scene.Frame();
            scene.AssertMatchesReference("after a change under the moved glass");
        }
    }

    [TestMethod]
    public void AFilterTheExecutorCannotRun_FillsTheFallbackBrush()
    {
        var fallback = Color.FromArgb(255, 200, 20, 120);
        if (!TryCreate(out var scene, filter: new UnknownFilter(), fallback: new SolidColorBrush(fallback)))
        {
            return;
        }

        using (scene)
        {
            scene.Window.RetainedStatistics!.Reset();
            scene.Change(TILE_UNDER_GLASS);
            scene.Frame();

            Assert.IsGreaterThanOrEqualTo(1, scene.Window.RetainedStatistics!.BackdropFallbackCount, "backdrops that fell back");
            // The glass's own translucent white lies over the fallback.
            var middle = scene.PixelAt((int)(scene.Glass.Bounds.X + scene.Glass.Bounds.Width / 2), (int)(scene.Glass.Bounds.Y + scene.Glass.Bounds.Height / 2));
            Assert.AreEqual(Over(fallback, GLASS_WHITE_ALPHA), middle, "the middle of the glass");
            scene.AssertMatchesReference("with a fallback brush");
        }
    }

    private const byte GLASS_WHITE_ALPHA = 60;

    /// <summary>White at <paramref name="alpha"/> over an opaque colour, rounded as the backend rounds.</summary>
    private static Color Over(Color under, byte alpha)
    {
        byte Blend(byte channel) => (byte)Math.Round(channel + (255 - channel) * alpha / 255.0);
        return Color.FromArgb(255, Blend(under.R), Blend(under.G), Blend(under.B));
    }

    private static bool TryCreate(
        out GlassScene scene,
        double glassParentOpacity = 1,
        ImageFilter? filter = null,
        Brush? fallback = null)
    {
        scene = null!;
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return false;
        }

        scene = new GlassScene(glassParentOpacity, filter ?? new BlurFilter(BLUR_RADIUS, BLUR_RADIUS), fallback);
        return true;
    }

    private sealed class GlassScene : IDisposable
    {
        private readonly GdiGraphicsFactory _factory;
        private readonly IRenderSurface _surface;
        private readonly Tile[] _tiles;

        internal GlassScene(double glassParentOpacity, ImageFilter filter, Brush? fallback)
        {
            _factory = new GdiGraphicsFactory();
            Application.DefaultGraphicsFactory = _factory;

            var grid = new UniformGrid { Columns = COLUMNS, Rows = ROWS };
            _tiles = new Tile[COLUMNS * ROWS];
            for (int index = 0; index < _tiles.Length; index++)
            {
                // Neighbouring tiles differ, so a blur across their edges shows.
                byte shade = (byte)((index + index / COLUMNS) % 2 == 0 ? 40 : 220);
                _tiles[index] = new Tile { Fill = Color.FromArgb(255, shade, (byte)(60 + index), (byte)(255 - shade)) };
                grid.Children(_tiles[index]);
            }

            Glass = new Border
            {
                Margin = _glassMargin,
                Background = Color.FromArgb(GLASS_WHITE_ALPHA, 255, 255, 255),
                BackdropFilter = filter,
                BackdropFallbackBrush = fallback,
            };

            var glassHost = new Grid { Opacity = glassParentOpacity };
            glassHost.Children(Glass);
            var root = new Grid();
            root.Children(grid);
            root.Children(glassHost);

            Window = HeadlessWindow.Create(WIDTH, HEIGHT);
            Window.Content = root;
            Window.PerformLayout();
            _surface = _factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
            for (int warmup = 0; warmup < 3; warmup++)
            {
                Frame();
            }
        }

        internal Window Window { get; }

        internal Border Glass { get; }

        internal void Change(int tileIndex)
        {
            var tile = _tiles[tileIndex];
            tile.Fill = Color.FromArgb(255, (byte)(255 - tile.Fill.R), tile.Fill.B, tile.Fill.G);
            tile.InvalidateVisual();
        }

        internal void Frame()
        {
            Window.PerformLayout();
            Window.RenderFrameToSurface(_surface);
        }

        internal Color PixelAt(int x, int y)
        {
            ReadOnlySpan<byte> pixels = ((ICpuPixelSurface)_surface).GetReadOnlyPixelSpan();
            int offset = (y * WIDTH + x) * 4;
            return Color.FromArgb(255, pixels[offset + 2], pixels[offset + 1], pixels[offset]);
        }

        internal void AssertMatchesReference(string label)
        {
            using var reference = _factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
            Window.RenderReferenceFrameToSurface(reference);

            ReadOnlySpan<byte> expected = ((ICpuPixelSurface)reference).GetReadOnlyPixelSpan();
            ReadOnlySpan<byte> shown = ((ICpuPixelSurface)_surface).GetReadOnlyPixelSpan();
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

        public void Dispose()
        {
            _surface.Dispose();
            _factory.Dispose();
        }
    }
}
