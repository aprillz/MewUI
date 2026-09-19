using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.Retained;

namespace MewUI.Test.Rendering;

/// <summary>
/// Validates that an item's visual state reaches the surface through the container that owns it:
/// changing one item's selection or hover must re-record that container alone, and must damage only
/// the area that container covers.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedItemScopeTests
{
    private const int SURFACE_WIDTH = 200;
    private const int SURFACE_HEIGHT = 200;
    private const int ITEM_COUNT = 5;

    [TestMethod]
    public void SelectionChange_ReRecordsOnlyTheItemThatChanged()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var containers = BuildItems();
        var root = BuildRoot(containers);
        Layout(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        var registry = new RenderDirtyRegistry();
        Render(factory, context => capture.Capture(scene, root, context, registry));

        scene.Statistics.Reset();
        scene.ResetDamage();
        containers[2].SetIsSelected(true);
        Render(factory, context => capture.Capture(scene, root, context, registry));

        Assert.AreEqual(
            1,
            scene.Statistics.ContentRecordCount,
            "a selection change re-recorded more than the container that owns it");
        Assert.IsGreaterThan(0, scene.Statistics.ContentReplayCount, "the unchanged items were not replayed");

        var damage = scene.DamageBounds;
        Assert.IsTrue(damage.IntersectsWith(containers[2].Bounds));
        Assert.IsFalse(
            damage.IntersectsWith(containers[0].Bounds),
            $"the damage {damage} reaches an item that did not change at {containers[0].Bounds}");
        Assert.IsFalse(
            damage.IntersectsWith(containers[4].Bounds),
            $"the damage {damage} reaches an item that did not change at {containers[4].Bounds}");
    }

    [TestMethod]
    public void HoverMove_ReRecordsTheTwoItemsItTouches()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var containers = BuildItems();
        var root = BuildRoot(containers);
        Layout(root);

        using var scene = new RenderScene();
        var capture = new SceneCapture();
        var registry = new RenderDirtyRegistry();
        containers[1].SetIsHovered(true);
        Render(factory, context => capture.Capture(scene, root, context, registry));

        scene.Statistics.Reset();
        containers[1].SetIsHovered(false);
        containers[3].SetIsHovered(true);
        Render(factory, context => capture.Capture(scene, root, context, registry));

        Assert.AreEqual(
            2,
            scene.Statistics.ContentRecordCount,
            "moving the hover re-recorded more than the item it left and the item it reached");
    }

    private static ItemContainer[] BuildItems()
    {
        var containers = new ItemContainer[ITEM_COUNT];
        for (int index = 0; index < ITEM_COUNT; index++)
        {
            containers[index] = new ItemContainer
            {
                Height = 24,
                SelectionBackground = Color.FromArgb(255, 60, 120, 220),
                HoverBackground = Color.FromArgb(255, 200, 220, 240),
                AlternateBackground = Color.FromArgb(255, 240, 240, 240),
                Content = new TextBlock { Text = "item " + index },
            };
            containers[index].SetIsAlternate((index & 1) == 1);
        }

        return containers;
    }

    private static StackPanel BuildRoot(ItemContainer[] containers)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(containers);
        return stack;
    }

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
        draw(context);
        context.EndFrame();
    }
}
