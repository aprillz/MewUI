extern alias MewVGWin32;

using System.Diagnostics;
using System.Globalization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

/// <summary>
/// Measures a frame that replays every recording of a page of ordinary controls against a frame that
/// draws the same page straight from the visuals. Nothing changes between frames, so the difference is
/// what replaying costs over drawing. It reports numbers and asserts none. Runs only when
/// MEWUI_WHOLE_FRAME_COST is 1.
/// Not parallelizable: timing, and the process-wide graphics factory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedWholeFrameCostTests
{
    private const int WIDTH = 1200;
    private const int HEIGHT = 800;
    private const int FRAMES = 200;
    private const int WARM_UP_FRAMES = 300;

    [TestMethod]
    [DataRow("Gdi")]
    [DataRow("MewVG")]
    [DataRow("Direct2D")]
    public void MeasureWholeFrames(string backend)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("MEWUI_WHOLE_FRAME_COST") != "1")
        {
            Assert.Inconclusive("Set MEWUI_WHOLE_FRAME_COST=1 on Windows to run the whole frame cost measurement.");
            return;
        }

        IGraphicsFactory factory = backend switch
        {
            "Gdi" => new GdiGraphicsFactory(),
            "Direct2D" => new Aprillz.MewUI.Rendering.Direct2D.Direct2DGraphicsFactory(),
            _ => new MewVGWin32GraphicsFactory(),
        };
        using var disposable = factory as IDisposable;
        Application.DefaultGraphicsFactory = factory;
        using var renderScope = factory is MewVGWin32GraphicsFactory ? factory.AcquireBackgroundRenderScope() : null;

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        var backdrop = new Border { Background = Color.FromArgb(255, 250, 250, 250), Child = BuildPage() };
        window.Content = backdrop;
        window.PerformLayout();
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));

        // Long enough for the runtime to finish tiering up both ways of drawing: whichever is measured cold reads twice as slow.
        for (int warm = 0; warm < WARM_UP_FRAMES; warm++)
        {
            backdrop.Background = Color.FromArgb(255, 250, 250, (byte)(240 + (warm & 7)));
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
            window.RenderReferenceFrameToSurface(reference);
        }

        var replayed = new double[FRAMES];
        window.RetainedStatistics!.Reset();
        int wholeFrames = 0;
        for (int frame = 0; frame < FRAMES; frame++)
        {
            // The backdrop under everything changes, so the frame repaints all of it and records only the backdrop.
            backdrop.Background = Color.FromArgb(255, 250, 250, (byte)(240 + (frame & 7)));
            window.PerformLayout();
            long start = Stopwatch.GetTimestamp();
            window.RenderFrameToSurface(surface);
            replayed[frame] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            wholeFrames += window.LastRetainedDamage == null ? 1 : 0;
        }

        var replayedStats = window.LastFrameStats;
        int replays = window.RetainedStatistics.ContentReplayCount / FRAMES;
        int records = window.RetainedStatistics.ContentRecordCount;
        int groups = window.RetainedStatistics.GroupSurfaceCount / FRAMES;

        var direct = new double[FRAMES];
        for (int frame = 0; frame < FRAMES; frame++)
        {
            backdrop.Background = Color.FromArgb(255, 250, 250, (byte)(240 + (frame & 7)));
            window.PerformLayout();
            long start = Stopwatch.GetTimestamp();
            window.RenderReferenceFrameToSurface(reference);
            direct[frame] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"""

            === whole frames ({backend}, {FRAMES} frames, {replays} recordings replayed a frame, {records} recorded in all, {groups} group surfaces a frame, {wholeFrames} whole frames) ===
            replayed : median {Median(replayed):0.000} ms, {replayedStats.DrawCalls} drawing calls of which {replayedStats.CullCount} culled
            straight : median {Median(direct):0.000} ms, {window.LastFrameStats.DrawCalls} drawing calls of which {window.LastFrameStats.CullCount} culled
            """));
    }

    private static double Median(double[] samples)
    {
        var sorted = (double[])samples.Clone();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    private static UIElement BuildPage()
    {
        var navigation = new StackPanel { Orientation = Orientation.Vertical, Width = 220 };
        for (int index = 0; index < 22; index++)
        {
            navigation.Children(new Button { Content = new TextBlock { Text = "Section " + index }, Margin = new Thickness(2) });
        }

        var cards = new WrapPanel();
        for (int card = 0; card < 9; card++)
        {
            var column = new StackPanel { Orientation = Orientation.Vertical, Width = 280, Margin = new Thickness(8) };
            column.Children(new TextBlock { Text = "Card " + card });
            for (int row = 0; row < 3; row++)
            {
                column.Children(new Button { Content = new TextBlock { Text = $"Button {card}.{row}" }, Margin = new Thickness(2) });
            }

            column.Children(new CheckBox { Content = new TextBlock { Text = "Option " + card }, IsChecked = (card & 1) == 0 });
            column.Children(new ProgressBar { Value = 10 + (card * 9), Height = 8, Margin = new Thickness(2) });
            column.Children(new Slider { Minimum = 0, Maximum = 100, Value = card * 11 });
            cards.Children(column);
        }

        var body = new DockPanel();
        DockPanel.SetDock(navigation, Dock.Left);
        body.Children(navigation, new ScrollViewer { Content = cards });
        return body;
    }
}
