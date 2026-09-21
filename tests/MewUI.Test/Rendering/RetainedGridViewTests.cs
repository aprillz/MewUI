using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A grid view draws rows that read state of the grid itself (grid lines, column widths) and cells
/// built from templates. Whatever changes, the surface has to show what a frame drawn straight from
/// the visuals shows, and a change of one row must not run the drawing of the others again.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedGridViewTests
{
    private const int WIDTH = 420;
    private const int HEIGHT = 260;

    private sealed record Row(string Name, int Role);

    [TestMethod]
    public void GridViewChanges_MatchTheReference_AndStayLocal()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var rows = Enumerable.Range(0, 30).Select(index => new Row($"row {index}", index % 3)).ToArray();
        var grid = new GridView()
            .ItemsSource(rows)
            .Columns(
                new GridViewColumn<Row>().Header("Name").Width(120).Text(row => row.Name),
                new GridViewColumn<Row>().Header("Role").Width(120).Template(
                    build: _ => new ComboBox().Items(new[] { "User", "Admin", "Guest" }).CenterVertical(),
                    bind: (view, row) => view.SelectedIndex = row.Role),
                new GridViewColumn<Row>().Header("Amount").Width(120).Template(
                    build: _ => new NumericUpDown().CenterVertical(),
                    bind: (view, row) => view.Value = row.Role));

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = grid;
        window.PerformLayout();

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        void Check(string label)
        {
            for (int index = 0; index < 2; index++)
            {
                window.PerformLayout();
                window.RenderFrameToSurface(surface);
            }

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

            Assert.AreEqual(0, differing, $"{label}: {differing} pixels differ from a frame drawn straight from the visuals");
        }

        Check("first frames");

        window.SendMouseMove(new Point(60, 70));
        Check("pointer over a row");
        window.SendMouseMove(new Point(60, 130));
        Check("pointer over another row");

        grid.SelectedIndex = 2;
        Check("after a row was selected");

        grid.ShowGridLines = true;
        Check("after grid lines were shown");

        window.SendMouseWheel(new Point(60, 130), -120);
        Check("after scrolling by the wheel");

        grid.ShowGridLines = false;
        grid.SelectedIndex = 5;
        Check("after grid lines were hidden and the selection moved");

        window.SendMouseMove(new Point(WIDTH - 4, HEIGHT - 4));
        Check("pointer moved away");

        // One row's hover must not run the drawing of the rest again.
        window.RetainedStatistics!.Reset();
        window.SendMouseMove(new Point(60, 100));
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
        Assert.IsLessThanOrEqualTo(
            6,
            window.RetainedStatistics!.ContentRecordCount,
            $"a hover over one row recorded {window.RetainedStatistics!.ContentRecordCount} drawings again");
        var dirtyRect = window.LastRetainedDirtyRect;
        Assert.IsTrue(
            dirtyRect is Rect hovered && hovered.Height > 0 && hovered.Height < 100,
            $"a hover over one row repainted {dirtyRect} of a {grid.Bounds} grid ({window.LastWholeFrameReason})");
    }
}
