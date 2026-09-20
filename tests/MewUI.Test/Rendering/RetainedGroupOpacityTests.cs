extern alias MewVGWin32;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

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
    [DataRow("MewVG")]
    [DataRow("Direct2D")]
    public void ChildChangedUnderAFadedParent_MatchesAFrameDrawnWhole(string backend)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("These backends are Windows-only.");
        }

        IGraphicsFactory factory = backend switch
        {
            "Gdi" => new GdiGraphicsFactory(),
            "Direct2D" => new Aprillz.MewUI.Rendering.Direct2D.Direct2DGraphicsFactory(),
            _ => new MewVGWin32GraphicsFactory(),
        };
        using var disposable = factory as IDisposable;
        Application.DefaultGraphicsFactory = factory;
        using var renderScope = factory is MewVGWin32GraphicsFactory ? factory.AcquireBackgroundRenderScope() : null;

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

    [TestMethod]
    [DataRow("Gdi")]
    [DataRow("Direct2D")]
    [DataRow("MewVG")]
    public void OverlappingChildrenOfAFadedParent_AreBlendedOnce(string backend)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("These backends are Windows-only.");
        }

        IGraphicsFactory factory = backend switch
        {
            "Gdi" => new GdiGraphicsFactory(),
            "Direct2D" => new Aprillz.MewUI.Rendering.Direct2D.Direct2DGraphicsFactory(),
            _ => new MewVGWin32GraphicsFactory(),
        };
        using var disposable = factory as IDisposable;
        Application.DefaultGraphicsFactory = factory;
        using var renderScope = factory is MewVGWin32GraphicsFactory ? factory.AcquireBackgroundRenderScope() : null;

        var lower = new Border { Width = 90, Height = 70, Background = Color.FromArgb(255, 30, 120, 220), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20, 20, 0, 0) };
        var upper = new Border { Width = 90, Height = 70, Background = Color.FromArgb(255, 220, 140, 30), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(70, 50, 0, 0) };
        var group = new Grid { Opacity = 0.5 };
        group.Children(lower, upper);
        var backdrop = new Border { Background = Color.FromArgb(255, 250, 250, 250), Child = group };

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = backdrop;
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        for (int index = 0; index < 2; index++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);

        // Where the two overlap only the upper one shows through the fade: half of it over the backdrop.
        AssertOverlap(factory, reference, backend, "a frame drawn straight from the visuals");
        AssertOverlap(factory, surface, backend, "a retained frame");
    }

    [TestMethod]
    public void ClipsAndTransforms_MakeNoSurfaceOfTheirOwn()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };
        for (int index = 0; index < 20; index++)
        {
            stack.Children(new Button { Content = new TextBlock { Text = "Row " + index }, Width = 120 });
        }

        var turned = new RotationDecorator { Rotation = Rotation.Clockwise90, Child = new Border { Width = 60, Height = 30, Background = Color.FromArgb(255, 200, 80, 40) } };
        var faded = new Border { Width = 60, Height = 30, Background = Color.FromArgb(255, 40, 80, 200), Opacity = 1 };
        var root = new StackPanel { Orientation = Orientation.Vertical };
        root.Children(new ScrollViewer { Height = 40, Content = stack }, turned, faded);

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = root;
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
        window.RetainedStatistics!.Reset();
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
        window.RenderReferenceFrameToSurface(surface);
        Assert.AreEqual(0, window.RetainedStatistics.GroupSurfaceCount, "a scene of clips and transforms made a surface of its own");

        // The same scene makes exactly one once something in it is faded as a group.
        faded.Opacity = 0.5;
        window.RetainedStatistics.Reset();
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
        Assert.AreEqual(1, window.RetainedStatistics.GroupSurfaceCount);

        // A visual faded all the way out shows nothing, so a frame that replays it has no group to make.
        faded.Opacity = 0;
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
        window.RetainedStatistics.Reset();
        stack.Children(new Button { Content = new TextBlock { Text = "one more" }, Width = 120 });
        turned.Rotation = Rotation.CounterClockwise90;
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
        Assert.AreEqual(0, window.RetainedStatistics.GroupSurfaceCount, "a visual with no opacity left was given a surface");
    }

    private static void AssertOverlap(IGraphicsFactory factory, IRenderSurface surface, string backend, string label)
    {
        var pixels = new byte[WIDTH * HEIGHT * 4];
        Assert.IsTrue(((IRenderDevice)factory).TryReadPixels(surface, pixels, WIDTH * 4));
        int offset = ((70 * WIDTH) + 90) * 4;
        int blue = pixels[offset];
        int green = pixels[offset + 1];
        int red = pixels[offset + 2];
        bool blendedOnce = Math.Abs(red - 235) <= 2 && Math.Abs(green - 195) <= 2 && Math.Abs(blue - 140) <= 2;
        Assert.IsTrue(blendedOnce, $"{backend}, {label}: the overlap is RGB({red},{green},{blue}), and half of the upper child over the backdrop is RGB(235,195,140)");
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
