using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Falling back to painting the whole frame widens what is replayed, never what is drawn again: the
/// visuals whose drawing did not change are not asked to render, however much of the surface the frame
/// repaints.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedFallbackCostTests
{
    private const int WIDTH = 300;
    private const int HEIGHT = 200;

    private sealed class Counted : Control
    {
        internal int Renders;

        internal Color Fill { get; set; }

        protected override void OnRender(IGraphicsContext context)
        {
            Renders++;
            context.FillRectangle(Bounds, Fill);
        }
    }

    [TestMethod]
    public void WholeFrameFallback_RendersOnlyWhatChanged()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        // One visual covers the window, so its change is past the share at which the frame is painted whole.
        var backdrop = new Counted { Fill = Color.FromArgb(255, 240, 240, 240) };
        var small = new Counted[6];
        var grid = new Grid();
        grid.Children(backdrop);
        for (int index = 0; index < small.Length; index++)
        {
            small[index] = new Counted
            {
                Fill = Color.FromArgb(255, (byte)(40 * index), 120, 200),
                Width = 30,
                Height = 20,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(10 + index * 40, 10, 0, 0),
            };
            grid.Children(small[index]);
        }

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = grid;
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        for (int warm = 0; warm < 3; warm++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        int before = small.Sum(visual => visual.Renders);
        backdrop.Fill = Color.FromArgb(255, 220, 230, 240);
        backdrop.InvalidateVisual();
        window.PerformLayout();
        window.RenderFrameToSurface(surface);

        Assert.IsNull(window.LastRetainedDirtyRect, "the frame was expected to be painted whole");
        Assert.AreEqual(before, small.Sum(visual => visual.Renders), "painting the frame whole asked unchanged visuals to render again");

        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        int beforeReference = small.Sum(visual => visual.Renders);
        window.RenderReferenceFrameToSurface(reference);
        Assert.IsGreaterThan(beforeReference, small.Sum(visual => visual.Renders), "the reference frame is expected to render every visual");
        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)reference).GetReadOnlyPixelSpan();
        ReadOnlySpan<byte> shown = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int differing = 0;
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            if (expected[offset] != shown[offset] || expected[offset + 1] != shown[offset + 1] || expected[offset + 2] != shown[offset + 2])
            {
                differing++;
            }
        }

        Assert.AreEqual(0, differing, $"{differing} pixels differ from a frame drawn straight from the visuals");
    }
}
