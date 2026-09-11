using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// While an element holds the capture only its subtree is under the pointer, and hover follows the real
/// pointer again as soon as the capture ends.
/// </summary>
[TestClass]
public sealed class CaptureHoverTests
{
    [TestMethod]
    public Task DuringACapture_ElementsOutsideTheCapturedSubtree_AreNotOver() => CaptureScene.RunAsync(async scene =>
    {
        var (window, slider, button) = await ShowSliderAndButtonAsync(scene);

        await scene.PressSliderAsync(window, slider);
        await scene.Input.MoveAsync(window, CaptureScene.Center(button));

        Assert.IsFalse(button.IsMouseOver, "a drag lit up the element it passed over");
    });

    [TestMethod]
    public Task DuringACapture_TheCapturedElement_IsOverOnlyWhileThePointerIsInside() => CaptureScene.RunAsync(async scene =>
    {
        var (window, slider, _) = await ShowSliderAndButtonAsync(scene);

        await scene.PressSliderAsync(window, slider);
        await scene.Input.MoveAsync(window, new Point(slider.Bounds.X + 10, CaptureScene.Center(slider).Y));
        Assert.IsTrue(slider.IsMouseOver, "the captured element is not over while the pointer is inside it");

        await scene.Input.MoveAsync(window, CaptureScene.Away(window));
        Assert.IsFalse(slider.IsMouseOver, "the captured element stays over after the pointer left it");
    });

    [TestMethod]
    public Task WhenTheCaptureEnds_MouseOverFollowsThePointerAtOnce() => CaptureScene.RunAsync(async scene =>
    {
        var (window, slider, button) = await ShowSliderAndButtonAsync(scene);

        await scene.PressSliderAsync(window, slider);
        var buttonCenter = CaptureScene.Center(button);
        await scene.Input.MoveAsync(window, buttonCenter);
        await scene.Input.ReleaseAsync(window, buttonCenter);

        Assert.IsTrue(button.IsMouseOver, "hover waited for another move after the release");
    });

    [TestMethod]
    public Task AutoHideScrollBar_StaysRevealedWhileItsThumbIsDraggedOutside() => CaptureScene.RunAsync(async scene =>
    {
        var viewer = new ScrollViewer
        {
            AutoHideScrollBars = true,
            Width = 200,
            Height = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(20),
            Content = new Border { Height = 2000 },
        };
        var window = await scene.ShowAsync(viewer);

        var bar = (ScrollBar)typeof(ScrollViewer).GetField("_vBar", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(viewer)!;
        var thumbPoint = new Point(bar.Bounds.X + bar.Bounds.Width / 2, bar.Bounds.Y + 6);

        await scene.Input.MoveAsync(window, thumbPoint);
        Assert.IsTrue(IsFadeHot(viewer), "precondition: hovering the bar reveals it");

        await scene.Input.PressAsync(window, thumbPoint);
        Assert.IsTrue(bar.IsMouseCaptured, "precondition: the thumb press captured");

        await scene.Input.MoveAsync(window, CaptureScene.Away(window));
        Assert.IsTrue(IsFadeHot(viewer), "the bar started fading out while its thumb was being dragged");

        await scene.Input.ReleaseAsync(window, CaptureScene.Away(window));
        Assert.IsFalse(IsFadeHot(viewer), "the bar stayed revealed after the drag ended away from it");
    });

    private static bool IsFadeHot(ScrollViewer viewer)
    {
        var fade = typeof(ScrollViewer).GetField("_barFade", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(viewer)!;
        return (bool)fade.GetType().GetField("_hot", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(fade)!;
    }

    private static async Task<(Window Window, Slider Slider, Button Button)> ShowSliderAndButtonAsync(CaptureScene scene)
    {
        var slider = new Slider { Minimum = 0, Maximum = 100, Width = 220, Height = 24, HorizontalAlignment = HorizontalAlignment.Left };
        var button = CaptureScene.NewButton("button");
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        panel.Add(slider);
        panel.Add(button);
        var window = await scene.ShowAsync(panel);
        return (window, slider, button);
    }
}
