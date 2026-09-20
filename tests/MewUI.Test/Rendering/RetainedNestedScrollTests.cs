extern alias MewVGWin32;
extern alias MewVGX11;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;
using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;
using MewVGX11GraphicsFactory = MewVGX11::Aprillz.MewUI.Rendering.MewVG.MewVGX11GraphicsFactory;

namespace MewUI.Test.Rendering;

/// <summary>
/// A scroll region nested in another scroll region keeps a composition layer of its own. Moving the
/// outer region records the inner layer into the outer one, so every frame must still match an
/// immediate render of the same offsets.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedNestedScrollTests
{
    private const int WIDTH = 120;
    private const int HEIGHT = 96;

    private sealed class Leaf : FrameworkElement
    {
        internal Color FillColor { get; set; } = Color.Red;

        protected override Size MeasureContent(Size availableSize) => Size.Empty;

        protected override void OnRender(IGraphicsContext context)
            => context.FillRectangle(Bounds, FillColor);
    }

    private static (Window window, ScrollViewer outer, ScrollViewer inner) CreateNestedScene()
    {
        var inner = new ScrollViewer
        {
            Height = 40,
            VerticalScroll = ScrollMode.Visible,
            Content = new StackPanel().Vertical().Children(
                new Leaf { Height = 20, FillColor = Color.FromArgb(255, 200, 30, 30) },
                new Leaf { Height = 20, FillColor = Color.FromArgb(255, 30, 200, 30) },
                new Leaf { Height = 20, FillColor = Color.FromArgb(255, 30, 30, 200) },
                new Leaf { Height = 20, FillColor = Color.FromArgb(255, 200, 200, 30) }),
        };
        var outer = new ScrollViewer
        {
            VerticalScroll = ScrollMode.Visible,
            Content = new StackPanel().Vertical().Children(
                new Leaf { Height = 30, FillColor = Color.FromArgb(255, 120, 40, 160) },
                inner,
                new Leaf { Height = 30, FillColor = Color.FromArgb(255, 40, 160, 120) },
                new Leaf { Height = 30, FillColor = Color.FromArgb(255, 160, 120, 40) },
                new Leaf { Height = 30, FillColor = Color.FromArgb(255, 80, 80, 200) }),
        };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = outer;
        window.PerformLayout();
        return (window, outer, inner);
    }

    private static string Compare(IGraphicsFactory factory, Window window, IRenderSurface retained)
    {
        using var immediateSurface = factory.CreateSurface(
            RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1));
        window.RenderReferenceFrameToSurface(immediateSurface);
        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)immediateSurface).GetReadOnlyPixelSpan();
        ReadOnlySpan<byte> actual = ((ICpuPixelSurface)retained).GetReadOnlyPixelSpan();
        int differing = 0;
        int firstIndex = -1;
        for (int index = 0; index < expected.Length; index += 4)
        {
            if (expected[index] != actual[index] ||
                expected[index + 1] != actual[index + 1] ||
                expected[index + 2] != actual[index + 2])
            {
                differing++;
                if (firstIndex < 0)
                {
                    firstIndex = index;
                }
            }
        }
        if (differing == 0)
        {
            return string.Empty;
        }

        int pixelIndex = firstIndex / 4;
        return $"{differing} pixels differ, first at ({pixelIndex % WIDTH},{pixelIndex / WIDTH}) " +
            $"expected=({expected[firstIndex + 2]},{expected[firstIndex + 1]},{expected[firstIndex]}) " +
            $"actual=({actual[firstIndex + 2]},{actual[firstIndex + 1]},{actual[firstIndex]})";
    }

    [TestMethod]
    public void WindowComparisonPath_OuterScrollAfterInnerScrollMatchesImmediateFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The retained nested scroll comparison uses the Windows GDI factory.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        AssertOuterScrollAfterInnerScrollMatchesImmediateFrame(factory);
    }

    [TestMethod]
    public void Direct2DWindowComparisonPath_OuterScrollAfterInnerScrollMatchesImmediateFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Direct2D retained nested scroll comparison is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        AssertOuterScrollAfterInnerScrollMatchesImmediateFrame(factory);
    }

    [TestMethod]
    public void MewVGWin32WindowComparisonPath_OuterScrollAfterInnerScrollMatchesImmediateFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The MewVG Win32 retained nested scroll comparison is Windows-only.");
            return;
        }

        using var factory = new MewVGWin32GraphicsFactory();
        using var renderScope = factory.AcquireBackgroundRenderScope();
        AssertOuterScrollAfterInnerScrollMatchesImmediateFrame(factory);
    }

    private static void AssertOuterScrollAfterInnerScrollMatchesImmediateFrame(IGraphicsFactory factory)
    {
        Application.DefaultGraphicsFactory = factory;
        var (window, outer, inner) = CreateNestedScene();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1));

        window.RenderFrameToSurface(surface);
        inner.SetScrollOffsets(0, 20);
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
        outer.SetScrollOffsets(0, 25);
        window.PerformLayout();
        window.RenderFrameToSurface(surface);

        string difference = Compare(factory, window, surface);
        Assert.AreEqual(string.Empty, difference, $"inner-then-outer: {difference}");
    }

    [TestMethod]
    public void MewVGX11WindowComparisonPath_ScrollingToBothEndsRepeatedlyMatchesImmediateFrame()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("The MewVG X11 retained scroll comparison needs an X11 session.");
            return;
        }

        using var factory = new MewVGX11GraphicsFactory();
        using var renderScope = factory.AcquireBackgroundRenderScope();
        AssertRoundTripScrollMatchesImmediateFrame(factory);
    }

    [TestMethod]
    public void MewVGX11WindowComparisonPath_OuterScrollAfterInnerScrollMatchesImmediateFrame()
    {
        if (!OperatingSystem.IsLinux())
        {
            Assert.Inconclusive("The MewVG X11 retained scroll comparison needs an X11 session.");
            return;
        }

        using var factory = new MewVGX11GraphicsFactory();
        using var renderScope = factory.AcquireBackgroundRenderScope();
        AssertOuterScrollAfterInnerScrollMatchesImmediateFrame(factory);
    }

    [TestMethod]
    public void WindowComparisonPath_AlternatingNestedScrollMatchesImmediateFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The retained nested scroll comparison uses the Windows GDI factory.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        AssertAlternatingNestedScrollMatchesImmediateFrame(factory);
    }

    private static void AssertAlternatingNestedScrollMatchesImmediateFrame(IGraphicsFactory factory)
    {
        Application.DefaultGraphicsFactory = factory;
        var (window, outer, inner) = CreateNestedScene();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1));

        window.RenderFrameToSurface(surface);
        double[] innerOffsets = [10, 10, 20, 20, 30];
        double[] outerOffsets = [0, 12, 12, 24, 24];
        for (int step = 0; step < innerOffsets.Length; step++)
        {
            inner.SetScrollOffsets(0, innerOffsets[step]);
            outer.SetScrollOffsets(0, outerOffsets[step]);
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
            string stepDifference = Compare(factory, window, surface);
            Assert.AreEqual(string.Empty, stepDifference,
                $"alternating step {step} (inner={innerOffsets[step]}, outer={outerOffsets[step]}): {stepDifference}");
        }

    }

    [TestMethod]
    public void WindowComparisonPath_ScrollingToBothEndsRepeatedlyMatchesImmediateFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The retained nested scroll comparison uses the Windows GDI factory.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        AssertRoundTripScrollMatchesImmediateFrame(factory);
    }

    [TestMethod]
    public void MewVGWin32WindowComparisonPath_ScrollingToBothEndsRepeatedlyMatchesImmediateFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The MewVG Win32 retained nested scroll comparison is Windows-only.");
            return;
        }

        using var factory = new MewVGWin32GraphicsFactory();
        using var renderScope = factory.AcquireBackgroundRenderScope();
        AssertRoundTripScrollMatchesImmediateFrame(factory);
    }

    private static void AssertRoundTripScrollMatchesImmediateFrame(IGraphicsFactory factory)
    {
        Application.DefaultGraphicsFactory = factory;
        var (window, outer, inner) = CreateNestedScene();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1));

        window.RenderFrameToSurface(surface);
        inner.SetScrollOffsets(0, 20);
        window.PerformLayout();
        window.RenderFrameToSurface(surface);

        const double STEP = 12;
        for (int roundTrip = 0; roundTrip < 3; roundTrip++)
        {
            for (double offset = 0; offset <= 120; offset += STEP)
            {
                outer.SetScrollOffsets(0, offset);
                window.PerformLayout();
                window.RenderFrameToSurface(surface);
                string downDifference = Compare(factory, window, surface);
                Assert.AreEqual(string.Empty, downDifference,
                    $"round trip {roundTrip} down to {offset}: {downDifference}");
            }
            for (double offset = 120; offset >= 0; offset -= STEP)
            {
                outer.SetScrollOffsets(0, offset);
                window.PerformLayout();
                window.RenderFrameToSurface(surface);
                string upDifference = Compare(factory, window, surface);
                Assert.AreEqual(string.Empty, upDifference,
                    $"round trip {roundTrip} up to {offset}: {upDifference}");
            }
        }

    }

    [TestMethod]
    public void WindowComparisonPath_RepeatedOuterScrollMatchesImmediateFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The retained nested scroll comparison uses the Windows GDI factory.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        AssertRepeatedOuterScrollMatchesImmediateFrame(factory);
    }

    private static void AssertRepeatedOuterScrollMatchesImmediateFrame(IGraphicsFactory factory)
    {
        Application.DefaultGraphicsFactory = factory;
        var (window, outer, _) = CreateNestedScene();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1));

        window.RenderFrameToSurface(surface);
        for (int step = 1; step <= 5; step++)
        {
            outer.SetScrollOffsets(0, step * 7);
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        string difference = Compare(factory, window, surface);
        Assert.AreEqual(string.Empty, difference, $"repeated-outer: {difference}");
    }

    [TestMethod]
    public void WindowComparisonPath_OuterScrollWithContentChangeMatchesImmediateFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The retained nested scroll comparison uses the Windows GDI factory.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        AssertOuterScrollWithContentChangeMatchesImmediateFrame(factory);
    }

    private static void AssertOuterScrollWithContentChangeMatchesImmediateFrame(IGraphicsFactory factory)
    {
        Application.DefaultGraphicsFactory = factory;
        var (window, outer, inner) = CreateNestedScene();
        var animated = (Leaf)((StackPanel)inner.Content!).Children[0];
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1));

        window.RenderFrameToSurface(surface);
        for (int step = 1; step <= 4; step++)
        {
            outer.SetScrollOffsets(0, step * 6);
            animated.FillColor = Color.FromArgb(255, (byte)(40 + step * 40), 60, 60);
            animated.InvalidateVisual();
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        string difference = Compare(factory, window, surface);
        Assert.AreEqual(string.Empty, difference, $"scroll-with-content-change: {difference}");
    }

    [TestMethod]
    [DataRow("Direct2D", "Alternating")]
    [DataRow("Direct2D", "RoundTrip")]
    [DataRow("Direct2D", "RepeatedOuter")]
    [DataRow("Direct2D", "ContentChange")]
    [DataRow("MewVG", "Alternating")]
    [DataRow("MewVG", "RepeatedOuter")]
    [DataRow("MewVG", "ContentChange")]
    public void OtherBackends_NestedScrollMatchesImmediateFrame(string backend, string scenario)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("These backends are Windows-only.");
            return;
        }

        IGraphicsFactory factory = backend == "Direct2D" ? new Direct2DGraphicsFactory() : new MewVGWin32GraphicsFactory();
        using var disposable = factory as IDisposable;
        using var renderScope = factory is MewVGWin32GraphicsFactory ? factory.AcquireBackgroundRenderScope() : null;
        switch (scenario)
        {
            case "Alternating":
                AssertAlternatingNestedScrollMatchesImmediateFrame(factory);
                break;
            case "RoundTrip":
                AssertRoundTripScrollMatchesImmediateFrame(factory);
                break;
            case "RepeatedOuter":
                AssertRepeatedOuterScrollMatchesImmediateFrame(factory);
                break;
            default:
                AssertOuterScrollWithContentChangeMatchesImmediateFrame(factory);
                break;
        }
    }
}
