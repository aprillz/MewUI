using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A control that draws all of its rows itself has one drawing for the lot, as a menu does. When the
/// highlight moves from one row to another, most of that drawing comes out as it was, and what is
/// repainted is where the drawing calls differ, not everything the control draws.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedChangedCallsTests
{
    private const int WIDTH = 320;
    private const int HEIGHT = 400;
    private const int ROW_HEIGHT = 30;
    private const int ROW_COUNT = 10;

    [TestMethod]
    public void MovingTheHighlight_RepaintsTheRowsBetween()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var rows = new SelfDrawnRows { HotRow = 2 };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = rows;
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        for (int warm = 0; warm < 3; warm++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        rows.HotRow = 3;
        window.PerformLayout();
        window.RenderFrameToSurface(surface);

        var damage = window.LastRetainedDamage;
        Assert.IsTrue(
            damage is Rect area && area.Height <= ROW_HEIGHT * 3,
            $"the highlight moved one row and the frame repainted {damage?.ToString() ?? "everything"} ({window.LastWholeFrameReason})");

        rows.HotRow = -1;
        window.PerformLayout();
        window.RenderFrameToSurface(surface);

        damage = window.LastRetainedDamage;
        Assert.IsTrue(
            damage is Rect cleared && cleared.Height <= ROW_HEIGHT * 2,
            $"the highlight went away and the frame repainted {damage?.ToString() ?? "everything"} ({window.LastWholeFrameReason})");

        AssertMatchesReference(factory, window, surface);
    }

    [TestMethod]
    public void AChangedClip_RepaintsEverythingTheDrawingReaches()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var rows = new SelfDrawnRows { HotRow = 2, ClipHeight = ROW_HEIGHT * 8 };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = rows;
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        for (int warm = 0; warm < 3; warm++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        // The calls after a clip draw under it, so none of them can be taken as unchanged.
        rows.ClipHeight = ROW_HEIGHT * 4;
        window.PerformLayout();
        window.RenderFrameToSurface(surface);

        AssertMatchesReference(factory, window, surface);
    }

    private static void AssertMatchesReference(GdiGraphicsFactory factory, Window window, IRenderSurface surface)
    {
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

        Assert.AreEqual(0, differing, $"{differing} pixels differ from a frame drawn straight from the visuals");
    }

    private sealed class SelfDrawnRows : Control
    {
        private int _hotRow = -1;
        private double _clipHeight = ROW_HEIGHT * ROW_COUNT;

        public int HotRow
        {
            get => _hotRow;
            set
            {
                _hotRow = value;
                InvalidateVisual();
            }
        }

        public double ClipHeight
        {
            get => _clipHeight;
            set
            {
                _clipHeight = value;
                InvalidateVisual();
            }
        }

        protected override Size MeasureContent(Size availableSize) => new(200, ROW_HEIGHT * ROW_COUNT);

        protected override void OnRender(IGraphicsContext context)
        {
            var bounds = Bounds;
            context.FillRectangle(bounds, Color.FromArgb(255, 245, 245, 245));
            context.Save();
            context.SetClip(new Rect(bounds.X, bounds.Y, bounds.Width, _clipHeight));
            for (int row = 0; row < ROW_COUNT; row++)
            {
                var rowBounds = new Rect(bounds.X, bounds.Y + (row * ROW_HEIGHT), bounds.Width, ROW_HEIGHT);
                if (row == _hotRow)
                {
                    context.FillRectangle(rowBounds, Color.FromArgb(255, 120, 170, 240));
                }

                context.FillRectangle(new Rect(rowBounds.X + 8, rowBounds.Y + 10, 60 + (row * 8), 10), Color.FromArgb(255, 40, 40, 40));
            }

            context.Restore();
        }
    }
}
