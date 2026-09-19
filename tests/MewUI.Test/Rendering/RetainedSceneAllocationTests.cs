using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.Retained;

namespace MewUI.Test.Rendering;

/// <summary>
/// Bounds what a scene update costs when nothing changed. The retained path runs this pass every
/// frame, so an allocation here is an allocation per frame.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedSceneAllocationTests
{
    private const int SURFACE_WIDTH = 200;
    private const int SURFACE_HEIGHT = 400;
    private const int ITEM_COUNT = 12;

    // Measured at 1,416 bytes for the 26-visual tree below (GDI, net8.0); raise it only with a reason.
    private const long STEADY_PASS_ALLOCATION_LIMIT = 2048;

    // Measured at 56 bytes (x64): the payload lives in the value buffer and the resource table.
    private const int COMMAND_RECORD_SIZE_LIMIT = 64;

    private sealed class FillBox : Control
    {
        protected override Size MeasureContent(Size availableSize) => new(60, 24);

        protected override void OnRender(IGraphicsContext context)
            => context.FillRectangle(Bounds, Color.FromArgb(255, 120, 160, 200));
    }

    [TestMethod]
    public void RecordedScene_ReportsWhatItsCommandsCost()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var root = BuildTree();
        root.Measure(new Size(SURFACE_WIDTH, SURFACE_HEIGHT));
        root.Arrange(new Rect(0, 0, SURFACE_WIDTH, SURFACE_HEIGHT));

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        var registry = new RenderDirtyRegistry();
        using var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));

        RunPass(factory, surface, capture, scene, root, registry);

        int commandSize = System.Runtime.CompilerServices.Unsafe.SizeOf<RenderCommand>();
        Console.WriteLine(
            $"command={commandSize}B scene={scene.EstimatedCommandBytes}B nodes={scene.NodeCount}");
        Assert.IsTrue(
            commandSize <= COMMAND_RECORD_SIZE_LIMIT,
            $"one recorded command takes {commandSize} bytes, over the {COMMAND_RECORD_SIZE_LIMIT} byte budget");
    }

    [TestMethod]
    public void SteadyStatePass_StaysWithinItsAllocationBudget()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var root = BuildTree();
        root.Measure(new Size(SURFACE_WIDTH, SURFACE_HEIGHT));
        root.Arrange(new Rect(0, 0, SURFACE_WIDTH, SURFACE_HEIGHT));

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        var registry = new RenderDirtyRegistry();

        using var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));

        // Two warm-up passes: the first records everything, the second settles the reused buffers.
        RunPass(factory, surface, capture, scene, root, registry);
        RunPass(factory, surface, capture, scene, root, registry);

        long before = GC.GetAllocatedBytesForCurrentThread();
        RunPass(factory, surface, capture, scene, root, registry);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.AreEqual(0, scene.Statistics.ContentRecordCount, "the steady pass re-recorded content");
        Assert.IsTrue(
            allocated <= STEADY_PASS_ALLOCATION_LIMIT,
            $"a scene update with no changes allocated {allocated} bytes, over the {STEADY_PASS_ALLOCATION_LIMIT} byte budget");
    }

    private static void RunPass(
        GdiGraphicsFactory factory,
        IRenderSurface surface,
        SceneCapture capture,
        RenderScene scene,
        UIElement root,
        RenderDirtyRegistry registry)
    {
        using var context = factory.CreateContext(surface);
        var recorder = new RenderDataRecorder(context) { SuppressDrawing = true };
        scene.Statistics.Reset();
        scene.ResetDamage();
        capture.Capture(scene, root, recorder, registry);
    }

    private static UIElement BuildTree()
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        var children = new Element[ITEM_COUNT];
        for (int index = 0; index < ITEM_COUNT; index++)
        {
            children[index] = new Border
            {
                Height = 28,
                CornerRadius = index % 2 == 0 ? 4 : 0,
                Background = Color.FromArgb(255, (byte)(200 - index * 4), 220, 230),
                Child = new FillBox(),
            };
        }

        stack.Children(children);

        return new Border
        {
            Padding = new Thickness(6),
            Background = Color.FromArgb(255, 250, 250, 250),
            Child = stack,
        };
    }
}
