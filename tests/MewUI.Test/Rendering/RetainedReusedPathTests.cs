using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Drawing code may build every outline in one path object it clears and fills again, which is how
/// the arrows of drop-downs and numeric boxes are drawn. A recording that holds several such drawings
/// (a grid view records all its cells as one) has to keep each outline as it was when it was drawn.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedReusedPathTests
{
    private const int WIDTH = 200;
    private const int HEIGHT = 120;

    private sealed class TwoArrows : Control
    {
        private static readonly PathGeometry _scratch = new();

        protected override void OnRender(IGraphicsContext context)
        {
            Arrow(context, 20, 20, Color.FromArgb(255, 200, 40, 40));
            Arrow(context, 120, 60, Color.FromArgb(255, 40, 40, 200));
        }

        private static void Arrow(IGraphicsContext context, double x, double y, Color color)
        {
            _scratch.Reset();
            _scratch.MoveTo(x, y);
            _scratch.LineTo(x + 40, y);
            _scratch.LineTo(x + 20, y + 30);
            _scratch.Close();
            context.FillPath(_scratch, color);
        }
    }

    [TestMethod]
    public void PathClearedAndFilledAgainInsideOneRecording_KeepsEveryOutline()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = new TwoArrows();
        window.PerformLayout();

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        for (int index = 0; index < 2; index++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

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

        Assert.AreEqual(0, differing, $"{differing} pixels differ: the second outline was drawn as the first");
    }
}
