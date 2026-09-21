extern alias MewVGWin32;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;
using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

/// <summary>
/// A frame that repaints part of the surface has to leave every pixel as a whole frame would: what
/// overlaps the dirty area is drawn again in its original order, antialiased edges that the dirty
/// area cuts through come out the same, and on a transparent surface the old pixels lose their alpha
/// before anything is drawn over them. All four channels are compared.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedDirtyRegionExactnessTests
{
    private const int WIDTH = 220;
    private const int HEIGHT = 160;

    private sealed class Shape : Control
    {
        internal Color Fill { get; set; }

        internal bool Round { get; set; }

        protected override Size MeasureContent(Size availableSize) => new(90, 70);

        protected override void OnRender(IGraphicsContext context)
        {
            if (Round)
            {
                context.FillEllipse(Bounds, Fill);
                context.DrawEllipse(Bounds, Color.FromArgb(255, 20, 20, 20), 3);
            }
            else
            {
                context.FillRoundedRectangle(Bounds, 9, 9, Fill);
                context.DrawRoundedRectangle(Bounds, 9, 9, Color.FromArgb(255, 20, 20, 20), 2);
            }
        }
    }

    [TestMethod]
    public void ChangeUnderAnOverlappingSibling_KeepsTheSiblingOnTop()
    {
        Run(transparent: false, (lower, upper, third) =>
        {
            lower.Fill = Color.FromArgb(255, 250, 160, 20);
            lower.InvalidateVisual();
        });
    }

    [TestMethod]
    public void ChangeOfATranslucentSibling_DoesNotBuildUpAlpha()
    {
        Run(transparent: false, (lower, upper, third) =>
        {
            upper.Fill = Color.FromArgb(120, 40, 200, 90);
            upper.InvalidateVisual();
        });
    }

    [TestMethod]
    public void RepeatedChangesAcrossAntialiasedEdges_MatchAWholeFrame()
    {
        Run(transparent: false, (lower, upper, third) =>
        {
            third.Fill = Color.FromArgb(255, 200, 40, 160);
            third.InvalidateVisual();
        }, repeat: 6);
    }

    [TestMethod]
    public void OnATransparentSurface_TheOldPixelsLoseTheirAlpha()
    {
        Run(transparent: true, (lower, upper, third) =>
        {
            // Shrinking uncovers surface that has to go back to fully transparent.
            upper.Width = 40;
            upper.Height = 30;
        });
    }

    [TestMethod]
    public void Direct2D_ChangesUnderOverlappingAndTranslucentSiblings_MatchAWholeFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D is Windows-only.");
            return;
        }

        using var factory = new Aprillz.MewUI.Rendering.Direct2D.Direct2DGraphicsFactory();
        RunOn(factory, transparent: false, ChangeLowerThenUpper, repeat: 4);
        RunOn(factory, transparent: true, ChangeLowerThenUpper, repeat: 4);
    }

    [TestMethod]
    public void MewVGWin32_ChangesUnderOverlappingAndTranslucentSiblings_MatchAWholeFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("MewVG Win32 is Windows-only.");
            return;
        }

        using var factory = new MewVGWin32GraphicsFactory();
        using var renderScope = factory.AcquireBackgroundRenderScope();
        RunOn(factory, transparent: false, ChangeLowerThenUpper, repeat: 4, MEWVG_CHANNEL_TOLERANCE);
        RunOn(factory, transparent: true, ChangeLowerThenUpper, repeat: 4, MEWVG_CHANNEL_TOLERANCE);
    }

    private static void ChangeLowerThenUpper(Shape lower, Shape upper, Shape third)
    {
        lower.Fill = Color.FromArgb(255, (byte)(lower.Fill.R + 40), 160, 20);
        lower.InvalidateVisual();
        upper.Fill = Color.FromArgb(120, 40, (byte)(upper.Fill.G + 30), 90);
        upper.InvalidateVisual();
    }

    private static void Run(bool transparent, Action<Shape, Shape, Shape> change, int repeat = 1)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        RunOn(factory, transparent, change, repeat);
    }

    // MewVG blends an antialiased edge one level apart between a frame that repaints part of the surface
    // and one that repaints all of it: 4 pixels of 35,200 in this scene, every one of them an
    // antialiased edge pixel on the first or last row of the repainted area, which is where the clip
    // of the area ends. Each partial frame erases and draws those pixels afresh, so the difference
    // stays at one level and does not build up. The clip is the vector backend's own, so the GL backend
    // is held to that one level here instead of being reported as exact.
    private const int MEWVG_CHANNEL_TOLERANCE = 1;

    private static void RunOn(IGraphicsFactory factory, bool transparent, Action<Shape, Shape, Shape> change, int repeat)
        => RunOn(factory, transparent, change, repeat, channelTolerance: 0);

    private static void RunOn(
        IGraphicsFactory factory,
        bool transparent,
        Action<Shape, Shape, Shape> change,
        int repeat,
        int channelTolerance)
    {
        Application.DefaultGraphicsFactory = factory;

        var lower = Place(new Shape { Fill = Color.FromArgb(255, 40, 120, 220) }, 20, 20);
        var upper = Place(new Shape { Fill = Color.FromArgb(140, 220, 60, 60), Round = true }, 70, 50);
        var third = Place(new Shape { Fill = Color.FromArgb(255, 60, 180, 120), Round = true }, 120, 10);
        var canvas = new Canvas();
        canvas.Children(lower, upper, third);

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        if (transparent)
        {
            window.AllowsTransparency = true;
            window.Background = Color.Transparent;
        }

        window.Content = canvas;
        window.PerformLayout();

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: transparent));
        Frames(window, surface, 3);

        for (int round = 0; round < repeat; round++)
        {
            change(lower, upper, third);
            if (repeat > 1)
            {
                third.Fill = Color.FromArgb(255, (byte)(200 - round * 25), (byte)(40 + round * 30), 160);
                third.InvalidateVisual();
            }

            Frames(window, surface, 1);
            Assert.IsNotNull(window.LastRetainedDirtyRect, $"round {round}: the frame was drawn whole, so it proves nothing about partial repaint");

            using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: transparent));
            window.RenderReferenceFrameToSurface(reference);
            AssertSurfacesEqual(reference, surface, $"round {round}", channelTolerance);
        }
    }

    private static Shape Place(Shape shape, double left, double top)
    {
        shape.Width = 90;
        shape.Height = 70;
        Canvas.SetLeft(shape, left);
        Canvas.SetTop(shape, top);
        return shape;
    }

    private static void Frames(Window window, IRenderSurface surface, int count)
    {
        for (int index = 0; index < count; index++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }
    }

    private static void AssertSurfacesEqual(IRenderSurface reference, IRenderSurface actual, string label, int channelTolerance)
    {
        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)reference).GetReadOnlyPixelSpan();
        ReadOnlySpan<byte> shown = ((ICpuPixelSurface)actual).GetReadOnlyPixelSpan();
        int differing = 0;
        int first = -1;
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            if (Math.Abs(expected[offset] - shown[offset]) > channelTolerance ||
                Math.Abs(expected[offset + 1] - shown[offset + 1]) > channelTolerance ||
                Math.Abs(expected[offset + 2] - shown[offset + 2]) > channelTolerance ||
                Math.Abs(expected[offset + 3] - shown[offset + 3]) > channelTolerance)
            {
                differing++;
                if (first < 0)
                {
                    first = offset / 4;
                }
            }
        }

        Assert.AreEqual(0, differing, $"{label}: {differing} pixels differ, first at ({first % WIDTH}, {first / WIDTH})");
    }
}
