using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// Press controls under real input: releasing away gives the capture back and commits nothing, the pressed
/// look follows the pointer during the press, and a keyboard press ignores the mouse.
/// </summary>
[TestClass]
public sealed class CapturePressTests
{
    [TestMethod]
    [DataRow("Button")]
    [DataRow("ToggleButton")]
    [DataRow("CheckBox")]
    [DataRow("RadioButton")]
    [DataRow("ToggleSwitch")]
    [DataRow("SegmentButton")]
    [DataRow("RepeatButton")]
    public Task ReleaseAway_EndsTheCaptureAndTheNextClickWorks(string kind) => CaptureScene.RunAsync(async scene =>
    {
        var subject = PressSubject.Create(kind);
        var (window, other) = await ShowWithOtherButtonAsync(scene, subject.Control);
        int otherClicks = 0;
        other.Click += () => otherClicks++;

        await scene.PressAndLeaveAsync(window, subject.Control);
        await scene.Input.ReleaseAsync(window, CaptureScene.Away(window));

        Assert.IsFalse(subject.Control.IsMouseCaptured, "the control kept the element capture after the release");
        scene.CheckPlatformCaptureFree(window, "after the release");

        await scene.ClickAsync(window, other);
        Assert.AreEqual(1, otherClicks, "the next click did not reach the other button once");
    });

    [TestMethod]
    [DataRow("Button")]
    [DataRow("ToggleButton")]
    [DataRow("CheckBox")]
    [DataRow("RadioButton")]
    [DataRow("ToggleSwitch")]
    [DataRow("SegmentButton")]
    [DataRow("RepeatButton")]
    public Task PressedLook_FollowsThePointerDuringThePress(string kind) => CaptureScene.RunAsync(async scene =>
    {
        var subject = PressSubject.Create(kind);
        var (window, _) = await ShowWithOtherButtonAsync(scene, subject.Control);

        await scene.PressAndLeaveAsync(window, subject.Control);
        Assert.IsFalse(subject.Control.IsPressed, "looks pressed while the pointer is away");

        await scene.Input.MoveAsync(window, CaptureScene.Center(subject.Control));
        Assert.IsTrue(subject.Control.IsPressed, "does not look pressed again after the pointer returned");

        await scene.Input.ReleaseAsync(window, CaptureScene.Center(subject.Control));
        Assert.IsFalse(subject.Control.IsPressed, "still looks pressed after the release");
    });

    [TestMethod]
    [DataRow("Button")]
    [DataRow("ToggleButton")]
    [DataRow("CheckBox")]
    [DataRow("RadioButton")]
    [DataRow("ToggleSwitch")]
    public Task ReleaseAway_CommitsNothing(string kind) => CaptureScene.RunAsync(async scene =>
    {
        var subject = PressSubject.Create(kind);
        var (window, _) = await ShowWithOtherButtonAsync(scene, subject.Control);

        await scene.PressAndLeaveAsync(window, subject.Control);
        await scene.Input.ReleaseAsync(window, CaptureScene.Away(window));

        Assert.AreEqual(0, subject.Commits, "a release away from the control committed it");
    });

    [TestMethod]
    [DataRow("Button")]
    [DataRow("ToggleButton")]
    [DataRow("CheckBox")]
    [DataRow("RadioButton")]
    [DataRow("ToggleSwitch")]
    public Task ReturnAndRelease_CommitsOnce(string kind) => CaptureScene.RunAsync(async scene =>
    {
        var subject = PressSubject.Create(kind);
        var (window, _) = await ShowWithOtherButtonAsync(scene, subject.Control);

        await scene.PressAndLeaveAsync(window, subject.Control);
        await scene.Input.MoveAsync(window, CaptureScene.Center(subject.Control));
        await scene.Input.ReleaseAsync(window, CaptureScene.Center(subject.Control));

        Assert.AreEqual(1, subject.Commits, "a release over the control after returning did not commit it once");
        Assert.IsFalse(subject.Control.IsMouseCaptured, "the control kept the element capture after the release");
        scene.CheckPlatformCaptureFree(window, "after the release");
    });

