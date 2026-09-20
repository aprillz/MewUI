using System.Diagnostics;
using System.Globalization;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Measures what a grid pays per row for realizing, scrolling and hovering, in a window so that styles
/// and visual states are resolved as they are in an application. It reports numbers and asserts none:
/// two builds are compared by running it on each. Runs only when MEWUI_GRIDVIEW_COST is 1.
/// Not parallelizable: timing, and the process-wide graphics factory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class GridViewRowCostTests
{
    private const int ITEM_COUNT = 1000;
    private const int COLUMN_COUNT = 4;
    private const int REALIZE_ROUNDS = 40;
    private const int SCROLL_STEPS = 300;
    private const int HOVER_MOVES = 300;
    private const double ROWS_PER_SCROLL_STEP = 10;

    [TestMethod]
    public void MeasureRowCosts()
    {
        if (!OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("MEWUI_GRIDVIEW_COST") != "1")
        {
            Assert.Inconclusive("Set MEWUI_GRIDVIEW_COST=1 on Windows to run the grid row cost measurement.");
            return;
        }

        Application.DefaultGraphicsFactory = new Aprillz.MewUI.Rendering.Gdi.GdiGraphicsFactory();

        // Warm the styles, the templates and the JIT before anything is timed.
        for (int warm = 0; warm < 5; warm++)
        {
            CreateGrid(out _);
        }

        var realize = new List<(double Microseconds, long Bytes)>();
        int realizedRows = 0;
        for (int round = 0; round < REALIZE_ROUNDS; round++)
        {
            long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            var grid = CreateGrid(out _);
            realize.Add((Elapsed(start), GC.GetAllocatedBytesForCurrentThread() - bytesBefore));
            realizedRows = 0;
            grid.VisitRealizedRows((_, _) => realizedRows++);
        }

        var scrolled = CreateGrid(out var window);
        var scroll = new List<(double Microseconds, long Bytes)>();
        int target = 0;
        int direction = 1;
        for (int step = 0; step < SCROLL_STEPS; step++)
        {
            // Bringing a row ten further on into view moves the viewport by ten rows once it has left the first screen.
            target += direction * (int)ROWS_PER_SCROLL_STEP;
            if (target >= ITEM_COUNT || target < 0)
            {
                direction = -direction;
                target += 2 * direction * (int)ROWS_PER_SCROLL_STEP;
            }

            long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            scrolled.ScrollIntoView(target);
            window.PerformLayout();
            window.PerformLayout();
            scroll.Add((Elapsed(start), GC.GetAllocatedBytesForCurrentThread() - bytesBefore));
        }

        var hovered = CreateGrid(out var hoverWindow);
        var rows = new List<Rect>();
        hovered.VisitRealizedRows((_, row) => rows.Add(row.Bounds));
        var hover = new List<(double Microseconds, long Bytes)>();
        for (int move = 0; move < HOVER_MOVES; move++)
        {
            var hoveredRow = rows[1 + (move % (rows.Count - 2))];
            long bytesBefore = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            hoverWindow.SendMouseMove(new Point(hoveredRow.X + 40, hoveredRow.Y + (hoveredRow.Height / 2)));
            hoverWindow.PerformLayout();
            hover.Add((Elapsed(start), GC.GetAllocatedBytesForCurrentThread() - bytesBefore));
        }

        string configuration =
#if DEBUG
            "Debug";
#else
            "Release";
#endif
        Console.Error.WriteLine(string.Create(CultureInfo.InvariantCulture, $"""

            === grid row cost ({configuration}, {ITEM_COUNT} items x {COLUMN_COUNT} columns, {realizedRows} rows realized) ===
            row type: {typeof(GridViewRow).BaseType!.Name}
            realize a grid  : {Report(realize)}
            scroll 10 rows  : {Report(scroll)}
            move the hover  : {Report(hover)}
            """));
    }

    private static GridView CreateGrid(out Window window)
    {
        var grid = new GridView();
        grid.ItemsSource = ItemsView.Create(Enumerable.Range(0, ITEM_COUNT).Select(index => "Item " + index).ToArray());
        var columns = new GridViewColumn<string>[COLUMN_COUNT];
        for (int column = 0; column < COLUMN_COUNT; column++)
        {
            columns[column] = new GridViewColumn<string>
            {
                Header = "Column " + column,
                Width = 150,
                CellTemplate = new DelegateTemplate<string>(
                    build: _ => new TextBlock(),
                    bind: (view, item, _, _) => ((TextBlock)view).Text = item),
            };
        }

        grid.SetColumns(columns);
        window = HeadlessWindow.Create(800, 600);
        window.Content = grid;
        window.PerformLayout();
        window.PerformLayout();
        return grid;
    }

    private static double Elapsed(long start)
        => (Stopwatch.GetTimestamp() - start) * 1_000_000.0 / Stopwatch.Frequency;

    private static string Report(List<(double Microseconds, long Bytes)> samples)
    {
        var times = samples.Select(sample => sample.Microseconds).OrderBy(value => value).ToArray();
        var bytes = samples.Select(sample => sample.Bytes).OrderBy(value => value).ToArray();
        return string.Create(
            CultureInfo.InvariantCulture,
            $"median {times[times.Length / 2],9:0.0} us, p90 {times[(int)(times.Length * 0.9)],9:0.0} us, median {bytes[bytes.Length / 2],9} bytes");
    }
}
