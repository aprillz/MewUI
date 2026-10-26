using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Wrapped text draws the lines its measure broke, so a measure at a new width draws it again even when its
/// parent keeps the same box for it.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedWrapWidthTests
{
    private const int WIDTH = 320;
    private const int HEIGHT = 240;

    [TestMethod]
    public void MeasureAtANewWidth_InTheSameBox_DrawsTheTextAgain()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var text = new CountingText { Text = "aaaa bbbb cccc dddd eeee ffff gggg hhhh", TextWrapping = TextWrapping.Wrap };
        var host = new FixedSlotHost { MeasureWidth = 200 };
        host.Add(text);
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = host;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        for (int warm = 0; warm < 3; warm++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        int renders = text.Renders;
        var bounds = text.Bounds;
        host.MeasureWidth = 90;
        host.InvalidateMeasure();
        window.PerformLayout();
        window.RenderFrameToSurface(surface);

        Assert.AreEqual(bounds, text.Bounds, "the host moved the text, so the test proves nothing");
        Assert.AreEqual(renders + 1, text.Renders, "the text measured at a new width kept drawing its old lines");
    }

    private sealed class CountingText : TextBlock
    {
        public int Renders { get; private set; }

        protected override void OnRender(IGraphicsContext context)
        {
            Renders++;
            base.OnRender(context);
        }
    }

    /// <summary>Measures its child at a width it is told and always arranges it in the same box.</summary>
    private sealed class FixedSlotHost : Panel
    {
        public double MeasureWidth { get; set; }

        protected override Size MeasureContent(Size availableSize)
        {
            for (int index = 0; index < Count; index++)
            {
                this[index].Measure(new Size(MeasureWidth, double.PositiveInfinity));
            }

            return new Size(WIDTH, HEIGHT);
        }

        protected override void ArrangeContent(Rect bounds)
        {
            for (int index = 0; index < Count; index++)
            {
                this[index].Arrange(new Rect(bounds.X, bounds.Y, 200, 120));
            }
        }
    }
}
