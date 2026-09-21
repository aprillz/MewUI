using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// The retained path has to earn its keep on a real window, not only in an offscreen test that hands
/// the frame one change at a time. These run the loop continuously, which is where a frame that falls
/// back to the whole surface costs the most: an untouched window must repaint nothing every frame, and
/// a hover must repaint where the pointer is instead of the whole client area.
/// </summary>
[TestClass]
public sealed class RetainedPartialRepaintTests
{
    private const double BUTTON_WIDTH = 120;
    private const double BUTTON_HEIGHT = 32;

    [TestMethod]
    public Task AFreshWindow_LeavesTheLoopWaitingForRequests() => CaptureScene.RunAsync(async scene =>
    {
        var (window, _, _) = await ShowTwoButtonsAsync(scene);
        await Task.Delay(800);

        var loop = Application.Current.RenderLoopSettings;
        Assert.IsFalse(
            loop.IsContinuous,
            $"the loop still pulses after startup (user flag {loop.Continuous}, animation {loop.AnimationActive}, vsync {loop.VSyncEnabled})");

        window.ResetRetainedFrameCounts();
        await Task.Delay(400);
        var counts = window.RetainedFrames;
        Assert.AreEqual(
            0,
            counts.Whole + counts.Partial + counts.Untouched,
            $"an idle window drew {counts.Whole + counts.Partial + counts.Untouched} frames nobody asked for");
    });

    [TestMethod]
    public Task WhileRenderingContinuously_AnIdleWindow_RepaintsNothing() => CaptureScene.RunAsync(async scene =>
    {
        var (window, _, _) = await ShowTwoButtonsAsync(scene);

        await RenderingContinuouslyAsync(async () =>
        {
            await SettleAsync(window);
            window.ResetRetainedFrameCounts();
            await Task.Delay(600);

            var counts = window.RetainedFrames;
            Assert.IsGreaterThan(
                0,
                counts.Untouched,
                "the loop drew no frame at all, so this run says nothing about what an idle frame costs");
            Assert.AreEqual(
                0,
                counts.Whole,
                $"an idle window drew {counts.Whole} whole frames (partial {counts.Partial}, untouched {counts.Untouched})");
            Assert.AreEqual(
                0,
                counts.Partial,
                $"an idle window repainted part of {counts.Partial} frames (untouched {counts.Untouched})");
        });
    });

    [TestMethod]
    public Task WhileRenderingContinuously_AnIdleWindow_PutsNothingOnScreen() => CaptureScene.RunAsync(async scene =>
    {
        var (window, first, _) = await ShowTwoButtonsAsync(scene);
        if (!window.PlatformReportsLostFrames)
        {
            Assert.Inconclusive("This platform does not say when a window loses the frame it shows, so every frame is presented.");
        }

        await RenderingContinuouslyAsync(async () =>
        {
            await scene.Input.MoveAsync(window, CaptureScene.Away(window));
            await SettleAsync(window);

            var before = window.PresentCounts;
            await Task.Delay(600);
            var idle = window.PresentCounts;
            if (before == idle && window.RetainedFrames.Untouched == 0)
            {
                Assert.Inconclusive("The frames of this window do not go through a frame surface.");
            }

            Assert.AreEqual(
                0,
                idle.Presented - before.Presented,
                $"an idle window was put on screen {idle.Presented - before.Presented} times (skipped {idle.Skipped - before.Skipped})");
            Assert.IsGreaterThan(before.Skipped, idle.Skipped, "no frame was skipped, so this run says nothing");

            // A change after a run of skipped frames still has to reach the screen.
            await scene.Input.MoveAsync(window, CaptureScene.Center(first));
            await Task.Delay(300);
            var hovered = window.PresentCounts;
            Assert.IsGreaterThan(idle.Presented, hovered.Presented, "the hover was drawn but never put on screen");
        });
    });

