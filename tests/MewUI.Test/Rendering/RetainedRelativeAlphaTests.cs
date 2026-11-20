using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// The alpha a visual sets is relative to the alpha the framework draws it under. A recording taken once
/// must follow an ancestor that keeps fading, and a visual that sets its alpha outright must still fade
/// with its ancestor, whether the frame is replayed or drawn straight from the visuals.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedRelativeAlphaTests
{
    private const int WIDTH = 120;
    private const int HEIGHT = 80;

    [TestMethod]
    public void AlphaMultipliedUnderAFadingParent_FollowsTheParentWithoutBeingRecordedAgain()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        var fill = new DimmedFill();
        var host = new FadingHost(fill) { Alpha = 0 };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = host;
        using var surface = Surface(factory);
        RenderFrames(window, surface);
        int recordings = fill.RenderCount;

        host.Alpha = 1;
        RenderFrames(window, surface);

        Assert.AreEqual(recordings, fill.RenderCount, "the fill was recorded again, so the replay was not tested");
        using var reference = Surface(factory);
        window.RenderReferenceFrameToSurface(reference);
        var expected = CenterPixel(reference);
        Assert.AreNotEqual(Color.FromRgb(255, 255, 255), expected, "the reference frame drew no fill");
        Assert.AreNotEqual(Color.FromRgb(255, 0, 0), expected, "the reference frame drew the fill without its alpha");
        Assert.AreEqual(expected, CenterPixel(surface), "the retained frame kept the alpha the fill was recorded under");
    }

    [TestMethod]
    public void AlphaSetOutrightUnderAFadedParent_StillFadesInEveryFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        var fill = new OpaqueFill();
        var host = new FadingHost(fill) { Alpha = 0.5 };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = host;
        using var surface = Surface(factory);
        RenderFrames(window, surface);

        using var reference = Surface(factory);
        window.RenderReferenceFrameToSurface(reference);
        var expected = CenterPixel(reference);
        Assert.AreNotEqual(Color.FromRgb(255, 0, 0), expected, "setting the alpha outright cleared the parent's fade");
        Assert.AreEqual(expected, CenterPixel(surface), "the retained frame and the frame drawn straight from the visuals differ");
    }

    private static void RenderFrames(Window window, IRenderSurface surface)
    {
        for (int index = 0; index < 2; index++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }
    }

    private static IRenderSurface Surface(GdiGraphicsFactory factory)
        => factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));

    private static Color CenterPixel(IRenderSurface surface)
    {
        ReadOnlySpan<byte> pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int offset = ((HEIGHT / 2) * WIDTH + WIDTH / 2) * 4;
        return Color.FromRgb(pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    private sealed class DimmedFill : FrameworkElement
    {
        internal int RenderCount { get; private set; }

        protected override Size MeasureContent(Size availableSize) => new(WIDTH, HEIGHT);

        protected override void OnRender(IGraphicsContext context)
        {
            RenderCount++;
            context.Save();
            context.GlobalAlpha *= 0.5f;
            context.FillRectangle(Bounds, Color.FromRgb(255, 0, 0));
            context.Restore();
        }
    }

    private sealed class OpaqueFill : FrameworkElement
    {
        protected override Size MeasureContent(Size availableSize) => new(WIDTH, HEIGHT);

        protected override void OnRender(IGraphicsContext context)
        {
            context.Save();
            context.GlobalAlpha = 1f;
            context.FillRectangle(Bounds, Color.FromRgb(255, 0, 0));
            context.Restore();
        }
    }

    private sealed class FadingHost : FrameworkElement, IVisualTreeHost
    {
        private readonly UIElement _child;
        private double _alpha = 1.0;

        internal FadingHost(UIElement child)
        {
            _child = child;
            AttachChild(child);
        }

        internal double Alpha
        {
            get => _alpha;
            set
            {
                _alpha = value;
                InvalidateVisual();
            }
        }

        internal override double ChildAlpha => _alpha;

        protected override Size MeasureContent(Size availableSize)
        {
            _child.Measure(availableSize);
            return _child.DesiredSize;
        }

        protected override void ArrangeContent(Rect bounds) => _child.Arrange(bounds);

        bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => visitor(_child);
    }
}
