using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A virtualized grid of cells, the shape of an icon browser. Scrolling it by a row keeps most cells on
/// screen, and those have to be moved, not drawn again: drawing a cell can be as expensive as rendering a
/// vector image. The frame still has to equal one drawn straight from the visuals.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedWrapPresenterTests
{
    private const int WIDTH = 330;
    private const int HEIGHT = 240;
    private const double CELL = 60;

    private static int _cellRecords;

    private sealed class Cell : Control
    {
        internal int Index { get; set; }

        protected override Size MeasureContent(Size availableSize) => new(40, 40);

        protected override void OnRender(IGraphicsContext context)
        {
            _cellRecords++;
            context.FillEllipse(Bounds, Color.FromArgb(255, (byte)(30 + Index * 3 % 200), (byte)(200 - Index * 5 % 180), 140));
            context.DrawEllipse(Bounds, Color.FromArgb(255, 20, 20, 20), 2);
        }
    }

    [TestMethod]
    public void ScrollingByARow_MovesTheCellsThatStayWithoutDrawingThemAgain()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var items = Enumerable.Range(0, 400).ToList();
        var grid = new ItemsControl()
            .ItemsSource(ItemsView.Create(items, index => index.ToString()))
            .ItemTemplate(new DelegateTemplate<int>(
                build: _ => new Cell(),
                bind: (view, index, _, _) => ((Cell)view).Index = index))
            .WrapPresenter(CELL, CELL);

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = grid;
        for (int pass = 0; pass < 3; pass++)
        {
            window.PerformLayout();
        }

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderFrameToSurface(surface);
        window.RenderFrameToSurface(surface);

        var scroll = FindScrollViewer(grid);
        Assert.IsNotNull(scroll, "the grid has no scroll viewer");

        _cellRecords = 0;
        scroll.SetScrollOffsets(0, CELL);
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
        int recordsForTheScroll = _cellRecords;

        // One row leaves and one row arrives; every other cell was already recorded.
        int columns = (int)(WIDTH / CELL);
        Assert.IsTrue(
            recordsForTheScroll <= columns * 2,
            $"scrolling by one row drew {recordsForTheScroll} cells again; only the {columns} that arrived should be new");

        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
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

        Assert.AreEqual(0, differing, $"{differing} pixels differ from the reference frame after the scroll");
    }

    private static ScrollViewer? FindScrollViewer(Element root)
    {
        ScrollViewer? found = null;
        VisualTree.Visit(root, element =>
        {
            if (found == null && element is ScrollViewer viewer)
            {
                found = viewer;
            }
        });
        return found;
    }
}
