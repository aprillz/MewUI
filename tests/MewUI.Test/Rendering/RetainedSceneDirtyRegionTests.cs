using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.Retained;

namespace MewUI.Test.Rendering;

/// <summary>
/// Validates the dirty region the scene reports: it must cover what an update changed, stay away from
/// what it did not, and a replay restricted to it must end up at the same pixels as a full replay.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedSceneDirtyRegionTests
{
    private const int SURFACE_WIDTH = 200;
    private const int SURFACE_HEIGHT = 160;

    private sealed class FillBox : Control
    {
        internal Color Fill { get; set; } = Color.FromArgb(255, 200, 60, 60);

        protected override Size MeasureContent(Size availableSize) => new(60, 30);

        protected override void OnRender(IGraphicsContext context) => context.FillRectangle(Bounds, Fill);
    }

    [TestMethod]
    public void DirtyReplay_EndsAtTheSamePixelsAsAFullReplay()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var top = new FillBox { Fill = Color.FromArgb(255, 30, 120, 200) };
        var bottom = new FillBox { Fill = Color.FromArgb(255, 200, 120, 30) };
        var root = BuildRoot(top, bottom);
        Layout(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        var registry = new RenderDirtyRegistry();

        using var live = CreateSurface(factory);
        DrawInto(factory, live, context => capture.Capture(scene, root, context, registry), clear: true);

        top.Fill = Color.FromArgb(255, 10, 200, 90);
        top.InvalidateVisual();

        scene.ResetDirtyRegion();
        using (var scratch = CreateSurface(factory))
        {
            DrawInto(factory, scratch, context => capture.Capture(scene, root, context, registry), clear: true);
        }

        Assert.IsTrue(scene.HasDirtyRegion, "the content change reported no dirty region");
        Assert.IsFalse(scene.IsFullyDirty, "a single item change escalated to the whole surface");

        var dirtyRect = scene.DirtyBounds;
        Assert.IsFalse(
            dirtyRect.IntersectsWith(bottom.Bounds),
            $"the dirty region {dirtyRect} reaches the item that did not change at {bottom.Bounds}");

        using var dirty = CreateSurface(factory);
        DrawInto(factory, dirty, context => FrameRenderer.Replay(scene, context, dirtyRect), clear: true);

        using var full = CreateSurface(factory);
        DrawInto(factory, full, context => FrameRenderer.Replay(scene, context), clear: true);

        byte[] dirtyPixels = ReadPixels(dirty);
        byte[] fullPixels = ReadPixels(full);

        // Inside the dirty region the partial replay must reach the same pixels as the full replay, and
        // outside it must not have drawn at all, which is what lets the previous frame stand.
        for (int row = 0; row < SURFACE_HEIGHT; row++)
        {
            for (int column = 0; column < SURFACE_WIDTH; column++)
            {
                bool inside = dirtyRect.Contains(new Point(column + 0.5, row + 0.5));
                int offset = (row * SURFACE_WIDTH + column) * 4;
                if (inside)
                {
                    AssertPixelEqual(fullPixels, dirtyPixels, offset, "inside the dirty region");
                }
                else
                {
                    AssertPixelIsWhite(dirtyPixels, offset);
                }
            }
        }
    }

    [TestMethod]
    public void RedrawingOnlyTheDirtyRegion_LeavesTheSurfaceAsAFullReplayWould()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var top = new FillBox { Fill = Color.FromArgb(255, 30, 120, 200) };
        var bottom = new FillBox { Fill = Color.FromArgb(255, 200, 120, 30) };
        var root = BuildRoot(top, bottom);
        Layout(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        var registry = new RenderDirtyRegistry();

        using var live = CreateSurface(factory);
        var persistent = live as IPersistentFrameSurface;
        Assert.IsNotNull(persistent, "the surface cannot keep its contents, so partial repaint is untestable");

        UpdateScene(factory, live, capture, scene, root, registry);
        DrawInto(factory, live, context => FrameRenderer.Replay(scene, context), clear: true);

        top.Fill = Color.FromArgb(255, 10, 200, 90);
        top.InvalidateVisual();

        scene.ResetDirtyRegion();
        UpdateScene(factory, live, capture, scene, root, registry);
        var dirtyRect = scene.DirtyBounds;
        Assert.IsTrue(scene.HasDirtyRegion && !scene.IsFullyDirty);

        // The second frame keeps the first one and repaints the dirty box alone.
        persistent.PreserveContentsOnBeginFrame = true;
        using (var context = factory.CreateContext(live))
        {
            context.BeginFrame(live);
            var dirtyRectContext = context as IOpaqueDirtyRectContext;
            Assert.IsNotNull(dirtyRectContext, "the context cannot erase a rectangle, so partial repaint is untestable");
            dirtyRectContext.ClearRectangle(dirtyRect, Color.White);
            FrameRenderer.Replay(scene, context, dirtyRect);
            context.EndFrame();
        }

        using var full = CreateSurface(factory);
        DrawInto(factory, full, context => FrameRenderer.Replay(scene, context), clear: true);

        byte[] partialPixels = ReadPixels(live);
        byte[] fullPixels = ReadPixels(full);
        for (int offset = 0; offset < fullPixels.Length; offset += 4)
        {
            AssertPixelEqual(fullPixels, partialPixels, offset, "after repainting only the dirty region");
        }
    }

    [TestMethod]
    public void UpdatePass_RecordsWithoutDrawing()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var root = BuildRoot(
            new FillBox { Fill = Color.FromArgb(255, 30, 120, 200) },
            new FillBox { Fill = Color.FromArgb(255, 200, 120, 30) });
        Layout(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        var registry = new RenderDirtyRegistry();

        using var updated = CreateSurface(factory);
        DrawInto(factory, updated, context =>
        {
            var recorder = new RenderDataRecorder(context) { SuppressDrawing = true };
            capture.Capture(scene, root, recorder, registry);
        }, clear: true);

        byte[] afterUpdate = ReadPixels(updated);
        for (int offset = 0; offset < afterUpdate.Length; offset += 4)
        {
            AssertPixelIsWhite(afterUpdate, offset);
        }

        Assert.IsTrue(scene.HasDirtyRegion, "the update pass recorded nothing");

        // The same scene, replayed, is what reaches the surface.
        using var replayed = CreateSurface(factory);
        DrawInto(factory, replayed, context => FrameRenderer.Replay(scene, context), clear: true);

        byte[] replayedPixels = ReadPixels(replayed);
        Assert.AreEqual(0, scene.Statistics.LiveFallbackCount);
        AssertPixelEqual(replayedPixels, replayedPixels, 0, "sanity");

        bool drewSomething = false;
        for (int offset = 0; offset < replayedPixels.Length; offset += 4)
        {
            if (replayedPixels[offset] != 255 || replayedPixels[offset + 1] != 255 || replayedPixels[offset + 2] != 255)
            {
                drewSomething = true;
                break;
            }
        }

        Assert.IsTrue(drewSomething, "the replay of the updated scene drew nothing");
    }

    [TestMethod]
    public void UnchangedFrame_ReportsNoDirtyRegion()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var root = BuildRoot(new FillBox(), new FillBox());
        Layout(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        var registry = new RenderDirtyRegistry();
        using var surface = CreateSurface(factory);
        DrawInto(factory, surface, context => capture.Capture(scene, root, context, registry), clear: true);

        scene.ResetDirtyRegion();
        DrawInto(factory, surface, context => capture.Capture(scene, root, context, registry), clear: true);

        Assert.IsFalse(scene.HasDirtyRegion, $"an unchanged frame reported a dirty region at {scene.DirtyBounds}");
    }

    [TestMethod]
    public void DeviceChange_DirtiesTheWholeSurface()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var root = BuildRoot(new FillBox(), new FillBox());
        Layout(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        var registry = new RenderDirtyRegistry();
        using var surface = CreateSurface(factory);
        DrawInto(factory, surface, context => capture.Capture(scene, root, context, registry), clear: true);

        scene.ResetDirtyRegion();
        scene.SetDeviceGeneration(scene.DeviceGeneration + 1);

        Assert.IsTrue(scene.IsFullyDirty, "a device change left the scene claiming a partial dirty region");
    }

    private static StackPanel BuildRoot(UIElement top, UIElement bottom)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(top, bottom);
        return stack;
    }

    private static void Layout(UIElement root)
    {
        root.Measure(new Size(SURFACE_WIDTH, SURFACE_HEIGHT));
        root.Arrange(new Rect(0, 0, SURFACE_WIDTH, SURFACE_HEIGHT));
    }

    /// <summary>Brings the scene up to date without drawing, which is what reports the dirty region.</summary>
    private static void UpdateScene(
        GdiGraphicsFactory factory,
        IRenderSurface surface,
        SceneCapture capture,
        RenderScene scene,
        UIElement root,
        RenderDirtyRegistry registry)
    {
        using var context = factory.CreateContext(surface);
        var recorder = new RenderDataRecorder(context) { SuppressDrawing = true };
        capture.Capture(scene, root, recorder, registry);
    }

    private static IRenderSurface CreateSurface(GdiGraphicsFactory factory)
        => factory.CreateSurface(
            RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));

    private static void DrawInto(
        GdiGraphicsFactory factory,
        IRenderSurface surface,
        Action<IGraphicsContext> draw,
        bool clear)
    {
        using var context = factory.CreateContext(surface);
        context.BeginFrame(surface);
        if (clear)
        {
            context.Clear(Color.White);
        }

        draw(context);
        context.EndFrame();
    }

    private static byte[] ReadPixels(IRenderSurface surface)
    {
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

    private static void AssertPixelEqual(byte[] expected, byte[] actual, int offset, string where)
    {
        if (expected[offset] == actual[offset] &&
            expected[offset + 1] == actual[offset + 1] &&
            expected[offset + 2] == actual[offset + 2] &&
            expected[offset + 3] == actual[offset + 3])
        {
            return;
        }

        int pixelIndex = offset / 4;
        Assert.Fail(
            $"The dirty replay differs from the full replay {where} at " +
            $"({pixelIndex % SURFACE_WIDTH},{pixelIndex / SURFACE_WIDTH}): " +
            $"expected BGRA=({expected[offset]},{expected[offset + 1]},{expected[offset + 2]},{expected[offset + 3]}) " +
            $"actual BGRA=({actual[offset]},{actual[offset + 1]},{actual[offset + 2]},{actual[offset + 3]}).");
    }

    private static void AssertPixelIsWhite(byte[] actual, int offset)
    {
        if (actual[offset] == 255 && actual[offset + 1] == 255 && actual[offset + 2] == 255)
        {
            return;
        }

        int pixelIndex = offset / 4;
        Assert.Fail(
            $"The dirty replay drew outside the dirty region at " +
            $"({pixelIndex % SURFACE_WIDTH},{pixelIndex / SURFACE_WIDTH}): " +
            $"BGRA=({actual[offset]},{actual[offset + 1]},{actual[offset + 2]},{actual[offset + 3]}).");
    }
}
