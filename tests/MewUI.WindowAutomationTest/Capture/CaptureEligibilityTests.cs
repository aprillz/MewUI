using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A capture holder is attached, effectively enabled and visible. A request for anything else is refused,
/// and a holder that stops qualifying while the button is held loses the capture without clicking.
/// </summary>
[TestClass]
public sealed class CaptureEligibilityTests
{
    [TestMethod]
    public Task CaptureRequest_ForADetachedElement_IsRefused() => CaptureScene.RunAsync(async scene =>
    {
        var window = await scene.ShowAsync(new Border());
        var detached = new Border { Width = 50, Height = 50 };

        window.CaptureMouse(detached);

        Assert.IsTrue(!detached.IsMouseCaptured && window.CapturedElement == null, "a detached element took the capture");
        scene.CheckPlatformCaptureFree(window, "after a refused request");
    });

    [TestMethod]
    [DataRow("disabled")]
    [DataRow("hidden")]
    public Task CaptureRequest_ForAnIneligibleAttachedElement_IsRefused(string state) => CaptureScene.RunAsync(async scene =>
    {
        var target = new Border { Width = 50, Height = 50 };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Add(target);
        var window = await scene.ShowAsync(panel);

        if (state == "disabled")
        {
            target.IsEnabled = false;
        }
        else
        {
            target.IsVisible = false;
        }

        window.CaptureMouse(target);

        Assert.IsTrue(!target.IsMouseCaptured && window.CapturedElement == null, $"a {state} element took the capture");
        scene.CheckPlatformCaptureFree(window, "after a refused request");
    });

    [TestMethod]
    [DataRow("detach")]
    [DataRow("detach-ancestor")]
    [DataRow("disable")]
    [DataRow("disable-ancestor")]
    [DataRow("can-click-false")]
    [DataRow("hide")]
    [DataRow("hide-ancestor")]
    public Task HolderThatStopsQualifyingDuringAPress_LosesTheCaptureWithoutClicking(string change) => CaptureScene.RunAsync(async scene =>
    {
        var pressed = CaptureScene.NewButton("pressed");
        var other = CaptureScene.NewButton("other");
        var inner = new StackPanel();
        inner.Add(pressed);
        var outer = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        outer.Add(inner);
        outer.Add(other);
        var window = await scene.ShowAsync(outer);

        int pressedClicks = 0;
        int otherClicks = 0;
        pressed.Click += () => pressedClicks++;
        other.Click += () => otherClicks++;

        var pressedCenter = CaptureScene.Center(pressed);
        await scene.Input.MoveAsync(window, pressedCenter);
        await scene.Input.PressAsync(window, pressedCenter);
        Assert.IsTrue(pressed.IsMouseCaptured, "precondition: the press captured");

        switch (change)
        {
            case "detach": inner.Remove(pressed); break;
            case "detach-ancestor": outer.Remove(inner); break;
            case "disable": pressed.IsEnabled = false; break;
            case "disable-ancestor": inner.IsEnabled = false; break;
            case "can-click-false": pressed.CanClick = static () => false; break;
            case "hide": pressed.IsVisible = false; break;
            case "hide-ancestor": inner.IsVisible = false; break;
        }

        await Task.Delay(150);
        Assert.IsNull(window.CapturedElement, $"the capture outlived '{change}'");
        Assert.IsFalse(pressed.IsPressed, $"the button still looks pressed after '{change}'");

        await scene.Input.ReleaseAsync(window, pressedCenter);
        scene.CheckPlatformCaptureFree(window, "after the release");

        await scene.ClickAsync(window, other);

        Assert.AreEqual(0, pressedClicks, "a button that stopped qualifying raised Click");
        Assert.AreEqual(1, otherClicks, "the next click did not reach the button under the pointer once");
    });

    [TestMethod]
    public Task HolderRecycledByAVirtualizedListDuringAPress_LosesTheCapture() => CaptureScene.RunAsync(async scene =>
    {
        var items = new List<string>();
        for (int index = 0; index < 200; index++)
        {
            items.Add($"row {index}");
        }

        var list = new ItemsControl { Height = 200, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(20) }
            .FixedHeightPresenter(28)
            .ItemTemplate(new DelegateTemplate<string>(
                static _ => new SegmentButton(),
                static (_, _, _, _) => { }))
            .ItemsSource(ItemsView.Create(items));
        var window = await scene.ShowAsync(list);

        var firstRow = (SegmentButton)VisualTree.Find(list, static element => element is SegmentButton)!;
        var center = CaptureScene.Center(firstRow);
        await scene.Input.MoveAsync(window, center);
        await scene.Input.PressAsync(window, center);
        Assert.IsTrue(firstRow.IsMouseCaptured, "precondition: the press captured");

        list.ScrollIntoView(items.Count - 1);
        await Task.Delay(250);

        Assert.IsNull(firstRow.FindVisualRoot(), "precondition: scrolling recycled the pressed row");
        Assert.IsNull(window.CapturedElement, "input stayed routed to a recycled row");

        await scene.Input.ReleaseAsync(window, center);
        scene.CheckPlatformCaptureFree(window, "after the release");
    });

    [TestMethod]
    public Task FocusMoveThatDisablesThePressedButton_RefusesItsCapture() => CaptureScene.RunAsync(async scene =>
    {
        var editor = new TextBox { Width = 200 };
        var button = CaptureScene.NewButton("command");
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        panel.Add(editor);
        panel.Add(button);
        var window = await scene.ShowAsync(panel);

        // A command whose availability depends on where focus is: pressing the button moves focus to it.
        button.CanClick = () => editor.IsFocused;
        editor.Focus();
        window.RequerySuggested();
        Assert.IsTrue(button.IsEffectivelyEnabled, "precondition: enabled while the editor has focus");

        var center = CaptureScene.Center(button);
        await scene.Input.MoveAsync(window, center);
        await scene.Input.PressAsync(window, center);

        Assert.IsFalse(button.IsEffectivelyEnabled, "precondition: the focus move disabled the button");
        Assert.IsFalse(button.IsMouseCaptured, "a disabled button took the capture");
        Assert.IsFalse(button.IsPressed, "a refused press left the button looking pressed");

        await scene.Input.ReleaseAsync(window, center);
        scene.CheckPlatformCaptureFree(window, "after the release");
    });
}
