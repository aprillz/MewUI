using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Validates the window frame on the retained path: a frame that repaints only the dirty box must
/// leave the surface exactly as a frame that repaints everything would, and a window whose target
/// cannot keep its contents must keep drawing whole frames.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedWindowFrameTests
{
    private const int SURFACE_WIDTH = 240;
    private const int SURFACE_HEIGHT = 180;

    private sealed class FillBox : Control
    {
        internal Color Fill { get; set; } = Color.FromArgb(255, 200, 60, 60);

        protected override Size MeasureContent(Size availableSize) => new(80, 40);

        protected override void OnRender(IGraphicsContext context) => context.FillRectangle(Bounds, Fill);
    }

    [TestMethod]
    public void PartiallyRepaintedFrame_MatchesAFullyRepaintedFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        {
            var top = new FillBox { Fill = Color.FromArgb(255, 30, 120, 200) };
            var bottom = new FillBox { Fill = Color.FromArgb(255, 200, 120, 30) };
            var window = BuildWindow(top, bottom);

            using var live = CreateSurface(factory);

            // The first frame has nothing to preserve, so it draws whole and asks the surface to keep
            // its contents for the next one.
            window.RenderFrameToSurface(live);
            window.RenderFrameToSurface(live);

            top.Fill = Color.FromArgb(255, 10, 200, 90);
            top.InvalidateVisual();
            window.RenderFrameToSurface(live);

            var dirtyRect = window.LastRetainedDirtyRect;
            Assert.IsNotNull(dirtyRect, "the frame was drawn whole, so this test would not exercise a partial repaint");
            Assert.IsFalse(
                dirtyRect.Value.Contains(new Point(bottom.Bounds.X + 4, bottom.Bounds.Y + 4)),
                $"the dirty region {dirtyRect} covers the item that did not change at {bottom.Bounds}");

            byte[] partialPixels = ReadPixels(live);

            var reference = BuildWindow(
                new FillBox { Fill = Color.FromArgb(255, 10, 200, 90) },
                new FillBox { Fill = Color.FromArgb(255, 200, 120, 30) });
            using var full = CreateSurface(factory);
            reference.RenderFrameToSurface(full);

            byte[] fullPixels = ReadPixels(full);
            AssertPixelsEqual(fullPixels, partialPixels);
        }
    }

    [TestMethod]
    public void FrameWithNothingChanged_RepaintsNothing()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var window = BuildWindow(
            new FillBox { Fill = Color.FromArgb(255, 30, 120, 200) },
            new FillBox { Fill = Color.FromArgb(255, 200, 120, 30) });
        using var live = CreateSurface(factory);

        // The first frame has nothing to preserve, and the second is the first one drawn onto a
        // surface that carried the previous frame.
        window.RenderFrameToSurface(live);
        window.RenderFrameToSurface(live);
        byte[] settled = ReadPixels(live);

        // A frame that repaints nothing is still a frame: what counts frames, such as a frame rate
        // readout, has to hear about it.
        int framesRendered = 0;
        window.FrameRendered += () => framesRendered++;
        window.RenderFrameToSurface(live);
        Assert.AreEqual(1, framesRendered, "a frame that repainted nothing was not reported as rendered");

        Assert.IsTrue(
            Window.RepaintsNothing(window.LastRetainedDirtyRect),
            $"a frame with nothing changed repainted {window.LastRetainedDirtyRect?.ToString() ?? "the whole surface"}");
        AssertPixelsEqual(settled, ReadPixels(live));
    }

    [TestMethod]
    public void FrameWithAChangedOpacity_RepaintsWhereThatVisualIs()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var top = new FillBox { Fill = Color.FromArgb(255, 30, 120, 200) };
        var bottom = new FillBox { Fill = Color.FromArgb(255, 200, 120, 30) };
        var window = BuildWindow(top, bottom);
        using var live = CreateSurface(factory);

        window.RenderFrameToSurface(live);
        window.RenderFrameToSurface(live);

        // Opacity is applied around the recorded content, so it asks for a repaint without claiming the
        // content changed. A frame that skipped it would freeze every fade.
        top.Opacity = 0.35;
        window.RenderFrameToSurface(live);

        var dirtyRect = window.LastRetainedDirtyRect;
        Assert.IsFalse(Window.RepaintsNothing(dirtyRect), "a changed opacity repainted nothing");
        if (dirtyRect is Rect repainted)
        {
            Assert.IsTrue(
                repainted.Contains(new Point(top.Bounds.X + 2, top.Bounds.Y + 2)),
                $"the dirty region {repainted} does not cover the visual at {top.Bounds}");
        }
    }

    [TestMethod]
    public void FrameWithAChangedOverlay_RepaintsWhereTheOverlayIs()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        // Small, so its change stays under the share of the surface past which a frame is drawn whole.
        var overlay = new FillBox
        {
            Fill = Color.FromArgb(255, 90, 90, 90),
            Width = 60,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var window = BuildWindow(
            new FillBox { Fill = Color.FromArgb(255, 30, 120, 200) },
            new FillBox { Fill = Color.FromArgb(255, 200, 120, 30) });
        window.OverlayLayer.Add(overlay);
        window.PerformLayout();

        using var live = CreateSurface(factory);
        window.RenderFrameToSurface(live);
        window.RenderFrameToSurface(live);

        overlay.Fill = Color.FromArgb(255, 200, 30, 30);
        overlay.InvalidateVisual();
        window.RenderFrameToSurface(live);

        var dirtyRect = window.LastRetainedDirtyRect;
        Assert.IsNotNull(dirtyRect, "a changed overlay repainted the whole surface");
        Assert.IsTrue(
            dirtyRect.Value.Width > 0 && dirtyRect.Value.Contains(new Point(overlay.Bounds.X + 1, overlay.Bounds.Y + 1)),
            $"the dirty region {dirtyRect} does not cover the overlay at {overlay.Bounds}");
    }

    [TestMethod]
    public void TargetThatKeepsItsContents_SkipsTheFrameSurfaceIndirection()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        {
            var window = BuildWindow(
                new FillBox { Fill = Color.FromArgb(255, 30, 120, 200) },
                new FillBox { Fill = Color.FromArgb(255, 200, 120, 30) });
            using var surface = CreateSurface(factory);

            Assert.IsInstanceOfType<IPersistentFrameSurface>(surface);
            Assert.IsFalse(
                window.TryRenderFrameThroughRetainedSurface(surface, new Size(SURFACE_WIDTH, SURFACE_HEIGHT)),
                "a target that already keeps its contents was copied through a second surface");
        }
    }

    private static Window BuildWindow(UIElement top, UIElement bottom)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(top, bottom);

        var window = HeadlessWindow.Create(SURFACE_WIDTH, SURFACE_HEIGHT);
        window.Content = new Border
        {
            Padding = new Thickness(8),
            Background = Color.FromArgb(255, 245, 245, 245),
            Child = stack,
        };

        window.PerformLayout();
        return window;
    }

    private static IRenderSurface CreateSurface(GdiGraphicsFactory factory)
        => factory.CreateSurface(
            RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));

    private static byte[] ReadPixels(IRenderSurface surface)
    {
        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        int stride = cpu.StrideBytes;
        var copy = new byte[SURFACE_WIDTH * SURFACE_HEIGHT * 4];
        for (int row = 0; row < SURFACE_HEIGHT; row++)
        {
            pixels.Slice(row * stride, SURFACE_WIDTH * 4).CopyTo(copy.AsSpan(row * SURFACE_WIDTH * 4));
        }

        return copy;
    }

    private static void AssertPixelsEqual(byte[] expected, byte[] actual)
    {
        for (int offset = 0; offset < expected.Length; offset += 4)
        {
            if (expected[offset] == actual[offset] &&
                expected[offset + 1] == actual[offset + 1] &&
                expected[offset + 2] == actual[offset + 2] &&
                expected[offset + 3] == actual[offset + 3])
            {
                continue;
            }

            int pixelIndex = offset / 4;
            Assert.Fail(
                $"The frames differ at ({pixelIndex % SURFACE_WIDTH},{pixelIndex / SURFACE_WIDTH}): " +
                $"expected BGRA=({expected[offset]},{expected[offset + 1]},{expected[offset + 2]},{expected[offset + 3]}) " +
                $"actual BGRA=({actual[offset]},{actual[offset + 1]},{actual[offset + 2]},{actual[offset + 3]}).");
        }
    }
}
