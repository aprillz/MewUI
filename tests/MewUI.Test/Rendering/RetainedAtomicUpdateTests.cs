using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.Retained;

namespace MewUI.Test.Rendering;

/// <summary>
/// A scene update either lands whole or leaves the scene exactly as it was. Each case breaks an update
/// after it has already visited, created and re-parented nodes, then requires the scene to still hold
/// the root, the node count and the pixels of the last update that succeeded.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedAtomicUpdateTests
{
    private const int WIDTH = 160;
    private const int HEIGHT = 120;

    private sealed class Leaf : Control
    {
        internal Color Fill { get; set; }

        internal bool Throws { get; set; }

        protected override Size MeasureContent(Size availableSize) => new(60, 24);

        protected override void OnRender(IGraphicsContext context)
        {
            if (Throws)
            {
                throw new InvalidOperationException("this visual cannot draw");
            }

            context.FillRectangle(Bounds, Fill);
        }
    }

    [TestMethod]
    public void UpdateThatThrowsAfterReparentingAVisual_LeavesTheSceneAsItWas()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var moved = new Leaf { Height = 24, Fill = Color.FromArgb(255, 30, 120, 200) };
        var stays = new Leaf { Height = 24, Fill = Color.FromArgb(255, 200, 120, 30) };
        var first = new StackPanel { Orientation = Orientation.Vertical };
        first.Children(moved, stays);
        var second = new StackPanel { Orientation = Orientation.Vertical };
        var root = new StackPanel { Orientation = Orientation.Vertical };
        root.Children(first, second);
        Layout(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        Capture(factory, scene, capture, root);

        var rootBefore = scene.Root;
        int nodesBefore = scene.NodeCount;
        byte[] pixelsBefore = Replay(factory, scene);

        // The update re-parents a visual, adds a new one, and only then reaches the visual that throws.
        first.Remove(moved);
        second.Children(moved);
        second.Children(new Leaf { Height = 24, Fill = Color.FromArgb(255, 90, 200, 90) });
        second.Children(new Leaf { Height = 24, Throws = true });
        Layout(root);

        Assert.ThrowsExactly<InvalidOperationException>(() => Capture(factory, scene, capture, root));

        Assert.AreSame(rootBefore, scene.Root, "the failed update replaced the scene root");
        Assert.AreEqual(nodesBefore, scene.NodeCount, "the failed update left nodes it created in the scene");
        AssertPixelsEqual(pixelsBefore, Replay(factory, scene), "after the failed update");
    }

    [TestMethod]
    public void UpdateAfterAFailedOne_RecordsWhatTheFailedOneDidNot()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var changing = new Leaf { Height = 24, Fill = Color.FromArgb(255, 30, 120, 200) };
        var breaking = new Leaf { Height = 24, Fill = Color.FromArgb(255, 200, 120, 30) };
        var root = new StackPanel { Orientation = Orientation.Vertical };
        root.Children(changing, breaking);
        Layout(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        Capture(factory, scene, capture, root);

        changing.Fill = Color.FromArgb(255, 10, 220, 90);
        changing.InvalidateVisual();
        breaking.Throws = true;
        breaking.InvalidateVisual();
        Assert.ThrowsExactly<InvalidOperationException>(() => Capture(factory, scene, capture, root));

        // The change the failed update had already recorded must not be lost with it.
        breaking.Throws = false;
        breaking.InvalidateVisual();
        Capture(factory, scene, capture, root);

        byte[] replayed = Replay(factory, scene);
        byte[] reference = Render(factory, context => root.Render(context));
        AssertPixelsEqual(reference, replayed, "after the update that followed the failed one");
    }

    private sealed class Invalidator : Control
    {
        internal Leaf? Target { get; set; }

        internal Color NextFill { get; set; }

        internal bool Armed { get; set; }

        protected override Size MeasureContent(Size availableSize) => new(60, 24);

        protected override void OnRender(IGraphicsContext context)
        {
            context.FillRectangle(Bounds, Color.FromArgb(255, 120, 120, 120));
            if (Armed && Target != null)
            {
                // The target was already recorded earlier in this pass, so this change belongs to the next.
                Armed = false;
                Target.Fill = NextFill;
                Target.InvalidateVisual();
            }
        }
    }

    [TestMethod]
    public void InvalidationRaisedWhileRecording_IsServedByTheNextUpdate()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var target = new Leaf { Height = 24, Fill = Color.FromArgb(255, 30, 120, 200) };
        var invalidator = new Invalidator { Height = 24, Target = target, NextFill = Color.FromArgb(255, 220, 40, 40) };
        var root = new StackPanel { Orientation = Orientation.Vertical };
        root.Children(target, invalidator);
        Layout(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        Capture(factory, scene, capture, root);

        invalidator.Armed = true;
        invalidator.InvalidateVisual();
        Capture(factory, scene, capture, root);
        Capture(factory, scene, capture, root);

        AssertPixelsEqual(Render(factory, context => root.Render(context)), Replay(factory, scene), "after the update that followed the recording");
    }

    [TestMethod]
    public void VisualRemovedAndAttachedAgain_IsDrawnWhereItNowStands()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var wanderer = new Leaf { Height = 24, Fill = Color.FromArgb(255, 30, 120, 200) };
        var first = new StackPanel { Orientation = Orientation.Vertical };
        first.Children(wanderer, new Leaf { Height = 24, Fill = Color.FromArgb(255, 200, 120, 30) });
        var second = new StackPanel { Orientation = Orientation.Vertical };
        second.Children(new Leaf { Height = 24, Fill = Color.FromArgb(255, 90, 200, 90) });
        var root = new StackPanel { Orientation = Orientation.Vertical };
        root.Children(first, second);
        Layout(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        Capture(factory, scene, capture, root);

        first.Remove(wanderer);
        Layout(root);
        Capture(factory, scene, capture, root);
        AssertPixelsEqual(Render(factory, context => root.Render(context)), Replay(factory, scene), "after the removal");

        second.Children(wanderer);
        Layout(root);
        Capture(factory, scene, capture, root);
        AssertPixelsEqual(Render(factory, context => root.Render(context)), Replay(factory, scene), "after attaching it under another parent");

        second.Remove(wanderer);
        first.Children(wanderer);
        Layout(root);
        Capture(factory, scene, capture, root);
        AssertPixelsEqual(Render(factory, context => root.Render(context)), Replay(factory, scene), "after moving it back in one update");
    }

    private static void Capture(GdiGraphicsFactory factory, RenderScene scene, SceneCapture capture, UIElement root)
    {
        using var surface = CreateSurface(factory);
        using var context = factory.CreateContext(surface);
        context.BeginFrame(surface);
        try
        {
            var recorder = new RenderDataRecorder(context) { SuppressDrawing = true };
            scene.ResetDamage();
            capture.Capture(scene, root, recorder);
        }
        finally
        {
            context.EndFrame();
        }
    }

    private static byte[] Replay(GdiGraphicsFactory factory, RenderScene scene)
        => Render(factory, context => FrameRenderer.Replay(scene, context));

    private static byte[] Render(GdiGraphicsFactory factory, Action<IGraphicsContext> draw)
    {
        using var surface = CreateSurface(factory);
        using var context = factory.CreateContext(surface);
        context.BeginFrame(surface);
        context.Clear(Color.FromArgb(255, 250, 250, 250));
        draw(context);
        context.EndFrame();

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        int stride = cpu.StrideBytes;
        var copy = new byte[WIDTH * HEIGHT * 4];
        for (int row = 0; row < HEIGHT; row++)
        {
            pixels.Slice(row * stride, WIDTH * 4).CopyTo(copy.AsSpan(row * WIDTH * 4));
        }

        return copy;
    }

    private static IRenderSurface CreateSurface(GdiGraphicsFactory factory)
        => factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));

    private static void Layout(UIElement root)
    {
        root.Measure(new Size(WIDTH, HEIGHT));
        root.Arrange(new Rect(0, 0, WIDTH, HEIGHT));
    }

    private static void AssertPixelsEqual(byte[] expected, byte[] actual, string label)
    {
        int differing = 0;
        int first = -1;
        for (int offset = 0; offset < expected.Length; offset += 4)
        {
            if (expected[offset] != actual[offset] ||
                expected[offset + 1] != actual[offset + 1] ||
                expected[offset + 2] != actual[offset + 2])
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
