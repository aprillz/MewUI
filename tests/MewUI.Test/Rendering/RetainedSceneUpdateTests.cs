using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.Retained;

namespace MewUI.Test.Rendering;

/// <summary>
/// Validates the update transaction of the retained scene: a failed pass keeps the previous scene,
/// state that the scene applies around a visual does not re-record its content, an invalidation the
/// pass could not serve stays queued, and a visual that changes parent loses its stale data.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedSceneUpdateTests
{
    private const int SURFACE_WIDTH = 160;
    private const int SURFACE_HEIGHT = 120;

    private sealed class FillBox : Control
    {
        internal Color Fill { get; set; } = Color.FromArgb(255, 200, 60, 60);

        internal bool ThrowOnRender { get; set; }

        internal int RenderCount { get; private set; }

        protected override Size MeasureContent(Size availableSize) => new(40, 30);

        protected override void OnRender(IGraphicsContext context)
        {
            if (ThrowOnRender)
            {
                throw new InvalidOperationException("render failure for the test");
            }

            RenderCount++;
            context.FillRectangle(Bounds, Fill);
        }
    }

    [TestMethod]
    public void FailedUpdate_KeepsThePreviousScene()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var box = new FillBox();
        var root = BuildRoot(box);
        Layout(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        Render(factory, context => capture.Capture(scene, root, context));

        var rootNode = scene.FindNode(root)!;
        var boxNode = scene.FindNode(box)!;
        var rootData = rootNode.GetSlot(0);
        var boxData = boxNode.GetSlot(0);
        Assert.IsNotNull(rootData);
        Assert.IsNotNull(boxData);

        box.Fill = Color.FromArgb(255, 10, 10, 10);
        box.ThrowOnRender = true;
        box.InvalidateVisual();

        Assert.ThrowsExactly<InvalidOperationException>(
            () => Render(factory, context => capture.Capture(scene, root, context)));

        Assert.AreSame(rootData, rootNode.GetSlot(0), "the failed pass replaced the root content");
        Assert.AreSame(boxData, boxNode.GetSlot(0), "the failed pass replaced the child content");

        // The previous scene is still drawable, which it would not be if its data had been released.
        box.ThrowOnRender = false;
        Render(factory, context => FrameRenderer.Replay(scene, context));
        Assert.AreEqual(0, scene.Statistics.LiveFallbackCount);
    }

    [TestMethod]
    public void OpacityChange_DoesNotReRecordContent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var box = new FillBox();
        var root = BuildRoot(box);
        Layout(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        var registry = new RenderDirtyRegistry();
        Render(factory, context => capture.Capture(scene, root, context, registry));

        scene.Statistics.Reset();
        root.Opacity = 0.5;
        Render(factory, context => capture.Capture(scene, root, context, registry));

        Assert.AreEqual(0, scene.Statistics.ContentRecordCount, "an opacity change re-recorded content");
        Assert.AreEqual(0.5, scene.FindNode(root)!.State.Opacity);
    }

    [TestMethod]
    public void ContentChangedWhileHidden_IsRecordedWhenShownAgain()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var box = new FillBox();
        var root = BuildRoot(box);
        Layout(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        var registry = new RenderDirtyRegistry();
        Render(factory, context => capture.Capture(scene, root, context, registry));

        box.IsVisible = false;
        Render(factory, context => capture.Capture(scene, root, context, registry));

        box.Fill = Color.FromArgb(255, 5, 90, 5);
        box.InvalidateVisual();
        Render(factory, context => capture.Capture(scene, root, context, registry));

        scene.Statistics.Reset();
        int renderCountWhileHidden = box.RenderCount;
        box.IsVisible = true;
        Render(factory, context => capture.Capture(scene, root, context, registry));

        Assert.IsTrue(
            box.RenderCount > renderCountWhileHidden,
            "the change made while hidden was lost instead of being recorded when shown again");
        Assert.IsNotNull(scene.FindNode(box)!.GetSlot(0));
    }

    [TestMethod]
    public void ReparentedVisual_DropsTheDataOfItsPreviousParent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var box = new FillBox();
        var firstHost = new Border { Width = 60, Height = 40, Child = box };
        var secondHost = new Border { Width = 60, Height = 40 };
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(firstHost, secondHost);
        Layout(stack);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        Render(factory, context => capture.Capture(scene, stack, context));

        var boxNode = scene.FindNode(box)!;
        int generation = boxNode.AttachmentGeneration;

        firstHost.Child = null;
        secondHost.Child = box;
        Layout(stack);
        scene.Statistics.Reset();
        Render(factory, context => capture.Capture(scene, stack, context));

        Assert.AreEqual(generation + 1, scene.FindNode(box)!.AttachmentGeneration);
        Assert.IsTrue(scene.Statistics.ContentRecordCount > 0, "the moved visual kept the data of its old parent");
    }

    [TestMethod]
    public void RemovedChild_LosesItsNode()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var box = new FillBox();
        var host = new Border { Width = 60, Height = 40, Child = box };
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(host);
        Layout(stack);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        Render(factory, context => capture.Capture(scene, stack, context));
        Assert.IsNotNull(scene.FindNode(box));

        host.Child = null;
        Layout(stack);
        Render(factory, context => capture.Capture(scene, stack, context));

        Assert.IsNull(scene.FindNode(box), "the scene kept a node for a visual that left the tree");
    }

    private static Border BuildRoot(UIElement child) => new()
    {
        Width = 120,
        Height = 80,
        BorderThickness = 2,
        BorderBrush = Color.FromArgb(255, 20, 20, 20),
        Background = Color.FromArgb(255, 240, 240, 240),
        Child = child,
    };

    private static void Layout(UIElement root)
    {
        root.Measure(new Size(SURFACE_WIDTH, SURFACE_HEIGHT));
        root.Arrange(new Rect(0, 0, SURFACE_WIDTH, SURFACE_HEIGHT));
    }

    private static void Render(GdiGraphicsFactory factory, Action<IGraphicsContext> draw)
    {
        using var surface = factory.CreateSurface(
            RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));
        using var context = factory.CreateContext(surface);
        context.BeginFrame(surface);
        context.Clear(Color.White);
        try
        {
            draw(context);
        }
        finally
        {
            context.EndFrame();
        }
    }
}
