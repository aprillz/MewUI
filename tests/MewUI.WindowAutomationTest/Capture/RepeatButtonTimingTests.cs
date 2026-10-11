using System.Diagnostics;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

[TestClass]
public sealed class RepeatButtonTimingTests
{
    [TestMethod]
    [DataRow("release")]
    [DataRow("disable")]
    [DataRow("detach")]
    public Task HeldButton_RepeatsAfterDelayAndStops(string stopReason) => CaptureScene.RunAsync(async scene =>
    {
        var button = new RepeatButton { Width = 120, Height = 32, Delay = 450, Interval = 60 };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Add(button);
        var window = await scene.ShowAsync(panel);
        var elapsed = new Stopwatch();
        var clickTimes = new List<long>();
        button.Click += () =>
        {
            if (clickTimes.Count == 0)
            {
                elapsed.Start();
            }
            clickTimes.Add(elapsed.ElapsedMilliseconds);
        };

        var center = CaptureScene.Center(button);
        await scene.Input.MoveAsync(window, center);
        await scene.Input.PressAsync(window, center);
        Assert.IsTrue(button.IsMouseCaptured);
        Assert.IsNotEmpty(clickTimes, "Pressing must raise the initial click.");
        Assert.IsTrue(await CaptureScene.WaitUntilAsync(() => clickTimes.Count >= 3),
            "Holding must produce timer-driven clicks after the initial click.");
        Assert.IsGreaterThanOrEqualTo(420L, clickTimes[1], "The first repeat preceded the configured delay.");
        Assert.IsGreaterThanOrEqualTo(40L, clickTimes[2] - clickTimes[1], "The next repeat ignored the configured interval.");

        switch (stopReason)
        {
            case "release":
                await scene.Input.ReleaseAsync(window, center);
                break;
            case "disable":
                button.IsEnabled = false;
                break;
            case "detach":
                panel.Remove(button);
                break;
        }

        int stoppedAt = clickTimes.Count;
        await Task.Delay(240);
        Assert.AreEqual(stoppedAt, clickTimes.Count, $"Repeating continued after {stopReason}.");
        Console.Error.WriteLine($"RepeatButton {stopReason}: {string.Join(", ", clickTimes)}ms; stopped at {stoppedAt} clicks");
    });

    [TestMethod]
    public Task PointerAway_PausesRepeatingAndReturningResumesIt() => CaptureScene.RunAsync(async scene =>
    {
        var button = new RepeatButton { Width = 120, Height = 32, Delay = 200, Interval = 60 };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Add(button);
        var window = await scene.ShowAsync(panel);
        int clicks = 0;
        button.Click += () => clicks++;
        var center = CaptureScene.Center(button);
        await scene.Input.MoveAsync(window, center);
        await scene.Input.PressAsync(window, center);
        Assert.IsTrue(await CaptureScene.WaitUntilAsync(() => clicks >= 3));

        await scene.Input.MoveAsync(window, CaptureScene.Away(window));
        Assert.IsTrue(button.IsMouseCaptured);
        Assert.IsFalse(button.IsMouseOver);
        int pausedAt = clicks;
        await Task.Delay(240);
        Assert.AreEqual(pausedAt, clicks, "A held button must pause while the pointer is away.");

        await scene.Input.MoveAsync(window, center);
        Assert.IsTrue(await CaptureScene.WaitUntilAsync(() => clicks > pausedAt),
            "Repeating must resume when the pointer returns without a new press.");
        await scene.Input.ReleaseAsync(window, center);
        Assert.IsFalse(button.IsMouseCaptured);
        int releasedAt = clicks;
        await Task.Delay(240);
        Assert.AreEqual(releasedAt, clicks);
    });
}
