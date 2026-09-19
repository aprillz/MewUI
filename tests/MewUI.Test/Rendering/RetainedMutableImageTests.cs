using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A recording keeps the image it drew. When the pixels of that image change, or the image is swapped
/// and the old one disposed, the surface has to show the new pixels, and only the image's area may be
/// repainted for it.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedMutableImageTests
{
    private const int WIDTH = 240;
    private const int HEIGHT = 160;

    [TestMethod]
    [DataRow("Gdi")]
    [DataRow("Direct2D")]
    public void PixelsChangedAndImageSwapped_ShowOnTheSurface(string backend)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("These backends are Windows-only.");
        }

        IGraphicsFactory factory = backend == "Gdi"
            ? new GdiGraphicsFactory()
            : new Aprillz.MewUI.Rendering.Direct2D.Direct2DGraphicsFactory();
        using var disposable = factory as IDisposable;
        Application.DefaultGraphicsFactory = factory;

        var bitmap = new WriteableBitmap(48, 32, clear: true, hasAlpha: false);
        bitmap.Clear(Color.FromArgb(255, 30, 140, 220));
        var image = new Image { Source = bitmap, Width = 48, Height = 32, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20) };
        var label = new TextBlock { Text = "beside the image", HorizontalAlignment = HorizontalAlignment.Right };
        var grid = new Grid();
        grid.Children(image, label);

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = grid;
        window.PerformLayout();

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        Frames(window, surface, 3);
        AssertMatchesReference(factory, window, surface, backend, "first frames");

        bitmap.Clear(Color.FromArgb(255, 220, 60, 40));
        Frames(window, surface, 2);
        AssertMatchesReference(factory, window, surface, backend, "after the pixels changed");
        Assert.AreEqual(
            (byte)220,
            ReadRed(factory, surface, 40, 34),
            $"{backend}: the surface still shows the old pixels of the bitmap");

        var replacement = new WriteableBitmap(48, 32, clear: true, hasAlpha: false);
        replacement.Clear(Color.FromArgb(255, 40, 200, 90));
        image.Source = replacement;
        bitmap.Dispose();
        Frames(window, surface, 2);
        AssertMatchesReference(factory, window, surface, backend, "after the image was swapped and the old one disposed");

        // A frame that repaints another area must not reach into a recording of the disposed bitmap.
        label.Text = "changed";
        Frames(window, surface, 2);
        AssertMatchesReference(factory, window, surface, backend, "after a change beside the image");
        replacement.Dispose();
    }

    private static void Frames(Window window, IRenderSurface surface, int count)
    {
        for (int index = 0; index < count; index++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }
    }

    private static byte ReadRed(IGraphicsFactory factory, IRenderSurface surface, int x, int y)
    {
        var pixels = new byte[WIDTH * HEIGHT * 4];
        Assert.IsTrue(((IRenderDevice)factory).TryReadPixels(surface, pixels, WIDTH * 4));
        return pixels[((y * WIDTH) + x) * 4 + 2];
    }

    private static void AssertMatchesReference(IGraphicsFactory factory, Window window, IRenderSurface actual, string backend, string label)
    {
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        var expected = new byte[WIDTH * HEIGHT * 4];
        var shown = new byte[WIDTH * HEIGHT * 4];
        var device = (IRenderDevice)factory;
        Assert.IsTrue(device.TryReadPixels(reference, expected, WIDTH * 4) && device.TryReadPixels(actual, shown, WIDTH * 4));
        int differing = 0;
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            if (expected[offset] != shown[offset] || expected[offset + 1] != shown[offset + 1] || expected[offset + 2] != shown[offset + 2])
            {
                differing++;
            }
        }

        Assert.AreEqual(0, differing, $"{backend} {label}: {differing} pixels differ from a frame drawn straight from the visuals");
    }
}
