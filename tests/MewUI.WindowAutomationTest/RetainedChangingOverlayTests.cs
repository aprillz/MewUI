using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// An overlay that draws something else every frame across the whole window must not make the window
/// replay its body every frame. Once the overlay has kept changing, it is left out of the kept frame and
/// drawn over it on the way to the screen, and it goes back into the kept frame when it settles.
/// </summary>
[TestClass]
public sealed class RetainedChangingOverlayTests
{
    private const int WARM_UP_MS = 3000;
    private const int MEASURE_MS = 1500;

    [TestMethod]
    public Task AnOverlayThatChangesEveryFrame_DoesNotReplayTheBody() => CaptureScene.RunAsync(async scene =>
    {
        var body = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(12) };
        for (int index = 0; index < 8; index++)
        {
            body.Children(new Button { Content = new TextBlock { Text = "Button " + index }, Margin = new Thickness(2) });
        }

        var window = await scene.ShowAsync(new Border { Background = Color.FromArgb(255, 250, 250, 250), Child = body });
        if (window.GraphicsFactory is not IPersistentFrameGraphicsFactory { IsPersistentFrameRenderingVerified: true, DrawsWindowFramesInPlace: false })
        {
            Assert.Inconclusive($"{window.GraphicsFactory.Backend} draws window frames in place, so nothing is drawn over a kept frame.");
        }

        var overlay = new MovingOverlay();
        window.OverlayLayer.Add(overlay);
        overlay.Start();
        try
        {
            await Task.Delay(WARM_UP_MS);
            Assert.IsGreaterThan(35, overlay.RenderCount, $"precondition: the overlay animates ({overlay.RenderCount} frames in {WARM_UP_MS} ms)");

            window.RetainedStatistics!.Reset();
            int framesBefore = overlay.RenderCount;
            await Task.Delay(MEASURE_MS);
            int frames = overlay.RenderCount - framesBefore;
            int replays = window.RetainedStatistics.ContentReplayCount;

            Assert.IsGreaterThan(8, frames, $"precondition: frames kept coming ({frames})");
            Assert.IsLessThanOrEqualTo(
                frames / 4,
                replays,
                $"{window.GraphicsFactory.Backend}: {frames} frames of a changing overlay replayed {replays} recordings of the body under it");

            if (OperatingSystem.IsWindows())
            {
                var shot = ScreenCapture.OfClientArea(window.Handle);
                Assert.IsGreaterThan(50, CountOf(shot.Bgra, MovingOverlay.Ink), "the overlay is not on the screen");
            }
        }
        finally
        {
            overlay.Stop();
        }

        // Settled, it goes back into the kept frame and stays on the screen without being drawn again.
        await Task.Delay(600);
        int rendersAfterSettling = overlay.RenderCount;
        body.Children(new Button { Content = new TextBlock { Text = "one more" }, Margin = new Thickness(2) });
        await Task.Delay(400);
        body.Children(new Button { Content = new TextBlock { Text = "and another" }, Margin = new Thickness(2) });
        await Task.Delay(400);
        Assert.IsLessThanOrEqualTo(
            2,
            overlay.RenderCount - rendersAfterSettling,
            $"a settled overlay was drawn {overlay.RenderCount - rendersAfterSettling} more times while only the body changed");

        if (OperatingSystem.IsWindows())
        {
            var shot = ScreenCapture.OfClientArea(window.Handle);
            Assert.IsGreaterThan(50, CountOf(shot.Bgra, MovingOverlay.Ink), "the settled overlay left the screen");
        }
    });

    private static int CountOf(byte[] bgra, Color color)
    {
        int count = 0;
        for (int offset = 0; offset + 3 < bgra.Length; offset += 4)
        {
            if (Math.Abs(bgra[offset] - color.B) <= 2 && Math.Abs(bgra[offset + 1] - color.G) <= 2 && Math.Abs(bgra[offset + 2] - color.R) <= 2)
            {
                count++;
            }
        }

        return count;
    }

    private sealed class MovingOverlay : FrameworkElement
    {
        internal static readonly Color Ink = Color.FromArgb(255, 230, 40, 200);

        private AnimationClock? _clock;
        private int _step;

        public MovingOverlay() => IsHitTestVisible = false;

        internal int RenderCount { get; private set; }

        internal void Start()
        {
            _clock = new AnimationClock(TimeSpan.FromSeconds(1)) { RepeatCount = -1 };
            _clock.TickCallback = _ =>
            {
                _step++;
                InvalidateVisual();
            };
            _clock.Start();
        }

        internal void Stop()
        {
            if (_clock != null)
            {
                _clock.TickCallback = null;
                _clock.Stop();
                _clock = null;
            }
        }

        protected override void OnRender(IGraphicsContext context)
        {
            RenderCount++;
            var bounds = Bounds;
            for (int index = 0; index < 12; index++)
            {
                double left = bounds.X + ((index * 37 + _step * 3) % Math.Max(1, bounds.Width - 12));
                double top = bounds.Y + ((index * 53 + _step * 2) % Math.Max(1, bounds.Height - 12));
                context.FillRectangle(new Rect(left, top, 10, 10), Ink);
            }
        }
    }
}
