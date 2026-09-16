using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A drag a control keeps alongside its capture ends when the capture is taken away, so a release that never
/// arrives cannot leave the control dragging or pressed (issue #259).
/// </summary>
[TestClass]
public sealed class CaptureDragStateTests
{
    [TestMethod]
    public Task SplitPanel_DragEndsWhenTheCaptureIsTakenAway() => CaptureScene.RunAsync(async scene =>
    {
        var panel = new SplitPanel
        {
            Width = 400,
            Height = 200,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(20),
            First = new Border(),
            Second = new Border(),
        };
        var window = await scene.ShowAsync(panel);
        var other = await scene.ShowAsync(new Border());
        await scene.Input.ActivateAsync(window);

        var thumb = (SplitPanel.SplitterThumb)typeof(SplitPanel).GetField("_splitter", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(panel)!;
        var grab = CaptureScene.Center(thumb);
        await scene.Input.MoveAsync(window, grab);
        await scene.Input.PressAsync(window, grab);
        Assert.IsTrue(thumb.IsDragging, $"precondition: the press started a drag (thumb {thumb.Bounds}, press {grab}, active {window.IsActive}, holder {window.CapturedElement?.GetType().Name ?? "none"})");

        await scene.Input.MoveAsync(window, new Point(grab.X + 40, grab.Y));
        Assert.IsTrue(thumb.IsDragging, $"precondition: the drag survived a move (captured {thumb.IsMouseCaptured}, active {window.IsActive}, holder {window.CapturedElement?.GetType().Name ?? "none"})");

        await scene.Input.DeactivateAsync(window, other);
        Assert.IsFalse(thumb.IsDragging, "the splitter kept dragging after its capture was taken away");

        await scene.Input.ReleaseAsync(window, CaptureScene.Away(window));
        await scene.Input.ActivateAsync(window);
        var lengthAfterRelease = panel.FirstLength;

        var over = CaptureScene.Center(thumb);
        await scene.Input.MoveAsync(window, over);
        await scene.Input.MoveAsync(window, new Point(over.X + 60, over.Y));

        Assert.AreEqual(lengthAfterRelease, panel.FirstLength, "the splitter followed a pointer with no button held");
    });

    [TestMethod]
    public Task Slider_PressedLookEndsWhenTheCaptureIsTakenAway() => CaptureScene.RunAsync(async scene =>
    {
        var (window, slider) = await scene.ShowSliderAsync();
        var other = await scene.ShowAsync(new Border());
        await scene.Input.ActivateAsync(window);

        await scene.PressSliderAsync(window, slider);
        Assert.IsTrue(slider.IsPressed, "precondition: the press shows the slider pressed");

        await scene.Input.DeactivateAsync(window, other);
        Assert.IsFalse(slider.IsPressed, "the slider stayed pressed after its capture was taken away");

        await scene.Input.ReleaseAsync(window, CaptureScene.Away(window));
    });
}
