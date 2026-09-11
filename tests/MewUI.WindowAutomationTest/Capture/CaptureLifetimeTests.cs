using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A capture taken while a press is routed ends when that button is released, whether or not the holder
/// releases it. An element capture also ends when the window deactivates, is disabled for a modal, or
/// starts a system move.
/// </summary>
[TestClass]
public sealed class CaptureLifetimeTests
{
    [TestMethod]
    public Task PressCapture_ThatTheHolderFailsToRelease_EndsOnRelease() => CaptureScene.RunAsync(async scene =>
    {
        var holder = new LeakyPressControl { Width = 140, Height = 36, HorizontalAlignment = HorizontalAlignment.Left, Content = new TextBlock { Text = "leaky" } };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Add(holder);
        var window = await scene.ShowAsync(panel);

        await scene.PressAndLeaveAsync(window, holder);
        await scene.Input.ReleaseAsync(window, CaptureScene.Away(window));

        Assert.IsNull(window.CapturedElement, "the release did not end the press capture");
        scene.CheckPlatformCaptureFree(window, "after the release");
    });

    [TestMethod]
    public Task CaptureTakenOutsideAPress_IsNotEndedByAClick() => CaptureScene.RunAsync(async scene =>
    {
        var target = new Border { Width = 140, Height = 36, HorizontalAlignment = HorizontalAlignment.Left };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Add(target);
        var window = await scene.ShowAsync(panel);

        window.CaptureMouse(target);
        await scene.ClickAsync(window, target);

        Assert.IsTrue(target.IsMouseCaptured, "a capture the press did not take ended with the press");
        window.ReleaseMouseCapture();
        scene.CheckPlatformCaptureFree(window, "after the explicit release");
    });

    [TestMethod]
    public Task Deactivation_EndsAnElementCapture() => CaptureScene.RunAsync(async scene =>
    {
        var (window, slider) = await scene.ShowSliderAsync();
        var other = await scene.ShowAsync(new Border());
        await scene.Input.ActivateAsync(window);

        await scene.PressSliderAsync(window, slider);
        await scene.Input.DeactivateAsync(window, other);

        Assert.IsFalse(window.IsActive, "precondition: the window deactivated");
        Assert.IsNull(window.CapturedElement, "an inactive window kept routing input to the captured element");

        await scene.Input.ReleaseAsync(window, CaptureScene.Away(window));
        scene.CheckPlatformCaptureFree(window, "after the release");
    });

    [TestMethod]
    public Task ModalDialog_EndsAnElementCapture() => CaptureScene.RunAsync(async scene =>
    {
        var (window, slider) = await scene.ShowSliderAsync();

        await scene.PressSliderAsync(window, slider);

        var dialog = new Window
        {
            Title = "modal",
            WindowSize = WindowSize.Fixed(200, 120),
            Content = new Border(),
        };
        _ = dialog.ShowDialogAsync(window);
        await Task.Delay(300);

        Assert.IsNull(window.CapturedElement, "a window disabled for a modal kept its element capture");

        dialog.Close();
        await Task.Delay(200);
        await scene.Input.ReleaseAsync(window, CaptureScene.Away(window));
        scene.CheckPlatformCaptureFree(window, "after the release");
    });

    [TestMethod]
    public Task DragMove_EndsTheElementCaptureBeforeTheSystemMoveStarts() => CaptureScene.RunAsync(async scene =>
    {
        var grip = new DragMoveGrip { Width = 140, Height = 36, HorizontalAlignment = HorizontalAlignment.Left };
        var panel = new StackPanel { Margin = new Thickness(20) };
        panel.Add(grip);
        var window = await scene.ShowAsync(panel);

        var center = CaptureScene.Center(grip);
        var moved = new Point(center.X + 40, center.Y + 30);
        await scene.Input.MoveAsync(window, center);
        await scene.Input.PressAsync(window, center);
        await scene.Input.MoveAsync(window, moved);
        await scene.Input.ReleaseAsync(window, moved);
        await Task.Delay(200);

        Assert.IsTrue(grip.MoveRequested, "precondition: the grip requested a system move");
        Assert.IsFalse(grip.CapturedWhenMoveStarted, "the system move started with an element capture still set");
        Assert.IsNull(window.CapturedElement, "the element capture outlived the system move");
        scene.CheckPlatformCaptureFree(window, "after the system move");
    });

    /// <summary>Captures on press and gates its own release on a flag its leave handler clears.</summary>
    private sealed class LeakyPressControl : ContentControl
    {
        private bool _armed;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButton.Left)
            {
                return;
            }

            _armed = true;
            (FindVisualRoot() as Window)?.CaptureMouse(this);
            e.Handled = true;
        }

        protected override void OnMouseLeave()
        {
            base.OnMouseLeave();
            _armed = false;
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButton.Left && _armed)
            {
                _armed = false;
                (FindVisualRoot() as Window)?.ReleaseMouseCapture();
            }
        }
    }

    /// <summary>A title-bar-like grip that captures on press and hands the drag to the platform move.</summary>
    private sealed class DragMoveGrip : ContentControl
    {
        public bool MoveRequested { get; private set; }

        public bool CapturedWhenMoveStarted { get; private set; }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButton.Left || FindVisualRoot() is not Window window)
            {
                return;
            }

            window.CaptureMouse(this);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (MoveRequested || !IsMouseCaptured || FindVisualRoot() is not Window window)
            {
                return;
            }

            MoveRequested = true;
            window.DragMove();
            CapturedWhenMoveStarted = window.CapturedElement != null;
        }
    }
}
