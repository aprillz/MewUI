using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Filters;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A backdrop on every backend of every platform: after a change under it, the retained frame, which reads
/// only what changed, comes out as the frame drawn straight from the visuals, on targets with and without
/// alpha and inside a faded group.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedBackdropWindowTests
{
    private const int TOLERANCE = 2;
    private const int COLUMNS = 8;
    private const int ROWS = 6;

    [TestMethod]
    public Task AChangeUnderTheGlass_MatchesTheReferenceFrame() => RunAsync(hasAlpha: false, glassParentOpacity: 1, expectsFilter: true);

    [TestMethod]
    public Task AChangeUnderTheGlass_MatchesTheReferenceFrameOnAnAlphaTarget() => RunAsync(hasAlpha: true, glassParentOpacity: 1, expectsFilter: true);

    [TestMethod]
    public Task AChangeUnderFadedGlass_MatchesTheReferenceFrame() => RunAsync(hasAlpha: false, glassParentOpacity: 0.7, expectsFilter: false);

    [TestMethod]
    public Task AChangeUnderABlurInsideAGraph_MatchesTheReferenceFrame()
        => RunAsync(hasAlpha: false, glassParentOpacity: 1, expectsFilter: true, new OffsetFilter(6, 4, new BlurFilter(8, 8)));

    private static Task RunAsync(bool hasAlpha, double glassParentOpacity, bool expectsFilter, ImageFilter? filter = null) => CaptureScene.RunAsync(async scene =>
    {
        var tiles = new Border[COLUMNS * ROWS];
        var grid = new UniformGrid { Columns = COLUMNS, Rows = ROWS };
        for (int index = 0; index < tiles.Length; index++)
        {
            byte shade = (byte)((index + index / COLUMNS) % 2 == 0 ? 40 : 220);
            tiles[index] = new Border { Background = Color.FromRgb(shade, (byte)(40 + index * 4), (byte)(255 - shade)) };
            grid.Children(tiles[index]);
        }

        var glass = new Border
        {
            Margin = new Thickness(60, 40, 60, 50),
            CornerRadius = 12,
            Background = Color.FromArgb(70, 255, 255, 255),
            BackdropFilter = filter ?? new BlurFilter(8, 8),
        };
        var glassHost = new Grid { Opacity = glassParentOpacity };
        glassHost.Children(glass);
        var root = new Grid { Width = 320, Height = 240 };
        root.Children(grid);
        root.Children(glassHost);

        var window = await scene.ShowAsync(root);
        await Task.Delay(400);
        string label = $"{window.GraphicsFactory.Backend}, {(hasAlpha ? "alpha" : "opaque")} target, glass opacity {glassParentOpacity}";

        var factory = window.GraphicsFactory;
        double scale = window.GetDpi() / 96.0;
        int width = (int)Math.Round(window.ClientSize.Width * scale);
        int height = (int)Math.Round(window.ClientSize.Height * scale);
        using var renderScope = factory.AcquireBackgroundRenderScope();
        using var retained = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(width, height, scale, hasAlpha));

        // The first frame into a new surface is whole; the ones after it go through the kept frame.
        window.RenderFrameToSurface(retained);
        window.RenderFrameToSurface(retained);

        // Under the glass, away from its edges.
        var changed = tiles[3 * COLUMNS + 3];
        changed.Background = Color.FromRgb(250, 230, 20);
        window.PerformLayout();
        window.RetainedStatistics!.Reset();
        window.RenderFrameToSurface(retained);
        var statistics = window.RetainedStatistics!;
        Assert.IsNotNull(window.LastRetainedDirtyRect, $"{label}: the frame was drawn whole");
        if (expectsFilter)
        {
            Assert.AreEqual(0, statistics.BackdropFallbackCount, $"{label}: the backdrop fell back");
            Assert.IsGreaterThanOrEqualTo(1, statistics.BackdropPassCount, $"{label}: backdrops drawn");
        }

        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(width, height, scale, hasAlpha));
        window.RenderReferenceFrameToSurface(reference);
        AssertSame(factory, reference, retained, width, height, label);
    });

    private static void AssertSame(IGraphicsFactory factory, IRenderSurface reference, IRenderSurface actual, int width, int height, string label)
    {
        var expected = new byte[width * height * 4];
        var shown = new byte[width * height * 4];
        if (!((IRenderDevice)factory).TryReadPixels(reference, expected, width * 4) ||
            !((IRenderDevice)factory).TryReadPixels(actual, shown, width * 4))
        {
            Assert.Inconclusive($"{label}: the surfaces cannot be read back.");
        }

        int differing = 0;
        int first = -1;
        int worst = 0;
        for (int offset = 0; offset < expected.Length; offset++)
        {
            int difference = Math.Abs(expected[offset] - shown[offset]);
            if (difference > TOLERANCE)
            {
                differing++;
                if (first < 0)
                {
                    first = offset / 4;
                }
            }

            worst = Math.Max(worst, difference);
        }

        Assert.AreEqual(0, differing, $"{label}: {differing} channels differ by more than {TOLERANCE} (worst {worst}), first at ({first % width}, {first / width})");
    }
}
