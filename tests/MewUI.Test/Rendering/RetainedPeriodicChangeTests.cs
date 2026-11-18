using System.Diagnostics;
using System.Reflection;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A visual that changes every frame is drawn straight from itself instead of being recorded. A caret
/// that blinks twice a second changes in every pass too when nothing else does, but its changes are half
/// a second apart: it is not animating, and its recording keeps the repaint to the caret.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedPeriodicChangeTests
{
    private const int WIDTH = 360;
    private const int HEIGHT = 240;
    private const int BLINKS = 60;
    private const double BLINK_INTERVAL_MS = 500;

    // An edge the repaint pads for antialiasing, in DIPs.
    private const double EDGE_SLACK = 2;

    [TestMethod]
    public void ACaretBlinkingForLong_KeepsRepaintingOnlyTheCaret()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        long now = Stopwatch.GetTimestamp();
        var box = new MultiLineTextBox
        {
            Text = string.Join("\n", Enumerable.Range(0, 10).Select(line => $"line {line}: the quick brown fox jumps")),
            Wrap = false,
        };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.RetainedClock = () => now;
        window.Content = box;
        window.PerformLayout();
        window.FocusManager.SetFocus(box);
        box.CaretPosition = 30;

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        for (int warm = 0; warm < 3; warm++)
        {
            Frame(window, surface);
        }

        var caret = box.GetCharRectInWindow(box.CaretPosition);
        var blink = typeof(TextBase).GetMethod("OnCaretBlink", BindingFlags.NonPublic | BindingFlags.Instance)!;
        for (int tick = 0; tick < BLINKS; tick++)
        {
            now += (long)(BLINK_INTERVAL_MS * Stopwatch.Frequency / 1000);
            blink.Invoke(box, null);
            Frame(window, surface);

            var dirty = window.LastRetainedDirtyRect;
            Assert.IsTrue(
                dirty is Rect area && area.Y >= caret.Y - EDGE_SLACK && area.Bottom <= caret.Bottom + EDGE_SLACK &&
                    area.X >= caret.X - EDGE_SLACK && area.Right <= caret.Right + EDGE_SLACK,
                $"blink {tick}: the caret at {caret} blinked and the frame repainted {dirty?.ToString() ?? "everything"} ({window.LastWholeFrameReason}); drawn live {window.RetainedStatistics!.LiveFallbackCount}");
        }
    }

    [TestMethod]
    public void PassesOfASlowAnimation_StillCountAsOneAnimation()
    {
        long now = 1_000_000;
        long millisecond = Stopwatch.Frequency / 1000;
        using var scene = new Aprillz.MewUI.Rendering.Retained.RenderScene { Clock = () => now };
        using var node = new Aprillz.MewUI.Rendering.Retained.VisualNode(new Border());

        // Sixty passes, each drawn in 120 ms and following the one before at once: slower than the
        // fixed gap allows, but as close together as an animation that takes this long to draw can be.
        for (int frame = 0; frame < 60; frame++)
        {
            int pass = scene.BeginPass();
            node.NoteContentChanged(pass, scene.PassTimestamp, scene.AnimationGapTicks, inkStaysInsideTheVisual: true);
            now += 120 * millisecond;
            scene.NoteFrameEnded();
        }

        Assert.IsTrue(
            node.ChangesEveryPass(scene.PassId + 1, scene.Clock(), scene.AnimationGapTicks),
            "an animation whose frames take 120 ms to draw was not taken for one");

        // The same passes half a second apart with nothing drawn in between are a periodic change.
        using var blinking = new Aprillz.MewUI.Rendering.Retained.VisualNode(new Border());
        for (int frame = 0; frame < 60; frame++)
        {
            now += 500 * millisecond;
            int pass = scene.BeginPass();
            blinking.NoteContentChanged(pass, scene.PassTimestamp, scene.AnimationGapTicks, inkStaysInsideTheVisual: true);
            now += millisecond;
            scene.NoteFrameEnded();
        }

        Assert.IsFalse(
            blinking.ChangesEveryPass(scene.PassId + 1, scene.Clock(), scene.AnimationGapTicks),
            "a change every half second was taken for an animation");
    }

    [TestMethod]
    public void PassesOfAnAnimationThatRecordsMostOfItsFrame_StillCountAsOneAnimation()
    {
        long now = 1_000_000;
        long millisecond = Stopwatch.Frequency / 1000;
        using var scene = new Aprillz.MewUI.Rendering.Retained.RenderScene { Clock = () => now };
        using var node = new Aprillz.MewUI.Rendering.Retained.VisualNode(new Border());

        // Each frame records for 100 ms before its pass opens and then draws for 10 ms: the frame is 110 ms of
        // work, of which the pass alone saw 10.
        for (int frame = 0; frame < 60; frame++)
        {
            scene.NoteFrameWorkStarted();
            now += 100 * millisecond;
            int pass = scene.BeginPass();
            node.NoteContentChanged(pass, scene.PassTimestamp, scene.AnimationGapTicks, inkStaysInsideTheVisual: true);
            now += 10 * millisecond;
            scene.NoteFrameEnded();
        }

        Assert.IsTrue(
            node.ChangesEveryPass(scene.PassId + 1, scene.Clock(), scene.AnimationGapTicks),
            "an animation whose frames spend most of their time recording was not taken for one");
    }

    private static void Frame(Window window, IRenderSurface surface)
    {
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
    }
}
