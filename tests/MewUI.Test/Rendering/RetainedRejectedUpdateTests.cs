using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.Retained;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A scene update that is rejected leaves the scene as it was, and the frame shows that scene. That
/// is right for an update that fails once. A visual whose composition is rejected every time would
/// freeze the window on the scene from before it, so once an update is rejected again the frame is
/// drawn straight from the visuals until an update lands.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedRejectedUpdateTests
{
    private const int WIDTH = 240;
    private const int HEIGHT = 160;

    [TestMethod]
    public void AnUpdateRejectedEveryFrame_DoesNotFreezeTheWindow()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var faulty = new DeclaresItsSlotTwice { Width = 120, Height = 60, Fill = Color.FromArgb(255, 40, 90, 200) };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = faulty;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        for (int warm = 0; warm < 3; warm++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        faulty.IsFaulty = true;
        faulty.Fill = Color.FromArgb(255, 210, 60, 40);
        for (int frame = 0; frame < 3; frame++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        Assert.IsTrue(window.RejectedSceneUpdates > 0, "the composition was expected to be rejected");
        Assert.AreEqual(0, CountDifferences(factory, window, surface), "the window still shows the scene from before the rejected updates");

        // Once the composition is sound again the scene takes over where it left off.
        faulty.IsFaulty = false;
        faulty.Fill = Color.FromArgb(255, 60, 170, 80);
        for (int frame = 0; frame < 3; frame++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        Assert.AreEqual(0, CountDifferences(factory, window, surface), "the window did not recover after the composition was fixed");
    }

    private static int CountDifferences(GdiGraphicsFactory factory, Window window, IRenderSurface surface)
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

        return differing;
    }

    private sealed class DeclaresItsSlotTwice : Control
    {
        private Color _fill;
        private bool _isFaulty;

        public Color Fill
        {
            get => _fill;
            set
            {
                _fill = value;
                InvalidateVisual();
            }
        }

        public bool IsFaulty
        {
            get => _isFaulty;
            set
            {
                _isFaulty = value;
                RaiseRenderDirty(RenderDirtyKind.Composition);
            }
        }

        protected override void OnRender(IGraphicsContext context) => context.FillRectangle(Bounds, _fill);

        internal override void WriteComposition(CompositionPlanBuilder builder)
        {
            builder.Content(0);
            if (_isFaulty)
            {
                builder.Content(0);
            }
        }
    }
}