    [TestMethod]
    public Task AnAnimationScrolledOutOfView_DrawsNoFrames() => CaptureScene.RunAsync(async scene =>
    {
        var rows = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6, Margin = new Thickness(12) };
        rows.Children(new ProgressBar { IsIndeterminate = true, Height = 8, Width = 200 });
        for (int index = 0; index < 60; index++)
        {
            rows.Children(new TextBlock { Text = $"row {index}" });
        }

        var scroll = new ScrollViewer { VerticalScroll = ScrollMode.Visible, Content = rows };
        var window = await scene.ShowAsync(scroll);
        await scene.Input.MoveAsync(window, CaptureScene.Away(window));
        await Task.Delay(500);

        window.ResetRetainedFrameCounts();
        await Task.Delay(400);
        var inView = window.RetainedFrames;
        Assert.IsGreaterThan(
            0,
            inView.Whole + inView.Partial,
            "the animation drew no frame while in view, so this run says nothing");

        scroll.SetScrollOffsets(0, 400);
        await Task.Delay(500);
        window.ResetRetainedFrameCounts();
        await Task.Delay(600);
        var outOfView = window.RetainedFrames;
        Assert.AreEqual(
            0,
            outOfView.Whole + outOfView.Partial + outOfView.Untouched,
            $"an animation nobody can see drew frames (whole {outOfView.Whole}, partial {outOfView.Partial}, untouched {outOfView.Untouched})");

        // Back in view it has to move again.
        scroll.SetScrollOffsets(0, 0);
        await Task.Delay(300);
        window.ResetRetainedFrameCounts();
        await Task.Delay(400);
        var back = window.RetainedFrames;
        Assert.IsGreaterThan(0, back.Whole + back.Partial, "the animation stayed still after it was scrolled back into view");
    });

    [TestMethod]
    public Task WhileRenderingContinuously_AHover_RepaintsOnlyWhereThePointerIs() => CaptureScene.RunAsync(async scene =>
    {
        var (window, first, second) = await ShowTwoButtonsAsync(scene);

        await RenderingContinuouslyAsync(async () =>
        {
            await scene.Input.MoveAsync(window, CaptureScene.Away(window));
            await SettleAsync(window);
            window.ResetRetainedFrameCounts();

            await scene.Input.MoveAsync(window, CaptureScene.Center(first));
            await Task.Delay(300);

            var counts = window.RetainedFrames;
            Assert.IsGreaterThan(
                0,
                counts.Partial,
                $"a hover repainted no frame in part (whole {counts.Whole}, untouched {counts.Untouched})");
            Assert.AreEqual(
                0,
                counts.Whole,
                $"a hover drew {counts.Whole} whole frames (partial {counts.Partial}, untouched {counts.Untouched})");

            var dirtyRect = window.LastRetainedDirtyRect;
            if (dirtyRect is Rect repainted && repainted.Width > 0)
            {
                double clientArea = window.ClientSize.Width * window.ClientSize.Height;
                Assert.IsFalse(
                    repainted.Contains(new Point(second.Bounds.X + 2, second.Bounds.Y + 2)),
                    $"the dirty region {repainted} covers the button the pointer never reached at {second.Bounds}");
                Assert.IsLessThan(
                    clientArea / 2,
                    repainted.Width * repainted.Height,
                    $"the dirty region {repainted} covers more than half of the {window.ClientSize} client area");
            }
        });
    });

    [TestMethod]
    public Task AHover_CopiesOnlyTheChangedAreaToTheWindow() => CaptureScene.RunAsync(async scene =>
    {
        var (window, first, _) = await ShowTwoButtonsAsync(scene);
        if (window.GraphicsFactory is not Aprillz.MewUI.Rendering.IPersistentFrameGraphicsFactory { WindowTargetKeepsPresentedFrame: true })
        {
            Assert.Inconclusive("This backend's window target does not keep the frame presented to it, so every frame is copied whole.");
        }

        await scene.Input.MoveAsync(window, CaptureScene.Away(window));
        await SettleAsync(window);

        await scene.Input.MoveAsync(window, CaptureScene.Center(first));
        await Task.Delay(400);

        double clientArea = window.ClientSize.Width * window.ClientSize.Height;
        Assert.IsTrue(
            window.LastPresentedArea > 0 && window.LastPresentedArea < clientArea / 2,
            $"a hover copied {window.LastPresentedArea} of the {clientArea} client area to the window");

        if (OperatingSystem.IsWindows())
        {
            // What reached the screen in parts has to be what a whole copy of the same frame shows.
            var inParts = ScreenCapture.OfClientArea(window.Handle);
            window.NotePresentedFrameLost();
            window.InvalidateVisual();
            await Task.Delay(400);
            var whole = ScreenCapture.OfClientArea(window.Handle);

            int differing = 0;
            for (int y = 0; y < whole.Height; y++)
            {
                for (int x = 0; x < whole.Width; x++)
                {
                    if (whole.At(x, y) != inParts.At(x, y))
                    {
                        differing++;
                    }
                }
            }

            Assert.AreEqual(0, differing, $"{differing} pixels differ between the frame copied in parts and the same frame copied whole");
        }
    });

    /// <summary>Runs the body with the render loop drawing every frame it can, as a profiled app does.</summary>
    private static async Task RenderingContinuouslyAsync(Func<Task> body)
    {
        var settings = Application.Current.RenderLoopSettings;
        bool previousContinuous = settings.Continuous;
        int previousTargetFps = settings.TargetFps;
        settings.Continuous = true;
        settings.TargetFps = 0;
        try
        {
            await body();
        }
        finally
        {
            settings.Continuous = previousContinuous;
            settings.TargetFps = previousTargetFps;
        }
    }

    /// <summary>Waits until the window stops repainting, so what follows is measured from a still frame.</summary>
    private static async Task SettleAsync(Window window)
    {
        for (int attempt = 0; attempt < 40; attempt++)
        {
            window.ResetRetainedFrameCounts();
            await Task.Delay(100);
            var counts = window.RetainedFrames;
            if (counts.Whole == 0 && counts.Partial == 0)
            {
                return;
            }
        }

        var settled = window.RetainedFrames;
        Assert.Inconclusive(
            $"the window never settled: whole {settled.Whole}, partial {settled.Partial}, untouched {settled.Untouched}");
    }

    private static async Task<(Window Window, Button First, Button Second)> ShowTwoButtonsAsync(CaptureScene scene)
    {
        var first = new Button
        {
            Content = new TextBlock { Text = "first" },
            Width = BUTTON_WIDTH,
            Height = BUTTON_HEIGHT,
            Margin = new Thickness(20, 20, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var second = new Button
        {
            Content = new TextBlock { Text = "second" },
            Width = BUTTON_WIDTH,
            Height = BUTTON_HEIGHT,
            Margin = new Thickness(20, 12, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };

        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(first, second);
        var window = await scene.ShowAsync(stack);
        return (window, first, second);
    }
}
