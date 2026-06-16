using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;

using LiveChartsCore.Kernel;
using LiveChartsCore.Measure;

internal static partial class Samples
{
    // Events/OverrideFind: a column chart with exact-match finding so the tooltip targets a single bar.
    internal static FrameworkElement EventsOverrideFind() => new CartesianChart
    {
        Series = [new ColumnSeries<double>(5, 6, 3, 8, 5, 2) { Name = "Series" }],
        FindingStrategy = FindingStrategy.ExactMatch,
        TooltipPosition = TooltipPosition.Top,
    };

    // Events/Tutorial (interactive): clicking a point toggles a highlight on it.
    internal static FrameworkElement EventsTutorialInteractive()
    {
        var highlight = new SolidColorPaint(new Color(255, 255, 193, 7));
        var active = new HashSet<ChartPoint>();
        var chart = new CartesianChart
        {
            Series =
            [
                new ColumnSeries<double>(1, 5, 4, 3) { Name = "Series 1" },
                new ColumnSeries<double>(3, 2, 6, 2) { Name = "Series 2" },
            ],
        };
        chart.DataPointerDown += (_, points) =>
        {
            foreach (var point in points)
            {
                if (point.Context.Visual is null) continue;
                if (active.Add(point)) point.Context.Visual.Fill = highlight;
                else { active.Remove(point); point.Context.Visual.Fill = null; }
            }
        };
        return WithActions(chart);
    }

    // Events/AddPointOnClick (interactive): clicking the plot adds a point; "Clear" resets it.
    internal static FrameworkElement EventsAddPointOnClickInteractive()
    {
        var chart = new ClickToAddChart();
        return WithActions(chart,
            ("Clear", () => { chart.Points.Clear(); }));
    }
}
