using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A full Window rendered headless (fake backend + GDI offscreen surface) must produce
/// readable content pixels via RenderFrameToSurface: the frame-acquisition path the editor
/// previewer relies on (agent/preview-tooling/plan.md 4.4).
/// Not parallelizable: reassigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WindowRenderFrameToSurfaceTests
{
    private const int WIDTH = 200;
    private const int HEIGHT = 150;

    [TestMethod]
    public void HeadlessWindow_RenderFrameToSurface_ProducesContentPixels()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Headless window uses the Windows-only GDI factory.");
            return;
        }

        var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1.0));

        var contentColor = Color.FromArgb(255, 190, 40, 40);
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = new Border
        {
            Background = contentColor,
            Child = new TextBlock
            {
                Text = "Preview",
                Foreground = Color.FromArgb(255, 255, 255, 255),
                FontSize = 16,
            },
        };
        window.PerformLayout();

        window.RenderFrameToSurface(surface);

        var cpu = (ICpuPixelSurface)surface;
        ReadOnlySpan<byte> pixels = cpu.GetReadOnlyPixelSpan();
        int stride = cpu.StrideBytes;
        int contentPixels = 0, opaquePixels = 0;
        for (int y = 0; y < HEIGHT; y++)
        {
            for (int x = 0; x < WIDTH; x++)
            {
                int offset = y * stride + x * 4;
                byte blue = pixels[offset + 0];
                byte green = pixels[offset + 1];
                byte red = pixels[offset + 2];
                byte alpha = pixels[offset + 3];
                if (alpha == 255) opaquePixels++;
                if (red > 150 && green < 90 && blue < 90) contentPixels++;
            }
        }

        Console.WriteLine($"[preview frame] opaquePixels={opaquePixels}/{WIDTH * HEIGHT}, contentPixels={contentPixels}");
        Assert.AreEqual(WIDTH * HEIGHT, opaquePixels, "an opaque window frame must fill the surface with opaque pixels");
        Assert.IsGreaterThan(WIDTH * HEIGHT / 2, contentPixels,
            "the Border background must dominate the frame; RenderFrameToSurface did not paint the content tree");
    }
}
