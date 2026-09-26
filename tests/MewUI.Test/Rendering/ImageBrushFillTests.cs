using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A fill with an image brush whose tile is a view of an offscreen surface, as an SVG pattern paints, comes out in the
/// tile's colour on every backend, drawn straight or replayed from the retained scene.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ImageBrushFillTests
{
    private const int WIDTH = 120;
    private const int HEIGHT = 80;
    private const int TILE = 8;
    private static readonly Color _tileColor = Color.FromRgb(200, 40, 60);
    private static readonly Color _background = Color.FromRgb(250, 250, 250);

    [TestMethod]
    [DataRow(TestBackend.Gdi)]
    [DataRow(TestBackend.Direct2D)]
    [DataRow(TestBackend.MewVG)]
    public void ARectFilledWithASurfaceTileShowsTheTile(TestBackend backend)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The backends under test are Windows-only.");
            return;
        }

        using var session = TestBackendSession.Open(backend);
        var fill = new TileFill();
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = new Border { Background = _background, Child = fill };
        window.PerformLayout();

        using var retained = session.Factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderFrameToSurface(retained);
        window.RenderFrameToSurface(retained);
        using var direct = session.Factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(direct);

        Assert.AreEqual(_tileColor, CenterOf(direct), "drawn straight, the fill did not show the tile");
        Assert.AreEqual(_tileColor, CenterOf(retained), "replayed from the retained scene, the fill did not show the tile");
        fill.Release();
    }

    private static Color CenterOf(IRenderSurface surface)
    {
        ReadOnlySpan<byte> pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int offset = ((HEIGHT / 2) * WIDTH + WIDTH / 2) * 4;
        return Color.FromRgb(pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    private sealed class TileFill : FrameworkElement
    {
        private IRenderSurface? _tileSurface;
        private IImage? _tile;

        public void Release()
        {
            _tile?.Dispose();
            _tileSurface?.Dispose();
        }

        protected override void OnRender(IGraphicsContext context)
        {
            var factory = GetGraphicsFactory();
            if (_tile is null)
            {
                // Drawn through a context of its own, as an SVG pattern renders its tile.
                _tileSurface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(TILE, TILE, dpiScale: 1.0, debugName: "TestTile"));
                using (var tileContext = factory.CreateContext(_tileSurface))
                {
                    tileContext.BeginFrame(_tileSurface);
                    tileContext.FillRectangle(new Rect(0, 0, TILE, TILE), _tileColor);
                    tileContext.EndFrame();
                }

                _tile = factory.CreateImageView(_tileSurface);
            }

            context.FillRectangle(Bounds, new ImageBrush(_tile, new Rect(0, 0, TILE, TILE), new Rect(0, 0, TILE, TILE)));
        }
    }
}
