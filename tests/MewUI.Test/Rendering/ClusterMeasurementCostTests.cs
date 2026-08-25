using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Text;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class ClusterMeasurementCostTests
{
    [TestMethod]
    public void WrappedTextMeasuresPerRunNotPerCluster()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var previousFactory = Application.DefaultGraphicsFactory;
        using var inner = new GdiGraphicsFactory();
        using var factory = new CountingGraphicsFactory(inner);
        Application.DefaultGraphicsFactory = factory;
        try
        {
            // Wrapping keeps this off the fast path, so it goes through cluster measurement.
            string text = new string('a', 200);
            factory.Reset();
            ((IGraphicsFactory)factory).TextEngine.CreateLayout(new TextLayoutRequest
            {
                Text = text.AsMemory(),
                Dpi = 96,
                DefaultStyle = new TextRunStyle("Consolas", 12),
                Paragraph = new TextParagraphStyle { Wrapping = TextWrapping.Wrap, MaxWidth = 100 }
            });

            Assert.IsLessThan(
                10,
                factory.MeasureTextCalls,
                $"Cluster measurement should stay per-run; {factory.MeasureTextCalls} calls for {text.Length} characters.");
        }
        finally
        {
            Application.DefaultGraphicsFactory = previousFactory;
        }
    }
}
