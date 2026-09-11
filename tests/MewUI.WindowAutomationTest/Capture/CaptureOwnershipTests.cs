using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// The platform holds one mouse capture per UI thread. A window taking it ends another window's element
/// capture, only the holder releases it, and an open popup keeps its dismiss watch.
/// </summary>
[TestClass]
public sealed class CaptureOwnershipTests
{
    [TestMethod]
    public Task CapturingInAnotherWindow_EndsTheFirstWindowsElementCapture() => CaptureScene.RunAsync(async scene =>
    {
        var (first, firstTarget) = await ShowCaptureTargetAsync(scene);
        var (second, secondTarget) = await ShowCaptureTargetAsync(scene);

        first.CaptureMouse(firstTarget);
        second.CaptureMouse(secondTarget);

        Assert.IsTrue(secondTarget.IsMouseCaptured, "precondition: the second window captured");
        Assert.IsTrue(!firstTarget.IsMouseCaptured && first.CapturedElement == null, "the first window kept an element capture with no platform capture behind it");
        scene.CheckPlatformCaptureHeld(second, "by the second window");

        second.ReleaseMouseCapture();
        scene.CheckPlatformCaptureFree(second, "after the holder released");
    });

    [TestMethod]
    public Task ReleaseFromAWindowThatNoLongerHolds_KeepsTheHoldersCapture() => CaptureScene.RunAsync(async scene =>
    {
        var (first, firstTarget) = await ShowCaptureTargetAsync(scene);
        var (second, secondTarget) = await ShowCaptureTargetAsync(scene);

        first.CaptureMouse(firstTarget);
        second.CaptureMouse(secondTarget);
        first.ReleaseMouseCapture();

        Assert.IsTrue(secondTarget.IsMouseCaptured, "a release from the former holder ended the second window's element capture");
        scene.CheckPlatformCaptureHeld(second, "after the former holder released");

        second.ReleaseMouseCapture();
        scene.CheckPlatformCaptureFree(second, "after the holder released");
    });

    [TestMethod]
    public Task PopupOpenedDuringAPress_EndsTheOwnersElementCaptureAndKeepsItsWatch() => CaptureScene.RunAsync(async scene =>
    {
        var (window, slider) = await scene.ShowSliderAsync();

        await scene.PressSliderAsync(window, slider);
        var popup = await CaptureScene.OpenPopupAsync(slider, CaptureScene.NewButton("inner"));

        Assert.IsNull(window.CapturedElement, "the owner kept an element capture after the popup took the platform capture");

        await scene.Input.ReleaseAsync(window, CaptureScene.Center(slider));
        Assert.IsTrue(popup.IsOpen, "releasing the press in the owner closed the popup");

        await scene.PressOutsideAsync(window);
        Assert.IsFalse(popup.IsOpen, "an outside press no longer dismisses the popup");
    });

    [TestMethod]
    public Task CapturingInAnUnrelatedWindow_DismissesAnOpenPopup() => CaptureScene.RunAsync(async scene =>
    {
        var (_, anchor) = await scene.ShowAnchorAsync();
        var popup = await CaptureScene.OpenPopupAsync(anchor, CaptureScene.NewButton("inner"));
        var (unrelated, unrelatedTarget) = await ShowCaptureTargetAsync(scene);

        unrelated.CaptureMouse(unrelatedTarget);
        await Task.Delay(200);

        Assert.IsFalse(popup.IsOpen, "another window took the platform capture without the popup closing");
        unrelated.ReleaseMouseCapture();
    });

    private static async Task<(Window Window, Border Target)> ShowCaptureTargetAsync(CaptureScene scene)
    {
        var target = new Border { Width = 120, Height = 40, HorizontalAlignment = HorizontalAlignment.Left };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Add(target);
        var window = await scene.ShowAsync(panel);
        return (window, target);
    }
}
