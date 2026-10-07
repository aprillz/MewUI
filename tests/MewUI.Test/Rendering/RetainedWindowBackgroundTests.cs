using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// The window background is what a frame erases with, not something a visual draws. When it changes, as it
/// does on a theme switch, the area no visual covers (the window padding, the gaps between elements) has to
/// be erased again too, although no visual there changed.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedWindowBackgroundTests
{
    private const int WIDTH = 300;
    private const int HEIGHT = 200;
    private const int PADDING = 40;

    private static readonly Color LIGHT = Color.FromArgb(255, 240, 240, 240);
    private static readonly Color DARK = Color.FromArgb(255, 32, 32, 32);

    [TestMethod]
    public void ChangedWindowBackground_ReachesTheAreaNoVisualCovers()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var content = new Border
        {
            Width = 100,
            Height = 60,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Background = Color.FromArgb(255, 40, 120, 200),
        };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Padding = new Thickness(PADDING);
        window.Background = LIGHT;
        window.Content = content;
        window.PerformLayout();

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        for (int warmup = 0; warmup < 3; warmup++)
        {
            Frame(window, surface);
        }

        Assert.AreEqual(LIGHT, PixelAt(surface, 5, 5), "the padding was not drawn in the first background");

        window.Background = DARK;
        Frame(window, surface);

        Assert.AreEqual(DARK, PixelAt(surface, 5, 5), "the window padding kept the previous background");
        Assert.AreEqual(DARK, PixelAt(surface, WIDTH - 5, HEIGHT - 5), "the area beside the content kept the previous background");

        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        TestBackendSession.AssertSurfacesEqual(reference, surface, WIDTH, 0, "after the window background changed");
    }

    private static void Frame(Window window, IRenderSurface surface)
    {
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
    }

    private static Color PixelAt(IRenderSurface surface, int x, int y)
    {
        ReadOnlySpan<byte> pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int offset = (y * WIDTH + x) * 4;
        return Color.FromArgb(255, pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }
}
