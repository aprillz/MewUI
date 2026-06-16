using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Drawing.Geometries;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;

internal static partial class Samples
{
    // Bars/Basic
    internal static FrameworkElement BarsBasic() => new CartesianChart
    {
        Series =
        [
            new ColumnSeries<double>(2, 5, 4) { Name = "Mary" },
            new ColumnSeries<double>(3, 1, 6) { Name = "Ana" },
        ],
        XAxes = [new Axis { Labels = ["Category 1", "Category 2", "Category 3"] }],
        LegendPosition = LegendPosition.Top,
    };

    // Bars/Spacing: a single column series with tightened padding.
    internal static FrameworkElement BarsSpacing() => new CartesianChart
    {
        Series = [new ColumnSeries<double>(20, 50, 40, 20, 40, 30, 50, 20, 50, 40) { Padding = 0 }],
    };

    // Bars/WithBackground: a translucent background series behind the data, both ignoring bar position.
    internal static FrameworkElement BarsWithBackground() => new CartesianChart
    {
        Series =
        [
            new ColumnSeries<double>(20, 50, 40, 20, 40, 30, 50, 20, 50, 40)
            {
                IgnoresBarPosition = true,
                Fill = new SolidColorPaint(new Color(50, 180, 180, 180)),
            },
            new ColumnSeries<double>(3, 10, 5, 3, 7, 3, 8) { IgnoresBarPosition = true },
        ],
    };

    // Bars/Layered: two overlapping column series, the front one narrower.
    internal static FrameworkElement BarsLayered() => new CartesianChart
    {
        Series =
        [
            new ColumnSeries<int>(6, 3, 5, 7, 3, 4, 6, 3) { IgnoresBarPosition = true, MaxBarWidth = double.MaxValue },
            new ColumnSeries<int>(2, 4, 8, 9, 5, 2, 4, 7) { IgnoresBarPosition = true, MaxBarWidth = 30 },
        ],
    };

    // Bars/RowsWithLabels: horizontal bars with data labels at End/Middle/Start.
    internal static FrameworkElement BarsRows()
    {
        var labels = new SolidColorPaint(new Color(255, 40, 40, 40));
        return new CartesianChart
        {
            Series =
            [
                new RowSeries<int>(8, -3, 4) { DataLabelsPaint = labels, DataLabelsSize = 14, DataLabelsPosition = DataLabelsPosition.End },
                new RowSeries<int>(4, -6, 5) { DataLabelsPaint = labels, DataLabelsSize = 14, DataLabelsPosition = DataLabelsPosition.Middle },
                new RowSeries<int>(6, -9, 3) { DataLabelsPaint = labels, DataLabelsSize = 14, DataLabelsPosition = DataLabelsPosition.Start },
            ],
        };
    }

    // Bars/Custom: columns using a custom rounded geometry and an SVG star geometry.
    internal static FrameworkElement BarsCustom() => new CartesianChart
    {
        Series =
        [
            new ColumnSeries<double>(2, 1, 4),
            new ColumnSeries<double, SvgGeometry>(4, 3, 6) { GeometrySvg = SVGPoints.Star },
            new ColumnSeries<double>(-2, 2, 1),
        ],
    };

    // Bars/DelayedAnimation: each bar's grow-in animation is staggered by its index (per-point
    // transition). "Restart" reassigns the values, replaying the staggered transition.
    internal static FrameworkElement BarsDelayedAnimation()
    {
        static float[] Make(double phase) => [.. Enumerable.Range(0, 30).Select(i => (float)(Math.Sin(i / 4.0 + phase) * 5 + 6))];

        var s1 = new DelayedColumnSeries(Make(0)) { Name = "A" };
        var s2 = new DelayedColumnSeries(Make(Math.PI)) { Name = "B" };
        var chart = new CartesianChart { Series = [s1, s2] };

        return WithActions(chart,
            ("Restart", () =>
            {
                var phase = Random.NextDouble() * Math.PI * 2;
                s1.Values = Make(phase);
                s2.Values = Make(phase + Math.PI);
            }
        ));
    }

    // Bars/AutoUpdate: a column series whose values change on a timer.
    internal static FrameworkElement BarsAutoUpdate()
    {
        var values = new ObservableCollection<double> { 2, 5, 4, 2, 6 };
        var chart = new CartesianChart { Series = [new ColumnSeries<double>(values)] };
        var timer = new DispatcherTimer().IntervalMs(1500).OnTick(() =>
        {
            values.Add(Random.Next(0, 10));
            if (values.Count > 8) values.RemoveAt(0);
        });
        timer.Start();
        Timers.Add(timer);
        return chart;
    }

    // Bars/Race: horizontal bars whose values change on a timer (highest sorts to the top).
    internal static FrameworkElement BarsRace()
    {
        var pilots = new ObservableCollection<ObservableValue>(
            [new(Random.Next(0, 100)), new(Random.Next(0, 100)), new(Random.Next(0, 100)), new(Random.Next(0, 100)), new(Random.Next(0, 100))]);
        var chart = new CartesianChart
        {
            Series = [new RowSeries<ObservableValue>(pilots) { DataLabelsPaint = new SolidColorPaint(new Color(255, 255, 255, 255)), DataLabelsPosition = DataLabelsPosition.End }],
            XAxes = [new Axis { SeparatorsPaint = null }],
            YAxes = [new Axis { IsVisible = false }],
        };
        var timer = new DispatcherTimer().IntervalMs(1500).OnTick(() =>
        {
            foreach (var pilot in pilots) pilot.Value = Random.Next(0, 100);
        });
        timer.Start();
        Timers.Add(timer);
        return chart;
    }
}
