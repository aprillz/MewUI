using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A visual that no part of reaches the surface (scrolled out of its viewport, say) must not wake the
/// window when it changes: an animation running out of sight would otherwise drive frames that paint
/// nothing. The change is not lost. It is drawn when the visual comes back into view.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedCulledVisualTests
{
    private const int WIDTH = 300;
    private const int HEIGHT = 200;

    private sealed class Blinker : Control
    {
        internal Color Fill { get; set; } = Color.FromArgb(255, 40, 160, 120);

        protected override Size MeasureContent(Size availableSize) => new(80, 20);

        protected override void OnRender(IGraphicsContext context) => context.FillRectangle(Bounds, Fill);
    }

    [TestMethod]
    public void ChangeOfAVisualScrolledOutOfView_DoesNotWakeTheWindow_AndShowsWhenScrolledBack()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var blinker = new Blinker { Height = 20 };
        var rows = new StackPanel { Orientation = Orientation.Vertical };
        rows.Children(blinker);
        for (int index = 0; index < 40; index++)
        {
            rows.Children(new TextBlock { Text = $"row {index}" });
        }

        var scroll = new ScrollViewer { VerticalScroll = ScrollMode.Visible, Content = rows };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = scroll;
        window.PerformLayout();

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        Frames(window, surface, 2);

        // In view: a change has to wake the window.
        blinker.Fill = Color.FromArgb(255, 200, 60, 60);
        blinker.InvalidateVisual();
        Assert.IsTrue(window.RenderDirtyQueue.Queued.ContainsKey(blinker), "a change of a visual in view was not queued");
        Frames(window, surface, 1);

        scroll.SetScrollOffsets(0, 300);
        Frames(window, surface, 2);

        blinker.Fill = Color.FromArgb(255, 60, 60, 220);
        blinker.InvalidateVisual();
        Assert.IsFalse(
            window.RenderDirtyQueue.Queued.ContainsKey(blinker),
            "a change of a visual scrolled out of view was queued, which wakes the window for a frame that paints nothing");

        scroll.SetScrollOffsets(0, 0);
        Frames(window, surface, 2);

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

        Assert.AreEqual(0, differing, $"the change made out of view was not drawn after scrolling back: {differing} pixels differ");
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
