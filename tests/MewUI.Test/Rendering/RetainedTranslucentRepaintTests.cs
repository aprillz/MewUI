extern alias MewVGWin32;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;
using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

/// <summary>
/// A translucent drawing on a transparent window (the shadow of a popup) is blended onto what is under
/// it. Painted again over a frame that was kept, it must not build up: after any number of frames the
/// surface has to hold what one frame alone would, alpha included.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedTranslucentRepaintTests
{
    private const int WIDTH = 160;
    private const int HEIGHT = 120;

    // One step of rounding between a frame painted whole and one painted in parts; a second layer of a
    // translucent drawing is dozens of steps.
    private const int CHANNEL_TOLERANCE = 1;

    private sealed class Shade : Control
    {
        internal Color Fill { get; set; } = Color.FromArgb(60, 0, 0, 0);

        protected override void OnRender(IGraphicsContext context)
        {
            context.DrawBoxShadow(new Rect(Bounds.X + 20, Bounds.Y + 20, Bounds.Width - 40, Bounds.Height - 40), 6, 12, Fill);
            context.FillRectangle(new Rect(Bounds.X + 20, Bounds.Y + 20, Bounds.Width - 40, Bounds.Height - 40), Color.FromArgb(255, 240, 240, 240));
        }
    }

    [TestMethod]
    [DataRow("Gdi", false)]
    [DataRow("Gdi", true)]
    [DataRow("Direct2D", false)]
    [DataRow("Direct2D", true)]
    [DataRow("MewVG", false)]
    [DataRow("MewVG", true)]
    public void TranslucentDrawingRepaintedManyTimes_DoesNotBuildUp(string backend, bool wholeFrames)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("These backends are Windows-only.");
        }

        IGraphicsFactory factory;
        try
        {
            factory = backend switch
            {
                "Gdi" => new GdiGraphicsFactory(),
                "Direct2D" => new Aprillz.MewUI.Rendering.Direct2D.Direct2DGraphicsFactory(),
                _ => new MewVGWin32GraphicsFactory(),
            };
        }
        catch (Exception exception)
        {
            Assert.Inconclusive($"{backend} is not available here: {exception.Message}");
            return;
        }

        using var disposable = factory as IDisposable;

        // The GL backend draws offscreen only with a context current on this thread.
        using var renderScope = factory is MewVGWin32GraphicsFactory ? factory.AcquireBackgroundRenderScope() : null;
        Application.DefaultGraphicsFactory = factory;

        byte[] once = Render(factory, frames: 1, wholeFrames);
        byte[] many = Render(factory, frames: 12, wholeFrames);

        int differing = 0;
        int largest = 0;
        for (int offset = 0; offset < once.Length; offset++)
        {
            int delta = Math.Abs(once[offset] - many[offset]);
            if (delta > CHANNEL_TOLERANCE)
            {
                differing++;
                largest = Math.Max(largest, delta);
            }
        }

        Assert.AreEqual(0, differing, $"{backend}: {differing} channel values differ after repeated frames, by up to {largest}: the translucent drawing built up");
    }

    private static byte[] Render(IGraphicsFactory factory, int frames, bool wholeFrames)
    {
        var shade = new Shade();
        var marker = new Border { Width = 6, Height = 6, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Background = Color.FromArgb(255, 200, 0, 0) };
        var grid = new Grid();
        grid.Children(shade, marker);

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.AllowsTransparency = true;
        window.Background = Color.Transparent;
        window.Content = grid;
        window.PerformLayout();

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: true));
        for (int index = 0; index < frames; index++)
        {
            // The same drawing is asked for again every frame, the way an animated shadow owner does.
            shade.Fill = Color.FromArgb((byte)(60 + (index % 2)), 0, 0, 0);
            shade.InvalidateVisual();
            if (wholeFrames)
            {
                window.InvalidateVisual();
            }

            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        // End on the same drawing whatever the frame count.
        shade.Fill = Color.FromArgb(60, 0, 0, 0);
        shade.InvalidateVisual();
        window.PerformLayout();
        window.RenderFrameToSurface(surface);

        var pixels = new byte[WIDTH * HEIGHT * 4];
        Assert.IsTrue(((IRenderDevice)factory).TryReadPixels(surface, pixels, WIDTH * 4), "the surface could not be read back");
        return pixels;
    }
}
