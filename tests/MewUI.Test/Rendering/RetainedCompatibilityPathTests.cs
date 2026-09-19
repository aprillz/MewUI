using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Resources;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A visual the scene cannot record costs only itself. It is drawn live where it stands, the visuals
/// around it keep their recordings, and a frame that repaints part of the surface still comes out equal
/// to a frame drawn straight from the visuals. The same holds when the device or the scale changes
/// under a scene that already has recordings.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedCompatibilityPathTests
{
    private const int WIDTH = 200;
    private const int HEIGHT = 160;

    private sealed class Tile : Control
    {
        internal int RecordCount { get; private set; }

        internal Color Fill { get; set; }

        protected override Size MeasureContent(Size availableSize) => new(80, 30);

        protected override void OnRender(IGraphicsContext context)
        {
            RecordCount++;
            context.FillRectangle(Bounds, Fill);
        }
    }

    /// <summary>Draws an image that hands out no lease, so its slot can never be recorded.</summary>
    private sealed class UnrecordableTile : Control
    {
        internal IImage? Image { get; set; }

        internal Color Fill { get; set; }

        protected override Size MeasureContent(Size availableSize) => new(80, 30);

        protected override void OnRender(IGraphicsContext context)
        {
            context.FillRectangle(Bounds, Fill);
            if (Image != null)
            {
                context.DrawImage(Image, new Rect(Bounds.X + 4, Bounds.Y + 4, 20, 20));
            }
        }
    }

    [TestMethod]
    public void UnrecordableVisual_CostsOnlyItself()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        using var imageSource = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(20, 20, 1.0, hasAlpha: false));
        using var rawView = factory.CreateImageView((IPixelBufferSource)imageSource);

        var above = new Tile { Height = 30, Fill = Color.FromArgb(255, 30, 120, 200) };
        var unrecordable = new UnrecordableTile { Height = 30, Image = rawView, Fill = Color.FromArgb(255, 90, 90, 90) };
        var below = new Tile { Height = 30, Fill = Color.FromArgb(255, 200, 120, 30) };
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(above, unrecordable, below);

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = stack;
        window.PerformLayout();

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        Frames(window, surface, 3);

        // The unrecordable visual changes, and then a recorded neighbour does. A reference frame draws
        // every visual, so the counts are taken around the scene-driven frames only.
        unrecordable.Fill = Color.FromArgb(255, 160, 40, 160);
        unrecordable.InvalidateVisual();
        int aboveRecords = above.RecordCount;
        int belowRecords = below.RecordCount;
        Frames(window, surface, 1);
        Assert.AreEqual(aboveRecords, above.RecordCount, "the visual above the unrecordable one was recorded again");
        Assert.AreEqual(belowRecords, below.RecordCount, "the visual below the unrecordable one was recorded again");
        AssertMatchesReference(factory, window, surface, "after the unrecordable visual changed");

        below.Fill = Color.FromArgb(255, 20, 200, 120);
        below.InvalidateVisual();
        aboveRecords = above.RecordCount;
        belowRecords = below.RecordCount;
        Frames(window, surface, 1);
        Assert.AreEqual(aboveRecords, above.RecordCount, "the visual above was recorded when its neighbour changed");
        Assert.AreEqual(belowRecords + 1, below.RecordCount, "the neighbour was recorded more than its one change");
        AssertMatchesReference(factory, window, surface, "after its neighbour changed");
    }

    [TestMethod]
    public void DeviceLoss_RecordsEverythingAgainAndMatchesTheReferenceFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var first = new Tile { Height = 30, Fill = Color.FromArgb(255, 30, 120, 200) };
        var second = new Tile { Height = 30, Fill = Color.FromArgb(255, 200, 120, 30) };
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(first, second);
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = stack;
        window.PerformLayout();

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        Frames(window, surface, 3);

        int firstRecords = first.RecordCount;
        int secondRecords = second.RecordCount;
        typeof(Window).GetMethod("OnGpuInteropInvalidated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(window, [null, new GpuInteropInvalidatedEventArgs(GpuInteropInvalidationReason.DeviceLost)]);
        Frames(window, surface, 1);

        // Recordings belong to the device they were taken on, so none of them may be replayed after it.
        Assert.AreEqual(firstRecords + 1, first.RecordCount, "a recording from the lost device was replayed");
        Assert.AreEqual(secondRecords + 1, second.RecordCount, "a recording from the lost device was replayed");
        AssertMatchesReference(factory, window, surface, "after the device was lost");
    }

    [TestMethod]
    public void ScaleChange_UnderAnExistingScene_MatchesTheReferenceFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(
            new Border
            {
                Height = 36,
                Margin = new Thickness(5, 3, 5, 3),
                Background = Color.FromArgb(255, 40, 160, 120),
                Child = new TextBlock { Text = "scaled text", Margin = new Thickness(6) },
            },
            new Tile { Height = 30, Fill = Color.FromArgb(255, 200, 120, 30) });

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = stack;
        window.PerformLayout();

        using (var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false)))
        {
            Frames(window, surface, 2);
        }

        window.SetDpi(144);
        window.PerformLayout();
        using var scaled = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH * 3 / 2, HEIGHT * 3 / 2, 1.5, hasAlpha: false));
        Frames(window, scaled, 2);

        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH * 3 / 2, HEIGHT * 3 / 2, 1.5, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        AssertSurfacesEqual(reference, scaled, "after the scale changed");
    }

    private static void Frames(Window window, IRenderSurface surface, int count)
    {
        for (int index = 0; index < count; index++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }
    }

    private static void AssertMatchesReference(GdiGraphicsFactory factory, Window window, IRenderSurface actual, string label)
    {
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        AssertSurfacesEqual(reference, actual, label);
    }

    private static void AssertSurfacesEqual(IRenderSurface reference, IRenderSurface actual, string label)
    {
        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)reference).GetReadOnlyPixelSpan();
        ReadOnlySpan<byte> shown = ((ICpuPixelSurface)actual).GetReadOnlyPixelSpan();
        int width = Math.Max(1, reference.PixelWidth);
        int differing = 0;
        int first = -1;
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            if (expected[offset] != shown[offset] ||
                expected[offset + 1] != shown[offset + 1] ||
                expected[offset + 2] != shown[offset + 2])
            {
                differing++;
                if (first < 0)
                {
                    first = offset / 4;
                }
            }
        }

        Assert.AreEqual(0, differing, $"{label}: {differing} pixels differ, first at ({first % width}, {first / width})");
    }
}
