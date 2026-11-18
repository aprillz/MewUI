using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// An element that a canvas places somewhere else draws what it drew before, so the frame moves the drawing it
/// kept instead of asking the element to draw again, however many frames in a row it moves.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedCanvasMoveTests
{
    private const int WIDTH = 300;
    private const int HEIGHT = 200;
    private const int SHAPES = 20;

    [TestMethod]
    public void ElementsMovedThroughCanvasPositions_AreReplayedNotRecordedAgain()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var shapes = new Rectangle[SHAPES];
        var canvas = new Canvas();
        for (int index = 0; index < SHAPES; index++)
        {
            shapes[index] = new Rectangle { Width = 12, Height = 12, Fill = Color.FromArgb(255, 40, (byte)(10 * index), 200) };
            Canvas.SetLeft(shapes[index], 10 * index);
            Canvas.SetTop(shapes[index], 20);
            canvas.Children(shapes[index]);
        }

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = canvas;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.PerformLayout();
        window.RenderFrameToSurface(surface);

        for (int frame = 1; frame <= 12; frame++)
        {
            for (int index = 0; index < SHAPES; index++)
            {
                Canvas.SetTop(shapes[index], 20 + frame);
            }

            window.RetainedStatistics!.Reset();
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
            Assert.AreEqual(0, window.RetainedStatistics.LiveFallbackCount, $"frame {frame}: moved elements were drawn live");
            Assert.IsLessThanOrEqualTo(1, window.RetainedStatistics.ContentRecordCount, $"frame {frame}: moved elements were recorded again");
        }

        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        TestBackendSession.AssertSurfacesEqual(reference, surface, WIDTH, 0, "after the moves");
    }
}
