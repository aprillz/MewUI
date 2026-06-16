using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts.Drawing.Geometries;
using Aprillz.MewUI.MewCharts.Views;

using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;

internal static partial class Samples
{
    // Scatter/Basic
    internal static FrameworkElement ScatterBasic() => new CartesianChart
    {
        Series =
        [
            new ScatterSeries<ObservablePoint>(new ObservablePoint[]
            {
                new(2.2, 5.4), new(3.6, 9.6), new(9.9, 5.2), new(8.1, 4.7), new(5.3, 7.1),
            }),
        ],
    };

    // Scatter/Bubbles
    internal static FrameworkElement ScatterBubbles() => new CartesianChart
    {
        Series =
        [
            new ScatterSeries<WeightedPoint>(new WeightedPoint[]
            {
                new(2.2, 5.4, 5), new(3.6, 9.6, 9), new(9.9, 5.2, 3), new(8.1, 4.7, 8), new(5.3, 7.1, 6),
            }) { MinGeometrySize = 10, GeometrySize = 50 },
        ],
    };

    // Scatter/AutoUpdate: a scatter whose points are added on a timer.
    internal static FrameworkElement ScatterAutoUpdate()
    {
        var values = new ObservableCollection<ObservablePoint>();
        for (var i = 0; i < 6; i++) values.Add(new ObservablePoint(Random.Next(0, 20), Random.Next(0, 20)));
        var chart = new CartesianChart { Series = [new ScatterSeries<ObservablePoint>(values)] };
        var timer = new DispatcherTimer().IntervalMs(1500).OnTick(() =>
        {
            values.Add(new ObservablePoint(Random.Next(0, 20), Random.Next(0, 20)));
            if (values.Count > 12) values.RemoveAt(0);
        });
        timer.Start();
        Timers.Add(timer);
        return chart;
    }

    // Scatter/Custom: scatter series using a circle marker and an SVG pin marker.
    internal static FrameworkElement ScatterCustom()
    {
        var r = new Random(7);
        ObservablePoint[] Fetch() => Enumerable.Range(0, 10).Select(_ => new ObservablePoint(r.Next(0, 20), r.Next(0, 20))).ToArray();
        return new CartesianChart
        {
            Series =
            [
                new ScatterSeries<ObservablePoint>(Fetch()) { GeometrySize = 30 },
                new ScatterSeries<ObservablePoint, SvgGeometry>(Fetch()) { GeometrySize = 40, GeometrySvg = SVGPoints.Pin },
            ],
        };
    }
}
