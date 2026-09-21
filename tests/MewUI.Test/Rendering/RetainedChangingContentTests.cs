using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A recording pays off when it is replayed. A visual that draws something else every frame is never
/// replayed, so after it has changed for a run of frames the scene stops recording it and draws it
/// straight from the visual, and goes back to recording once it has settled.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedChangingContentTests
{
    private const int WIDTH = 240;
    private const int HEIGHT = 180;
    private const int FRAMES_UNTIL_SETTLED_IN = 60;

    [TestMethod]
    public void AVisualThatChangesEveryFrame_StopsBeingRecorded_AndIsRecordedAgainOnceItSettles()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var mover = new Mover();
        var still = new Border { Width = 60, Height = 30, Background = Color.FromArgb(255, 40, 120, 200), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom };
        var layers = new Grid();
        layers.Children(mover, still);
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = new Border { Background = Color.FromArgb(255, 250, 250, 250), Child = layers };
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));

        for (int frame = 0; frame < FRAMES_UNTIL_SETTLED_IN; frame++)
        {
            mover.Step();
            Frame(window, surface);
            AssertMatchesReference(factory, window, surface, $"changing frame {frame}");
        }

        window.RetainedStatistics!.Reset();
        int rendersBefore = mover.RenderCount;
        mover.Step();
        Frame(window, surface);
        Assert.AreEqual(0, window.RetainedStatistics.ContentRecordCount, "a visual that has changed every frame was recorded once more");
        Assert.AreEqual(1, mover.RenderCount - rendersBefore, "a visual drawn straight from itself is drawn once a frame");
        AssertMatchesReference(factory, window, surface, "a frame that draws the visual directly");

        // It stops changing. A frame goes by without it, and the one after takes a recording of it again.
        still.Background = Color.FromArgb(255, 200, 80, 40);
        Frame(window, surface);
        AssertMatchesReference(factory, window, surface, "the first frame it sat out");
        still.Background = Color.FromArgb(255, 200, 80, 140);
        Frame(window, surface);
        AssertMatchesReference(factory, window, surface, "the frame that records it again");

        window.RetainedStatistics.Reset();
        rendersBefore = mover.RenderCount;
        still.Background = Color.FromArgb(255, 80, 200, 40);
        Frame(window, surface);
        still.Background = Color.FromArgb(255, 80, 40, 200);
        Frame(window, surface);
        Assert.AreEqual(0, mover.RenderCount - rendersBefore, "a settled visual was drawn from itself again");
        Assert.AreEqual(0, window.RetainedStatistics.LiveFallbackCount, "a settled visual was still drawn live");
        AssertMatchesReference(factory, window, surface, "frames after it settled");
    }

    [TestMethod]
    public void AChangingVisualThatDrawsPastItsBounds_KeepsBeingRecorded()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var mover = new Mover { Overhang = 14, Width = 80, Height = 40, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = new Border { Background = Color.FromArgb(255, 250, 250, 250), Child = mover };
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));

        for (int frame = 0; frame < FRAMES_UNTIL_SETTLED_IN + 20; frame++)
        {
            mover.Step();
            Frame(window, surface);
            AssertMatchesReference(factory, window, surface, $"frame {frame}");
        }

        // Drawn live it would answer for its own box alone, so it keeps its recordings, which know what it inks.
        window.RetainedStatistics!.Reset();
        mover.Step();
        Frame(window, surface);
        Assert.AreEqual(1, window.RetainedStatistics.ContentRecordCount, "a visual that draws past its bounds stopped being recorded");
    }

    private static void Frame(Window window, IRenderSurface surface)
    {
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
    }

    private static void AssertMatchesReference(GdiGraphicsFactory factory, Window window, IRenderSurface surface, string what)
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

        Assert.AreEqual(0, differing, $"{what}: {differing} pixels differ from a frame drawn straight from the visuals");
    }

    private sealed class Mover : FrameworkElement
    {
        private int _step;

        internal int RenderCount { get; private set; }

        // How far past its own bounds the visual draws, as a glow or a shadow does.
        internal double Overhang { get; set; }

        internal void Step()
        {
            _step++;
            InvalidateVisual();
        }

        protected override void OnRender(IGraphicsContext context)
        {
            RenderCount++;
            var bounds = Bounds;
            double travel = Math.Max(1, bounds.Width - 20);
            double left = bounds.X + (_step * 3 % travel);
            context.FillRectangle(new Rect(left, bounds.Y + 4, 16, 10), Color.FromArgb(255, 220, 60, 60));
            if (Overhang > 0)
            {
                context.FillRectangle(new Rect(bounds.X - Overhang + (_step % 5), bounds.Y - Overhang, 10, 8), Color.FromArgb(255, 60, 160, 60));
                context.FillRectangle(new Rect(bounds.Right + Overhang - 12 - (_step % 7), bounds.Bottom + Overhang - 8, 12, 8), Color.FromArgb(255, 60, 60, 200));
            }
        }
    }
}
