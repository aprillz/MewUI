using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Validates that an item's visual state reaches the surface through the container that owns it:
/// changing one item's selection or hover must re-record that container alone, and must damage only
/// the area that container covers. The state goes through the container's style, as it does in a
/// list, so the containers stand in a window.
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
        var window = BuildWindow(containers);
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));
        Frames(window, surface, 3);

        window.RetainedStatistics!.Reset();
        containers[2].SetIsSelected(true);
        Frames(window, surface, 1);

        Assert.AreEqual(
            1,
            window.RetainedStatistics.ContentRecordCount,
            "a selection change re-recorded more than the container that owns it");

        Assert.IsTrue(window.LastRetainedDamage is Rect, $"the selection change repainted the whole frame ({window.LastWholeFrameReason})");
        var damage = (Rect)window.LastRetainedDamage!;
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
        var window = BuildWindow(containers);
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));
        containers[1].SetIsHovered(true);
        Frames(window, surface, 3);

        window.RetainedStatistics!.Reset();
        containers[1].SetIsHovered(false);
        containers[3].SetIsHovered(true);
        Frames(window, surface, 1);

        Assert.AreEqual(
            2,
            window.RetainedStatistics.ContentRecordCount,
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
                Content = new TextBlock { Text = "item " + index },
            };
            containers[index].SetAlternate((index & 1) == 1, Color.FromArgb(255, 240, 240, 240));
        }

        return containers;
    }

    private static Window BuildWindow(ItemContainer[] containers)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(containers);
        var window = HeadlessWindow.Create(SURFACE_WIDTH, SURFACE_HEIGHT);
        window.Content = stack;
        window.PerformLayout();
        return window;
    }

    private static void Frames(Window window, IRenderSurface surface, int count)
    {
        for (int index = 0; index < count; index++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }
    }
}
