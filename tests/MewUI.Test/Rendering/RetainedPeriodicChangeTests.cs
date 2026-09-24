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

    private static void Frame(Window window, IRenderSurface surface)
    {
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
    }
}
