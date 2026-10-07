using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A recording wider than the clip around it on both sides reaches the same part of the surface before
/// and after a move along that axis, so the clipped extent alone cannot tell that it moved. A row longer
/// than a list's viewport is that case: scrolled sideways, it has to be repainted like the short rows.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedOverhangingMoveTests
{
    private const int SURFACE_WIDTH = 200;
    private const int SURFACE_HEIGHT = 160;
    private const int LONG_ITEM_INDEX = 7;
    private const double HORIZONTAL_STEP = 40;

    [TestMethod]
    public void ARowWiderThanTheViewport_IsRepaintedOnEveryHorizontalScroll()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var items = Enumerable.Range(0, 15).Select(index => "Item " + index).ToList();
        items[LONG_ITEM_INDEX] = "Abcdefghij klmnopqrst uvwxyz 0123456789 ABCDEFGHIJ KLMNOPQRST UVWXYZ the end";

        var window = HeadlessWindow.Create(SURFACE_WIDTH, SURFACE_HEIGHT);
        var list = new ListBox { ItemHeight = 24 };
        list.ItemsSource = ItemsView.Create(items);
        window.Content = list;
        window.PerformLayout();

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));
        Frames(window, surface, 2);

        var scroll = (ScrollViewer?)VisualTree.Find(list, static element => element is ScrollViewer);
        Assert.IsNotNull(scroll, "the list has no scroll viewer");

        // From the second step on, the long row overhangs the viewport on both sides.
        for (int step = 1; step <= 4; step++)
        {
            scroll.SetScrollOffsets(step * HORIZONTAL_STEP, 60);
            Frames(window, surface, 2);
            Assert.AreEqual(step * HORIZONTAL_STEP, scroll.HorizontalOffset, "precondition: the list scrolled sideways");

            window.RenderReferenceFrameToSurface(reference);
            int differing = Differing(surface, reference);
            Assert.AreEqual(0, differing, $"step {step}: {differing} pixels differ from a frame drawn straight from the visuals");
        }
    }

    private static int Differing(IRenderSurface surface, IRenderSurface reference)
    {
        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)reference).GetReadOnlyPixelSpan();
        ReadOnlySpan<byte> actual = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int differing = 0;
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            if (expected[offset] != actual[offset] || expected[offset + 1] != actual[offset + 1] || expected[offset + 2] != actual[offset + 2])
            {
                differing++;
            }
        }

        return differing;
    }

    private static void Frames(Window window, IRenderSurface surface, int count)
    {
        for (int index = 0; index < count; index++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }
    }
}
