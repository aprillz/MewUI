using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.Retained;

namespace MewUI.Test.Rendering;

/// <summary>
/// A scroll moves what it scrolls; it does not change how any of it is drawn. The criterion comes from
/// the first retained implementation, which asserted that a scrolled content reports a bounds change
/// and no content change: the scene has to place the recording somewhere else instead of taking it
/// again. Without that, every notch of a wheel re-records the whole scrolled subtree.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedScrollReuseTests
{
    private const int SURFACE_WIDTH = 160;
    private const int SURFACE_HEIGHT = 120;
    private const int LEAF_COUNT = 8;
    private const double LEAF_HEIGHT = 30;

    private sealed class Leaf : Control
    {
        internal int RecordCount { get; private set; }

        internal Color Fill { get; set; } = Color.FromArgb(255, 200, 60, 60);

        protected override Size MeasureContent(Size availableSize) => new(60, LEAF_HEIGHT);

        protected override void OnRender(IGraphicsContext context)
        {
            RecordCount++;
            context.FillRectangle(Bounds, Fill);
        }
    }

    /// <summary>Replaces the transform outright, the way drawing code does when it has no Save to lean on.</summary>
    private sealed class AbsoluteTransformLeaf : Control
    {
        protected override Size MeasureContent(Size availableSize) => new(60, LEAF_HEIGHT);

        protected override void OnRender(IGraphicsContext context)
        {
            var ambient = context.GetTransform();
            context.SetTransform(System.Numerics.Matrix3x2.CreateTranslation(6, 0) * ambient);
            context.FillRectangle(Bounds, Color.FromArgb(255, 40, 160, 90));
            context.SetTransform(ambient);
            context.ResetClip();
            context.FillRectangle(new Rect(Bounds.X, Bounds.Y, 4, Bounds.Height), Color.FromArgb(255, 200, 40, 40));
        }
    }

    [TestMethod]
    public void AScroll_KeepsContentThatSetsItsTransformOutrightInPlace()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var content = new StackPanel { Orientation = Orientation.Vertical };
        for (int index = 0; index < LEAF_COUNT; index++)
        {
            content.Children(new AbsoluteTransformLeaf { Height = LEAF_HEIGHT });
        }

        var scroll = new ScrollViewer { VerticalScroll = ScrollMode.Visible, Content = content };
        var root = new Border { Child = scroll };
        Layout(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        var dirty = new RenderDirtyRegistry();
        Capture(factory, scene, capture, root, dirty);

        scroll.SetScrollOffsets(0, LEAF_HEIGHT);
        Layout(root);
        Capture(factory, scene, capture, root, dirty);

        byte[] replayed = RenderSurface(factory, context => FrameRenderer.Replay(scene, context));
        byte[] immediate = RenderSurface(factory, context => root.Render(context));
        AssertPixelsEqual(immediate, replayed);
    }

    [TestMethod]
    public void AScroll_MovesTheContentWithoutRecordingItAgain()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var leaves = new Leaf[LEAF_COUNT];
        var content = new StackPanel { Orientation = Orientation.Vertical };
        for (int index = 0; index < leaves.Length; index++)
        {
            leaves[index] = new Leaf
            {
                Height = LEAF_HEIGHT,
                Fill = Color.FromArgb(255, (byte)(40 + index * 25), 90, (byte)(200 - index * 20)),
            };
            content.Children(leaves[index]);
        }

        var scroll = new ScrollViewer { VerticalScroll = ScrollMode.Visible, Content = content };
        var root = new Border { Child = scroll };
        Layout(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        var dirty = new RenderDirtyRegistry();

        Capture(factory, scene, capture, root, dirty);
        int recordsAfterFirstFrame = TotalRecords(leaves);

        scroll.SetScrollOffsets(0, LEAF_HEIGHT);
        Layout(root);
        Capture(factory, scene, capture, root, dirty);

        Console.Error.WriteLine($"leaf records after first frame: {recordsAfterFirstFrame}, leaf0 bounds {leaves[0].Bounds}, leaf7 bounds {leaves[7].Bounds}, scroll offset {scroll.VerticalOffset}");
        int recordsAfterScroll = TotalRecords(leaves) - recordsAfterFirstFrame;
        Console.Error.WriteLine($"leaf records added by the scroll: {recordsAfterScroll}");
        Assert.AreEqual(
            0,
            recordsAfterScroll,
            $"scrolling re-recorded {recordsAfterScroll} leaf drawings; the scene took the content again " +
            "instead of placing the recording it already had");

        // Reusing a recording is only worth anything if it lands where the content now is.
        byte[] replayed = RenderSurface(factory, context => FrameRenderer.Replay(scene, context));
        byte[] immediate = RenderSurface(factory, context => root.Render(context));
        AssertPixelsEqual(immediate, replayed);
    }

    private static byte[] RenderSurface(GdiGraphicsFactory factory, Action<IGraphicsContext> draw)
    {
        using var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));
        using var context = factory.CreateContext(surface);
        context.BeginFrame(surface);
        context.Clear(Color.FromArgb(255, 250, 250, 250));
        draw(context);
        context.EndFrame();

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
        int differing = 0;
        int firstOffset = -1;
        for (int offset = 0; offset < expected.Length; offset += 4)
        {
            if (expected[offset] != actual[offset] ||
                expected[offset + 1] != actual[offset + 1] ||
                expected[offset + 2] != actual[offset + 2] ||
                expected[offset + 3] != actual[offset + 3])
            {
                differing++;
                if (firstOffset < 0)
                {
                    firstOffset = offset;
                }
            }
        }

        int firstPixel = firstOffset / 4;
        Assert.AreEqual(
            0,
            differing,
            $"the replayed scroll differs from an immediate render at {differing} pixels, " +
            $"first at ({firstPixel % SURFACE_WIDTH}, {firstPixel / SURFACE_WIDTH})");
    }

    private static int TotalRecords(Leaf[] leaves)
    {
        int total = 0;
        foreach (var leaf in leaves)
        {
            total += leaf.RecordCount;
        }

        return total;
    }

    private static void Capture(
        GdiGraphicsFactory factory,
        RenderScene scene,
        SceneCapture capture,
        UIElement root,
        RenderDirtyRegistry dirty)
    {
        using var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));
        using var context = factory.CreateContext(surface);
        context.BeginFrame(surface);
        var recorder = new RenderDataRecorder(context) { SuppressDrawing = true };
        scene.ResetDamage();
        capture.Capture(scene, root, recorder, dirty);
        context.EndFrame();
    }

    private static void Layout(UIElement root)
    {
        root.Measure(new Size(SURFACE_WIDTH, SURFACE_HEIGHT));
        root.Arrange(new Rect(0, 0, SURFACE_WIDTH, SURFACE_HEIGHT));
    }
}
