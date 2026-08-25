using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
[DoNotParallelize]
public sealed class TextBlockLayoutCostTests
{
    [TestMethod]
    public void SteadyStateFramesDoNotRebuildTheLayout()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var previousFactory = Application.DefaultGraphicsFactory;
        using var gdi = new GdiGraphicsFactory();
        using var factory = new CountingGraphicsFactory(gdi);
        Application.DefaultGraphicsFactory = factory;
        try
        {
            var block = new TextBlock().Text("Steady state label");
            using var window = HeadlessWindow.Create(300, 120);
            window.Content = block;

            RenderFrame(window, factory);
            factory.Reset();

            for (int frame = 0; frame < 5; frame++)
            {
                RenderFrame(window, factory);
            }

            Assert.AreEqual(0, factory.MeasureTextCalls,
                "Repainting an unchanged TextBlock re-measured text.");
            Assert.AreEqual(0, factory.CreateTextLayoutCalls,
                "Repainting an unchanged TextBlock rebuilt a backend layout.");
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    [TestMethod]
    public void PlainLabelLayoutStaysOnTheFastPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var previousFactory = Application.DefaultGraphicsFactory;
        using var gdi = new GdiGraphicsFactory();
        using var factory = new CountingGraphicsFactory(gdi);
        Application.DefaultGraphicsFactory = factory;
        try
        {
            var block = new TextBlock().Text("A plain single line label");
            using var window = HeadlessWindow.Create(300, 120);
            window.Content = block;
            factory.Reset();

            RenderFrame(window, factory);

            // Measure and render resolve different constraints, so two layouts is the floor; the
            // fast path keeps each to a single whole-string measurement.
            Assert.IsLessThanOrEqualTo(4, factory.MeasureTextCalls,
                $"Plain label cost {factory.MeasureTextCalls} measure calls.");
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }

    private static void RenderFrame(Window window, IGraphicsFactory factory)
    {
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(300, 120, 1));
        window.RenderFrameToSurface(surface);
    }
}
