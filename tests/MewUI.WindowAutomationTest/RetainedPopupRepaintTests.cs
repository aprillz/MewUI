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
}
