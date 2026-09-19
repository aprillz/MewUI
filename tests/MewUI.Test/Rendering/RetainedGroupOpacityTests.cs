using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Opacity on a visual fades the visual as a whole: where two of its children overlap, the pair is
/// blended once, not child by child. A frame that repaints only one of those children has to come out
/// the same as a frame that draws everything, on every backend.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedGroupOpacityTests
{
    private const int WIDTH = 220;
    private const int HEIGHT = 160;

    [TestMethod]
    [DataRow("Gdi")]
    [DataRow("Direct2D")]
    public void ChildChangedUnderAFadedParent_MatchesAFrameDrawnWhole(string backend)
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

        var lower = new Border { Width = 90, Height = 70, Background = Color.FromArgb(255, 30, 120, 220), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20, 20, 0, 0) };
        var upper = new Border { Width = 90, Height = 70, Background = Color.FromArgb(255, 220, 140, 30), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(70, 50, 0, 0) };
        var group = new Grid { Opacity = 0.5 };
        group.Children(lower, upper);
        var backdrop = new Border { Background = Color.FromArgb(255, 250, 250, 250), Child = group };

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = backdrop;
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));

        Check(factory, window, surface, backend, "first frames");

        lower.Background = Color.FromArgb(255, 40, 200, 90);
        Check(factory, window, surface, backend, "after the lower child changed");

        upper.Background = Color.FromArgb(255, 200, 40, 160);
        Check(factory, window, surface, backend, "after the upper child changed");

        group.Opacity = 0.8;
        Check(factory, window, surface, backend, "after the opacity changed");

        group.Opacity = 1;
        Check(factory, window, surface, backend, "after the fade was removed");
    }

    private static void Check(IGraphicsFactory factory, Window window, IRenderSurface surface, string backend, string label)
    {
        for (int index = 0; index < 2; index++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        var expected = new byte[WIDTH * HEIGHT * 4];
        var shown = new byte[WIDTH * HEIGHT * 4];
        var device = (IRenderDevice)factory;
        Assert.IsTrue(device.TryReadPixels(reference, expected, WIDTH * 4) && device.TryReadPixels(surface, shown, WIDTH * 4));
        int differing = 0;
        int largest = 0;
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                int delta = Math.Abs(expected[offset + channel] - shown[offset + channel]);
                if (delta > 0)
                {
                    differing++;
                    largest = Math.Max(largest, delta);
                }
            }
        }

        Assert.AreEqual(0, differing, $"{backend} {label}: {differing} channel values differ from a frame drawn straight from the visuals, by up to {largest}");
    }
}
