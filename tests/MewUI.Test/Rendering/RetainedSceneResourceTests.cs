using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.Retained;
using Aprillz.MewUI.Resources;

namespace MewUI.Test.Rendering;

/// <summary>
/// Validates that recorded data keeps the resources it draws: an image view recorded into a scene
/// must stay drawable after its owner releases it, and a resource that cannot be preserved must be
/// reported instead of replayed as a stale handle.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedSceneResourceTests
{
    private const int SURFACE_WIDTH = 120;
    private const int SURFACE_HEIGHT = 80;

    private sealed class ImageBox : Control
    {
        internal IImage? Image { get; set; }

        protected override Size MeasureContent(Size availableSize) => new(40, 30);

        protected override void OnRender(IGraphicsContext context)
        {
            if (Image != null)
            {
                context.DrawImage(Image, new Rect(Bounds.X, Bounds.Y, 40, 30));
            }
        }
    }

    [TestMethod]
    public void RecordedImage_SurvivesTheReleaseOfItsOwner()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var source = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(40, 30, 1.0, hasAlpha: false));
        using (var sourceContext = factory.CreateContext(source))
        {
            sourceContext.BeginFrame(source);
            sourceContext.Clear(Color.FromArgb(255, 20, 140, 220));
            sourceContext.EndFrame();
        }

        var view = factory.CreateImageView(source);
        var box = new ImageBox { Image = view };
        var root = new Border { Width = 80, Height = 60, Child = box };
        root.Measure(new Size(SURFACE_WIDTH, SURFACE_HEIGHT));
        root.Arrange(new Rect(0, 0, SURFACE_WIDTH, SURFACE_HEIGHT));

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        byte[] captured = Render(factory, context => capture.Capture(scene, root, context));

        Assert.AreEqual(0, scene.Statistics.RejectedSlotCount, scene.FindNode(box)!.NonRecordableReason);

        // The owner lets its view and the surface go; the recorded lease must keep the pixels alive.
        box.Image = null;
        view.Dispose();
        source.Dispose();

        scene.Statistics.Reset();
        byte[] replayed = Render(factory, context => FrameRenderer.Replay(scene, context));

        Assert.AreEqual(0, scene.Statistics.LiveFallbackCount, "the replay fell back to the live element");
        AssertPixelsEqual(captured, replayed);
    }

    private sealed class Thrower : Control
    {
        internal bool Throws { get; set; }

        protected override Size MeasureContent(Size availableSize) => new(20, 20);

        protected override void OnRender(IGraphicsContext context)
        {
            if (Throws)
            {
                throw new InvalidOperationException("this visual cannot draw");
            }

            context.FillRectangle(Bounds, Color.FromArgb(255, 200, 200, 40));
        }
    }

    [TestMethod]
    public void UpdateThatThrowsAfterAnImageWasReplaced_ReplaysTheOldPictureFromItsOwnLease()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var first = NewPicture(factory, Color.FromArgb(255, 20, 140, 220));
        var firstView = factory.CreateImageView(first);
        var box = new ImageBox { Image = firstView };
        var thrower = new Thrower();
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(box, thrower);
        var root = new Border { Width = 80, Height = 70, Child = stack };
        root.Measure(new Size(SURFACE_WIDTH, SURFACE_HEIGHT));
        root.Arrange(new Rect(0, 0, SURFACE_WIDTH, SURFACE_HEIGHT));

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        byte[] captured = Render(factory, context => capture.Capture(scene, root, context));

        // One update swaps the picture, lets the old one go, and then fails further down the tree. The
        // scene keeps what it had, and what it had still draws the old picture: from the lease the
        // recording took, not from the handle its owner has released.
        var second = NewPicture(factory, Color.FromArgb(255, 220, 60, 40));
        using var secondView = factory.CreateImageView(second);
        box.Image = secondView;
        box.InvalidateVisual();
        firstView.Dispose();
        first.Dispose();
        thrower.Throws = true;
        thrower.InvalidateVisual();
        Assert.ThrowsExactly<InvalidOperationException>(() => Render(factory, context => capture.Capture(scene, root, context)));

        scene.Statistics.Reset();
        byte[] replayed = Render(factory, context => FrameRenderer.Replay(scene, context));
        Assert.AreEqual(0, scene.Statistics.LiveFallbackCount, "the replay fell back to the live element");
        AssertPixelsEqual(captured, replayed);

        second.Dispose();
    }

    private static IRenderSurface NewPicture(GdiGraphicsFactory factory, Color color)
    {
        var picture = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(40, 30, 1.0, hasAlpha: false));
        using var pictureContext = factory.CreateContext(picture);
        pictureContext.BeginFrame(picture);
        pictureContext.Clear(color);
        pictureContext.EndFrame();
        return picture;
    }

    [TestMethod]
    public void UnpreservableResource_IsReportedInsteadOfReplayed()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        // A raw pixel-buffer view is drawable but hands out no lease, which is what the scene has to
        // notice instead of recording a handle it cannot keep alive.
        var source = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(40, 30, 1.0, hasAlpha: false));
        var rawView = factory.CreateImageView((IPixelBufferSource)source);
        Assert.IsFalse(rawView is IRetainableImage, "the raw view unexpectedly supports leases");

        var box = new ImageBox { Image = rawView };
        var root = new Border { Width = 80, Height = 60, Child = box };
        root.Measure(new Size(SURFACE_WIDTH, SURFACE_HEIGHT));
        root.Arrange(new Rect(0, 0, SURFACE_WIDTH, SURFACE_HEIGHT));

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        Render(factory, context => capture.Capture(scene, root, context));

        var node = scene.FindNode(box)!;
        Assert.AreEqual(1, scene.Statistics.RejectedSlotCount);
        Assert.IsNotNull(node.NonRecordableReason);
        Assert.IsNull(node.GetSlot(0), "a slot that could not preserve its resource kept data");

        scene.Statistics.Reset();
        Render(factory, context => FrameRenderer.Replay(scene, context));
        Assert.AreEqual(1, scene.Statistics.LiveFallbackCount, "the rejected slot was not drawn live");

        rawView.Dispose();
        source.Dispose();
    }

    private static byte[] Render(GdiGraphicsFactory factory, Action<IGraphicsContext> draw)
    {
        using var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.White);
            draw(context);
            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        int stride = cpu.StrideBytes;
        var copy = new byte[SURFACE_WIDTH * SURFACE_HEIGHT * 4];
        for (int row = 0; row < SURFACE_HEIGHT; row++)
        {
            pixels.Slice(row * stride, SURFACE_WIDTH * 4).CopyTo(copy.AsSpan(row * SURFACE_WIDTH * 4));
        }

        return copy;
    }

    private static void AssertPixelsEqual(byte[] expected, byte[] actual)
    {
        for (int offset = 0; offset < expected.Length; offset += 4)
        {
            if (expected[offset] != actual[offset] ||
                expected[offset + 1] != actual[offset + 1] ||
                expected[offset + 2] != actual[offset + 2] ||
                expected[offset + 3] != actual[offset + 3])
            {
                int pixelIndex = offset / 4;
                Assert.Fail(
                    $"The replay differs at ({pixelIndex % SURFACE_WIDTH},{pixelIndex / SURFACE_WIDTH}): " +
                    $"expected BGRA=({expected[offset]},{expected[offset + 1]},{expected[offset + 2]},{expected[offset + 3]}) " +
                    $"actual BGRA=({actual[offset]},{actual[offset + 1]},{actual[offset + 2]},{actual[offset + 3]}).");
            }
        }
    }
}
