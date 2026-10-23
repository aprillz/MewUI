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
/// Measures what a scroll frame costs in the retained window: the layout pass, the frame, the rows drawn
/// again, the recordings made, for plain content in a ScrollViewer and for virtualized lists of rows with
/// wrapped text. Reports numbers and asserts none. Runs only when MEWUI_SCROLL_COST is 1.
/// Not parallelizable: timing, and the process-wide graphics factory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedScrollCostTests
{
    private const int WIDTH = 900;
    private const int HEIGHT = 700;
    private const int FRAMES = 240;
    private const int WARM_UP_FRAMES = 240;
    private const int PLAIN_ROW_COUNT = 400;
    private const int VIRTUAL_ROW_COUNT = 10000;
    private const double FLING_STEP = 3.37;
    private const double WHEEL_STEP = 48;

    private const string SENTENCE = "The quick brown fox jumps over the lazy dog while the retained scene keeps every recording it can. ";

    private sealed class Row : ContentControl
    {
        internal int RenderCount;
        internal int MeasureCount;
        internal int ArrangeCount;
        internal int Index = -1;

        protected override Size MeasureContent(Size availableSize)
        {
            MeasureCount++;
            return base.MeasureContent(availableSize);
        }

        protected override void ArrangeContent(Rect bounds)
        {
            ArrangeCount++;
            base.ArrangeContent(bounds);
        }

        protected override void OnRender(IGraphicsContext context)
        {
            RenderCount++;
            base.OnRender(context);
        }
    }

    private sealed class Scenario
    {
        public required string Name;
        public required UIElement Content;
        public required List<Row> Rows;
        public required Action<double> ScrollTo;
    }

    [TestMethod]
    [DataRow("Gdi")]
    [DataRow("Direct2D")]
    [DataRow("MewVG")]
    public void MeasureScrollFrames(string backend)
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("MEWUI_SCROLL_COST") != "1")
        {
            Assert.Inconclusive("Set MEWUI_SCROLL_COST=1 on Windows to run the scroll cost measurement.");
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

        foreach (var build in new Func<Scenario>[] { BuildPlain, BuildVariable, BuildFixed })
        {
            foreach (double step in new[] { FLING_STEP, WHEEL_STEP })
            {
                var scenario = build();
                Measure(factory, backend, scenario, step);
            }
        }
    }

    private static void Measure(IGraphicsFactory factory, string backend, Scenario scenario, double step)
    {
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = scenario.Content;
        window.PerformLayout();
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));

        double offset = 0;
        for (int warm = 0; warm < WARM_UP_FRAMES; warm++)
        {
            offset += step;
            scenario.ScrollTo(offset);
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
            window.RenderReferenceFrameToSurface(reference);
        }

        scenario.ScrollTo(0);
        for (int settle = 0; settle < 3; settle++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        var rows = scenario.Rows;
        var layoutMs = new double[FRAMES];
        var frameMs = new double[FRAMES];
        var dirtyShare = new double[FRAMES];
        long rowsDrawn = 0;
        long rowsRedrawn = 0;
        long rowsMeasured = 0;
        long rowsArranged = 0;
        long records = 0;
        long replays = 0;
        int wholeFrames = 0;
        var statistics = window.RetainedStatistics!;
        offset = 0;
        for (int frame = 0; frame < FRAMES; frame++)
        {
            var before = rows.Select(row => (row.Index, row.RenderCount, row.MeasureCount, row.ArrangeCount)).ToArray();
            int recordsBefore = statistics.ContentRecordCount;
            int replaysBefore = statistics.ContentReplayCount;

            offset += step;
            scenario.ScrollTo(offset);
            long start = Stopwatch.GetTimestamp();
            window.PerformLayout();
            layoutMs[frame] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = Stopwatch.GetTimestamp();
            window.RenderFrameToSurface(surface);
            frameMs[frame] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;

            records += statistics.ContentRecordCount - recordsBefore;
            replays += statistics.ContentReplayCount - replaysBefore;
            var dirty = window.LastRetainedDirtyRect;
            dirtyShare[frame] = dirty == null ? 1.0 : dirty.Value.Width * dirty.Value.Height / (WIDTH * (double)HEIGHT);
            wholeFrames += dirty == null ? 1 : 0;

            for (int index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                int drawn = row.RenderCount - before[index].RenderCount;
                rowsDrawn += drawn;
                if (drawn > 0 && before[index].RenderCount > 0 && before[index].Index == row.Index)
                {
                    rowsRedrawn += drawn;
                }

                rowsMeasured += row.MeasureCount - before[index].MeasureCount;
                rowsArranged += row.ArrangeCount - before[index].ArrangeCount;
            }
        }

        int onScreen = rows.Count(row => row.Parent != null && row.Bounds.Bottom > 0 && row.Bounds.Y < HEIGHT && row.Bounds.Width > 0);

        var straightMs = new double[FRAMES];
        for (int frame = 0; frame < FRAMES; frame++)
        {
            offset += step;
            scenario.ScrollTo(offset);
            window.PerformLayout();
            long start = Stopwatch.GetTimestamp();
            window.RenderReferenceFrameToSurface(reference);
            straightMs[frame] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        var idleMs = new double[FRAMES];
        for (int frame = 0; frame < FRAMES; frame++)
        {
            long start = Stopwatch.GetTimestamp();
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
            idleMs[frame] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"""

            === scroll cost ({backend}, {scenario.Name}, {step} DIP a frame, {FRAMES} frames, {onScreen} rows on screen) ===
            layout   : median {Median(layoutMs):0.000} ms, mean {layoutMs.Average():0.000} ms; rows measured {rowsMeasured / (double)FRAMES:0.0} and arranged {rowsArranged / (double)FRAMES:0.0} a frame
            frame    : median {Median(frameMs):0.000} ms, mean {frameMs.Average():0.000} ms; {replays / (double)FRAMES:0.0} recordings replayed and {records / (double)FRAMES:0.00} recorded a frame; dirty {Median(dirtyShare) * 100:0}% of the window, {wholeFrames} whole frames
            rows     : {rowsDrawn / (double)FRAMES:0.00} drawn a frame, of which {rowsRedrawn / (double)FRAMES:0.00} were on screen already with the same item
            straight : median {Median(straightMs):0.000} ms (the frame drawn from the visuals, no recordings)
            idle     : median {Median(idleMs):0.000} ms (layout and frame with nothing changed)
            """));
    }

    private static double Median(double[] samples)
    {
        var sorted = (double[])samples.Clone();
        Array.Sort(sorted);
        return sorted[sorted.Length / 2];
    }

    private static Row CreateRow()
    {
        var title = new TextBlock { FontSize = 14 };
        var body = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        stack.Children(title, body);
        return new Row
        {
            Content = stack,
            Padding = new Thickness(8),
            BorderThickness = 1,
            BorderBrush = Color.FromArgb(255, 200, 200, 200),
            Background = Color.FromArgb(255, 250, 250, 250),
        };
    }

    private static void BindRow(Row row, int index, bool wrapped)
    {
        var stack = (StackPanel)row.Content!;
        var title = (TextBlock)stack.Children[0];
        var body = (TextBlock)stack.Children[1];
        row.Index = index;
        title.Text = "Message " + index;
        body.Text = wrapped ? string.Concat(Enumerable.Repeat(SENTENCE, 1 + (index % 4))) : SENTENCE;
    }

    private static Scenario BuildPlain()
    {
        var rows = new List<Row>();
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        for (int index = 0; index < PLAIN_ROW_COUNT; index++)
        {
            var row = CreateRow();
            BindRow(row, index, wrapped: true);
            rows.Add(row);
            stack.Children(row);
        }

        var viewer = new ScrollViewer { VerticalScroll = ScrollMode.Visible, Content = stack };
        return new Scenario { Name = "plain rows in a ScrollViewer", Content = viewer, Rows = rows, ScrollTo = y => viewer.SetScrollOffsets(0, y) };
    }

    private static Scenario BuildVariable()
    {
        var rows = new List<Row>();
        var list = new ItemsControl().VariableHeightPresenter();
        list.ItemsSource = new ItemsView<int>(Enumerable.Range(0, VIRTUAL_ROW_COUNT).ToArray());
        list.ItemTemplate = new DelegateTemplate<object?>(
            build: _ =>
            {
                var row = CreateRow();
                rows.Add(row);
                return row;
            },
            bind: (view, item, index, _) => BindRow((Row)view, (int)item!, wrapped: true));
        return new Scenario { Name = "variable height virtualized rows", Content = list, Rows = rows, ScrollTo = y => list.SetScrollOffsets(0, y) };
    }

    private static Scenario BuildFixed()
    {
        var rows = new List<Row>();
        var list = new ItemsControl().FixedHeightPresenter(56);
        list.ItemsSource = new ItemsView<int>(Enumerable.Range(0, VIRTUAL_ROW_COUNT).ToArray());
        list.ItemTemplate = new DelegateTemplate<object?>(
            build: _ =>
            {
                var row = CreateRow();
                rows.Add(row);
                return row;
            },
            bind: (view, item, index, _) => BindRow((Row)view, (int)item!, wrapped: false));
        return new Scenario { Name = "fixed height virtualized rows", Content = list, Rows = rows, ScrollTo = y => list.SetScrollOffsets(0, y) };
    }
}
