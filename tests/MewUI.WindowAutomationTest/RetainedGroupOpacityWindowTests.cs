using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A faded visual is blended onto the window once, as a whole: where two of its children overlap only
/// the upper one shows through the fade. Every backend of every platform has to come out that way,
/// through the retained scene and drawn straight from the visuals alike.
/// </summary>
[TestClass]
public sealed class RetainedGroupOpacityWindowTests
{
    private const int TOLERANCE = 3;

    [TestMethod]
    public Task OverlappingChildrenOfAFadedParent_AreBlendedOnce() => CaptureScene.RunAsync(async scene =>
    {
        var lower = new Border { Width = 90, Height = 70, Background = Color.FromArgb(255, 30, 120, 220), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20, 20, 0, 0) };
        var upper = new Border { Width = 90, Height = 70, Background = Color.FromArgb(255, 220, 140, 30), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(70, 50, 0, 0) };
        var group = new Grid { Opacity = 0.5 };
        group.Children(lower, upper);
        var backdrop = new Border { Background = Color.FromArgb(255, 250, 250, 250), Child = group };

        var window = await scene.ShowAsync(backdrop);
        await Task.Delay(400);

        string backend = window.GraphicsFactory.Backend;
        AssertOverlap(Capture(window, reference: true), backend, "a frame drawn straight from the visuals");
        AssertOverlap(Capture(window, reference: false), backend, "a retained frame");
    });

    private static (byte[] Pixels, int Width, double Scale) Capture(Window window, bool reference)
    {
        var factory = window.GraphicsFactory;
        double scale = window.GetDpi() / 96.0;
        int width = (int)Math.Round(window.ClientSize.Width * scale);
        int height = (int)Math.Round(window.ClientSize.Height * scale);

        // A GL backend renders offscreen only under a current context.
        using var renderScope = factory.AcquireBackgroundRenderScope();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(width, height, scale, hasAlpha: false));
        if (reference)
        {
            window.RenderReferenceFrameToSurface(surface);
        }
        else
        {
            // The first frame into a new surface is whole; the second goes through the kept frame.
            window.RenderFrameToSurface(surface);
            window.RenderFrameToSurface(surface);
        }

        var pixels = new byte[width * height * 4];
        if (!((IRenderDevice)factory).TryReadPixels(surface, pixels, width * 4))
        {
            Assert.Inconclusive($"{factory.Backend} cannot read an offscreen surface back.");
        }

        return (pixels, width, scale);
    }

    private static void AssertOverlap((byte[] Pixels, int Width, double Scale) shot, string backend, string label)
    {
        int column = (int)Math.Round(90 * shot.Scale);
        int row = (int)Math.Round(70 * shot.Scale);
        int offset = ((row * shot.Width) + column) * 4;
        int blue = shot.Pixels[offset];
        int green = shot.Pixels[offset + 1];
        int red = shot.Pixels[offset + 2];
        bool blendedOnce = Math.Abs(red - 235) <= TOLERANCE && Math.Abs(green - 195) <= TOLERANCE && Math.Abs(blue - 140) <= TOLERANCE;
        Assert.IsTrue(blendedOnce, $"{backend}, {label}: the overlap is RGB({red},{green},{blue}), and half of the upper child over the backdrop is RGB(235,195,140)");
    }
}
