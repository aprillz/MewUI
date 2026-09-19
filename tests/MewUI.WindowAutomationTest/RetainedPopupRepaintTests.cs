using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A popup is a window of its own that draws a subtree of its owner. Moving the pointer from one menu
/// item to another changes two rows, so the popup has to repaint part of itself, not all of it, and an
/// idle popup must not draw frames at all.
/// </summary>
[TestClass]
public sealed class RetainedPopupRepaintTests
{
    [TestMethod]
    public Task HoveringMenuItems_RepaintsPartOfThePopup() => CaptureScene.RunAsync(async scene =>
    {
        var owner = new Button { Content = new TextBlock { Text = "Owner" }, Width = 140, Height = 30, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20) };
        var window = await scene.ShowAsync(owner);

        var menu = new ContextMenu();
        for (int index = 0; index < 8; index++)
        {
            menu.AddItem(new Command($"probe.item{index}", $"Menu item number {index}"));
        }

        menu.Show(owner, new Point(owner.Bounds.X, owner.Bounds.Bottom));
        await Task.Delay(500);

        var popup = menu.ResolveInputHostWindow();
        if (popup == null || ReferenceEquals(popup, window))
        {
            Assert.Inconclusive("The menu opened inside its owner's surface, so there is no popup window to look at.");
        }

        double popupArea = popup!.ClientSize.Width * popup.ClientSize.Height;
        await scene.Input.MoveAsync(popup, new Point(popup.ClientSize.Width / 2, popup.ClientSize.Height * 0.2));
        await Task.Delay(400);

        popup.ResetRetainedFrameCounts();
        await Task.Delay(400);
        var idle = popup.RetainedFrames;
        Assert.AreEqual(0, idle.Whole + idle.Partial, $"an idle popup painted {idle.Whole} whole and {idle.Partial} partial frames");

        popup.ResetRetainedFrameCounts();
        await scene.Input.MoveAsync(popup, new Point(popup.ClientSize.Width / 2, popup.ClientSize.Height * 0.7));
        await Task.Delay(500);

        var counts = popup.RetainedFrames;
        Assert.IsTrue(
            counts.Partial > 0,
            $"moving between menu items repainted no frame in part (whole {counts.Whole}, partial {counts.Partial}, untouched {counts.Untouched}; {popup.LastWholeFrameReason})");
        Assert.AreEqual(
            0,
            counts.Whole,
            $"moving between menu items painted {counts.Whole} whole popup frames ({popup.LastWholeFrameReason})");
        Assert.IsTrue(
            popup.LargestPartialRepaintArea < popupArea * 0.7,
            $"the largest repaint {popup.LargestPartialRepaint} covers most of the {popup.ClientSize} popup");

        menu.CloseTree(window);
    });

    [TestMethod]
    public Task ControlsInsideAPopup_RepaintWhereTheyChange() => CaptureScene.RunAsync(async scene =>
    {
        var owner = new Button { Content = new TextBlock { Text = "Owner" }, Width = 140, Height = 30, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20) };
        var window = await scene.ShowAsync(owner);

        var list = new ListBox { Width = 220, Height = 200 };
        list.Items(Enumerable.Range(0, 30).Select(index => $"List item number {index}").ToArray());

        var progress = new ProgressBar { IsIndeterminate = false, Width = 220, Height = 8, Value = 40 };
        var content = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Margin = new Thickness(8) };
        content.Children(list, progress);
        var popup = new Popup { Content = content, StaysOpen = true };
        popup.ShowAt(owner, owner.Bounds);
        await Task.Delay(600);

        var surface = list.ResolveInputHostWindow();
        if (surface == null || ReferenceEquals(surface, window))
        {
            Assert.Inconclusive("The popup opened inside its owner's surface, so there is no popup window to look at.");
        }

        double surfaceArea = surface!.ClientSize.Width * surface.ClientSize.Height;
        await scene.Input.MoveAsync(surface, new Point(surface.ClientSize.Width / 2, surface.ClientSize.Height * 0.15));
        await Task.Delay(400);

        surface.ResetRetainedFrameCounts();
        await scene.Input.MoveAsync(surface, new Point(surface.ClientSize.Width / 2, surface.ClientSize.Height * 0.55));
        await Task.Delay(500);

        var hover = surface.RetainedFrames;
        Assert.IsTrue(hover.Partial > 0, $"hovering list items in a popup repainted no frame in part (whole {hover.Whole}; {surface.LastWholeFrameReason})");
        Assert.AreEqual(0, hover.Whole, $"hovering list items in a popup painted {hover.Whole} whole frames ({surface.LastWholeFrameReason})");
        Assert.IsTrue(
            surface.LargestPartialRepaintArea < surfaceArea * 0.3,
            $"hovering a list item repainted {surface.LargestPartialRepaint} of the {surface.ClientSize} popup");

        surface.ResetRetainedFrameCounts();
        progress.IsIndeterminate = true;
        await Task.Delay(800);

        var animated = surface.RetainedFrames;
        Assert.IsTrue(animated.Partial > 3, $"an animation inside a popup painted {animated.Partial} partial frames (whole {animated.Whole}; {surface.LastWholeFrameReason})");
        Assert.IsTrue(animated.Whole <= 1, $"an animation inside a popup painted {animated.Whole} whole frames ({surface.LastWholeFrameReason})");
        Assert.IsTrue(
            surface.LargestPartialRepaintArea < surfaceArea * 0.3,
            $"an animated bar repainted {surface.LargestPartialRepaint} of the {surface.ClientSize} popup");

        popup.Close();
    });
}