    [TestMethod]
    public Task TabHeaderReleaseAway_EndsTheCapture() => CaptureScene.RunAsync(async scene =>
    {
        var tabs = new TabControl { Width = 300, Height = 160 };
        tabs.AddTab(new TabItem { Header = new TextBlock { Text = "first" }, Content = new Border() });
        tabs.AddTab(new TabItem { Header = new TextBlock { Text = "second" }, Content = new Border() });
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Add(tabs);
        var window = await scene.ShowAsync(panel);

        var header = (UIElement)VisualTree.Find(tabs, static element => element is TabHeaderButton && element.Bounds.Width > 0)!;
        await scene.PressAndLeaveAsync(window, header);
        await scene.Input.ReleaseAsync(window, CaptureScene.Away(window));

        Assert.IsFalse(header.IsMouseCaptured, "the tab header kept the element capture after the release");
        scene.CheckPlatformCaptureFree(window, "after the release");
    });

    [TestMethod]
    public Task KeyboardPress_IgnoresTheMouseLeaving() => CaptureScene.RunAsync(async scene =>
    {
        var button = new Button { Content = new TextBlock { Text = "button" } };
        var (window, _) = await ShowWithOtherButtonAsync(scene, button);
        int clicks = 0;
        button.Click += () => clicks++;
        button.Focus();

        await scene.SpaceAsync(window, isDown: true);
        Assert.IsTrue(button.IsPressed, "precondition: Space pressed the button");

        await scene.Input.MoveAsync(window, CaptureScene.Center(button));
        await scene.Input.MoveAsync(window, CaptureScene.Away(window));
        Assert.IsTrue(button.IsPressed, "the mouse leaving cancelled a keyboard press");

        await scene.SpaceAsync(window, isDown: false);
        Assert.AreEqual(1, clicks, "the keyboard press did not click once");
    });

    [TestMethod]
    public Task ToggleButtonMouseUpMarkedHandled_StillReleasesTheCapture() => CaptureScene.RunAsync(async scene =>
    {
        var toggle = new ToggleButton { Content = new TextBlock { Text = "toggle" } };
        var (window, _) = await ShowWithOtherButtonAsync(scene, toggle);
        toggle.MouseUp += static e => e.Handled = true;

        var center = CaptureScene.Center(toggle);
        await scene.Input.MoveAsync(window, center);
        await scene.Input.PressAsync(window, center);
        await scene.Input.ReleaseAsync(window, center);

        Assert.IsFalse(toggle.IsMouseCaptured, "a handled mouse-up stranded the element capture");
        scene.CheckPlatformCaptureFree(window, "after a handled mouse-up");
        Assert.IsFalse(toggle.IsChecked, "a handled mouse-up still toggled");
    });

    private static async Task<(Window Window, Button Other)> ShowWithOtherButtonAsync(CaptureScene scene, Control control)
    {
        control.Width = 140;
        control.Height = 36;
        control.HorizontalAlignment = HorizontalAlignment.Left;
        var other = CaptureScene.NewButton("other");
        var panel = new StackPanel { Margin = new Thickness(20), Spacing = 16 };
        panel.Add(control);
        panel.Add(other);
        var window = await scene.ShowAsync(panel);
        return (window, other);
    }

    /// <summary>A press control and how many times a release has committed it (clicked, checked or toggled).</summary>
    private sealed class PressSubject(Control control, Func<int>? commits)
    {
        public Control Control { get; } = control;

        public int Commits => commits?.Invoke() ?? 0;

        public static PressSubject Create(string kind)
        {
            switch (kind)
            {
                case "Button":
                {
                    var button = new Button { Content = new TextBlock { Text = "button" } };
                    int clicks = 0;
                    button.Click += () => clicks++;
                    return new PressSubject(button, () => clicks);
                }
                case "ToggleButton":
                {
                    var toggle = new ToggleButton { Content = new TextBlock { Text = "toggle" } };
                    return new PressSubject(toggle, () => toggle.IsChecked ? 1 : 0);
                }
                case "CheckBox":
                {
                    var check = new CheckBox();
                    return new PressSubject(check, () => check.IsChecked == true ? 1 : 0);
                }
                case "RadioButton":
                {
                    var radio = new RadioButton();
                    return new PressSubject(radio, () => radio.IsChecked ? 1 : 0);
                }
                case "ToggleSwitch":
                {
                    var toggleSwitch = new ToggleSwitch();
                    return new PressSubject(toggleSwitch, () => toggleSwitch.IsChecked ? 1 : 0);
                }
                case "SegmentButton":
                    return new PressSubject(new SegmentButton(), null);
                case "RepeatButton":
                    return new PressSubject(new RepeatButton { Content = new TextBlock { Text = "repeat" } }, null);
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, "not a press control this suite covers");
            }
        }
    }
}
