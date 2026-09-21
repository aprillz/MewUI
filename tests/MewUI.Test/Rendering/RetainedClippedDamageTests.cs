using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// What a visual inks is what reaches the surface, not what it asked to draw. A canvas zoomed in on a
/// picture draws far past its own box and clips it away; a scrolled list holds rows that lie outside
/// the viewport. Counting the clipped-away part as damage makes a small change repaint the window.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedClippedDamageTests
{
    private const int WIDTH = 400;
    private const int HEIGHT = 300;

    /// <summary>Clips to its box and then draws ten times past it, the way a zoomed picture does.</summary>
    private sealed class ZoomedPicture : Control
    {
        internal Color Fill { get; set; } = Color.FromArgb(255, 40, 160, 120);

        protected override Size MeasureContent(Size availableSize) => new(100, 80);

        protected override void OnRender(IGraphicsContext context)
        {
            context.Save();
            context.SetClip(Bounds);
            context.FillRectangle(new Rect(Bounds.X - 1000, Bounds.Y - 1000, 3000, 3000), Fill);
            context.Restore();
        }
    }

    private sealed class Tile : Control
    {
        internal Color Fill { get; set; }

        protected override Size MeasureContent(Size availableSize) => new(60, 24);

        protected override void OnRender(IGraphicsContext context) => context.FillRectangle(Bounds, Fill);
    }

    [TestMethod]
    public void ChangeOfAVisualThatDrawsPastItsClip_DamagesOnlyWhatTheClipLetsThrough()
    {
        if (!TryStart(out var factory))
        {
            return;
        }

        using (factory)
        {
            var picture = new ZoomedPicture { Width = 100, Height = 80, HorizontalAlignment = HorizontalAlignment.Left };
            var stack = new StackPanel { Orientation = Orientation.Vertical };
            stack.Children(picture);
            for (int index = 0; index < 8; index++)
            {
                stack.Children(new Tile { Height = 24, Fill = Color.FromArgb(255, (byte)(60 + index * 20), 120, 200) });
            }

            var window = Show(stack);
            using var surface = Surface(factory);
            Frames(window, surface, 3);

            picture.Fill = Color.FromArgb(255, 220, 60, 60);
            picture.InvalidateVisual();
            window.RetainedStatistics!.Reset();
            Frames(window, surface, 1);

            var damage = window.LastRetainedDamage;
            Assert.IsNotNull(damage, "the frame was drawn whole");
            Assert.IsTrue(
                damage.Value.Width <= picture.Bounds.Width + 2 && damage.Value.Height <= picture.Bounds.Height + 2,
                $"the damage {damage} reaches past the clipped picture at {picture.Bounds}");
            Assert.IsLessThanOrEqualTo(
                3,
                window.RetainedStatistics!.ContentReplayCount,
                $"repainting the picture replayed {window.RetainedStatistics!.ContentReplayCount} recordings");
        }
    }

    [TestMethod]
    public void ScrollingAList_DamagesOnlyItsViewport()
    {
        if (!TryStart(out var factory))
        {
            return;
        }

        using (factory)
        {
            var rows = new StackPanel { Orientation = Orientation.Vertical };
            for (int index = 0; index < 60; index++)
            {
                rows.Children(new Tile { Height = 24, Fill = Color.FromArgb(255, (byte)(40 + index * 3), 150, (byte)(220 - index * 3)) });
            }

            var scroll = new ScrollViewer { VerticalScroll = ScrollMode.Visible, Content = rows, Width = 150, Height = 120, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
            var side = new Tile { Fill = Color.FromArgb(255, 200, 80, 40), Width = 200, Height = 250, HorizontalAlignment = HorizontalAlignment.Right };
            var grid = new Grid();
            grid.Children(scroll, side);

            var window = Show(grid);
            using var surface = Surface(factory);
            Frames(window, surface, 3);

            scroll.SetScrollOffsets(0, 48);
            Frames(window, surface, 1);

            var damage = window.LastRetainedDamage;
            Assert.IsNotNull(damage, "scrolling a list that covers a small part of the window drew the whole frame");
            Assert.IsTrue(
                damage.Value.Bottom <= scroll.Bounds.Bottom + 1 && damage.Value.Right <= scroll.Bounds.Right + 1,
                $"the damage {damage} reaches past the scroll viewer at {scroll.Bounds}: rows outside the viewport were counted");
        }
    }

    /// <summary>Draws one child under a transform it sets itself, the way a zoom and pan host does.</summary>
    private sealed class ZoomHost : FrameworkElement, IVisualTreeHost
    {
        private readonly UIElement _child;

        internal ZoomHost(UIElement child)
        {
            _child = child;
            SkipViewportCull = true;
            child.SkipViewportCull = true;
            AttachChild(child);
        }

        internal double Zoom { get; set; } = 4;

        protected override Size MeasureContent(Size availableSize)
        {
            _child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return new Size(_child.DesiredSize.Width * Zoom, _child.DesiredSize.Height * Zoom);
        }

        protected override void ArrangeContent(Rect bounds)
            => _child.Arrange(new Rect(0, 0, _child.DesiredSize.Width, _child.DesiredSize.Height));

        protected override void RenderSubtree(IGraphicsContext context)
        {
            context.Save();
            var transform = System.Numerics.Matrix3x2.CreateScale((float)Zoom)
                * System.Numerics.Matrix3x2.CreateTranslation((float)Bounds.X, (float)Bounds.Y)
                * context.GetTransform();
            context.SetTransform(transform);
            _child.Render(context);
            context.Restore();
        }

        bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => visitor(_child);
    }

    /// <summary>Fills its box; its size is what the zoom host multiplies.</summary>
    private sealed class Picture : FrameworkElement
    {
        protected override Size MeasureContent(Size availableSize) => new(300, 300);

        protected override void OnRender(IGraphicsContext context)
            => context.FillRectangle(new Rect(0, 0, ActualWidth, ActualHeight), Color.FromArgb(255, 90, 60, 200));
    }

    [TestMethod]
    public void PanningAZoomedPicture_DamagesOnlyItsViewport()
    {
        if (!TryStart(out var factory))
        {
            return;
        }

        using (factory)
        {
            ScrollViewer preview = null!;
            var body = new Grid()
                .Columns("90,*")
                .Spacing(6)
                .Children(
                    new GroupBox()
                        .Header("Files")
                        .Content(new ListBox().Items(new[] { "a.svg", "b.svg", "c.svg", "d.svg" })),
                    new SplitPanel()
                        .Column(1)
                        .Horizontal()
                        .SplitterThickness(8)
                        .First(new GroupBox().Header("Source").Content(new MultiLineTextBox { Text = "<svg>\n</svg>" }))
                        .Second(
                            new GroupBox()
                                .Header("Vector")
                                .Content(
                                    new DockPanel().Children(
                                        new Button().DockTop().Content("Reset Zoom"),
                                        new Border()
                                            .BorderThickness(1)
                                            .Child(
                                                new ScrollViewer()
                                                    .Ref(out preview)
                                                    .HorizontalScroll(ScrollMode.Auto)
                                                    .VerticalScroll(ScrollMode.Auto)
                                                    .Content(new ZoomHost(new Picture())))))));

            var tabs = new TabControl().TabItems(new TabItem().Header("Issues").Content(body));

            var window = Show(tabs);
            using var surface = Surface(factory);
            Frames(window, surface, 3);

            window.RetainedStatistics!.Reset();
            preview.SetScrollOffsets(40, 60);
            Frames(window, surface, 1);

            var damage = window.LastRetainedDamage;
            Assert.IsNotNull(damage, $"panning the picture drew the whole frame: {window.LastWholeFrameReason}");
            Assert.IsTrue(
                damage.Value.X >= preview.Bounds.X - 1 && damage.Value.Y >= preview.Bounds.Y - 1 &&
                damage.Value.Right <= preview.Bounds.Right + 1 && damage.Value.Bottom <= preview.Bounds.Bottom + 1,
                $"the damage {damage} reaches past the preview at {preview.Bounds}");
            Assert.IsLessThanOrEqualTo(
                4,
                window.RetainedStatistics!.ContentRecordCount,
                $"panning recorded {window.RetainedStatistics!.ContentRecordCount} slots again");
            AssertMatchesReference(factory, window, surface, "after panning");

            // Laying the window out again without anything having changed draws what is already there.
            tabs.InvalidateArrange();
            Frames(window, surface, 1);
            Assert.AreEqual(default(Rect), window.LastRetainedDamage, $"arranging again damaged {window.LastRetainedDamage} ({window.LastWholeFrameReason})");
            AssertMatchesReference(factory, window, surface, "after arranging again");
        }
    }

    private static void AssertMatchesReference(GdiGraphicsFactory factory, Window window, IRenderSurface actual, string label)
    {
        using var reference = Surface(factory);
        window.RenderReferenceFrameToSurface(reference);
        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)reference).GetReadOnlyPixelSpan();
        ReadOnlySpan<byte> shown = ((ICpuPixelSurface)actual).GetReadOnlyPixelSpan();
        int differing = 0;
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            if (expected[offset] != shown[offset] || expected[offset + 1] != shown[offset + 1] || expected[offset + 2] != shown[offset + 2])
            {
                differing++;
            }
        }

        Assert.AreEqual(0, differing, $"{label}: {differing} pixels differ from a frame drawn straight from the visuals");
    }

    private static bool TryStart(out GdiGraphicsFactory factory)
    {
        factory = null!;
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return false;
        }

        factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        return true;
    }

    private static Window Show(UIElement content)
    {
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = content;
        window.PerformLayout();
        return window;
    }

    private static IRenderSurface Surface(GdiGraphicsFactory factory)
        => factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));

    private static void Frames(Window window, IRenderSurface surface, int count)
    {
        for (int index = 0; index < count; index++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }
    }
}
