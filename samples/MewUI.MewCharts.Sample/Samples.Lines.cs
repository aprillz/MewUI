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
    // Lines/Basic
    internal static FrameworkElement LinesBasic() => new CartesianChart
    {
        Series =
        [
            new LineSeries<double>(2, 1, 3, 5, 3, 4, 6),
            new LineSeries<double>(4, 2, 5, 2, 4, 5, 3),
        ],
    };

    // Lines/Straight
    internal static FrameworkElement LinesStraight() => new CartesianChart
    {
        Series = [new LineSeries<double>(2, 1, 3, 5, 3, 4, 6) { LineSmoothness = 0 }],
    };

    // Lines/Area
    internal static FrameworkElement LinesArea() => new CartesianChart
    {
        Series = [new LineSeries<double>(2, 1, 3, 5, 3, 4, 6) { GeometrySize = 0 }],
    };

    // Lines/XY: a line plotted from explicit (X, Y) points.
    internal static FrameworkElement LinesXY() => new CartesianChart
    {
        Series = [new LineSeries<ObservablePoint>(new ObservablePoint[]
        {
            new(0, 4), new(1, 3), new(3, 8), new(18, 6), new(20, 12),
        })],
    };

    // Lines/Properties: a line with custom stroke/fill/geometry paints and smoothness.
    internal static FrameworkElement LinesProperties() => new CartesianChart
    {
        Series = [new LineSeries<double>(-2, -1, 3, 5, 3, 4, 6)
        {
            LineSmoothness = 0.5,
            GeometrySize = 20,
            Stroke = new SolidColorPaint(new Color(255, 0, 0, 0), 4),
            Fill = new SolidColorPaint(new Color(48, 0, 0, 0)),
            GeometryStroke = new SolidColorPaint(new Color(255, 0, 0, 0), 4),
            GeometryFill = new SolidColorPaint(new Color(48, 0, 0, 0)),
        }],
    };

    // Lines/Padding: extra data padding around the plotted line.
    internal static FrameworkElement LinesPadding() => new CartesianChart
    {
        Series = [new LineSeries<double>(2, 1, 3, 5, 3, 4, 6) { DataPadding = new LvcPoint(5, 5) }],
    };

    // Lines/Zoom
    internal static FrameworkElement LinesZoom() => new CartesianChart
    {
        Series = [new LineSeries<double>(2, 1, 3, 5, 3, 4, 6, 2, 5, 3, 7, 4)],
        ZoomMode = ZoomAndPanMode.X,
    };

    // Lines/Custom: lines using different point-marker geometries (circle, rounded square, SVG pin).
    internal static FrameworkElement LinesCustom() => new CartesianChart
    {
        Series =
        [
            new LineSeries<double>(2, 1, 4, 2, 2, -5, -2) { GeometrySize = 20 },
            new LineSeries<double, RoundedRectangleGeometry>(3, 3, -3, -2, -4, -3, -1) { GeometrySize = 20 },
            new LineSeries<double, SvgGeometry>(-2, 2, 1, 3, -1, 4, 3) { GeometrySize = 20, GeometrySvg = SVGPoints.Pin },
        ],
    };

    // Lines/CustomPoints: each point's marker is rotated by a per-point angle (set when measured).
    internal static FrameworkElement LinesCustomPoints()
    {
        LiveChartsMewUI.EnsureInitialized();
        LiveCharts.Configure(config => config.HasMap<RotatedPoint>((point, index) => new(index, point.Value)));
        var values = new RotatedPoint[]
        {
            new(4, 0), new(6, 20), new(8, 90), new(2, 176), new(7, 55), new(9, 226), new(3, 320),
        };
        return new CartesianChart { Series = [new RotatingLineSeries(values) { GeometrySize = 24 }] };
    }

    // Lines/AutoUpdate: a line whose backing collection is mutated on a timer.
    internal static FrameworkElement LinesAutoUpdate()
    {
        var values = new ObservableCollection<double> { 2, 5, 4, 2, 6, 5, 3 };
        var chart = new CartesianChart { Series = [new LineSeries<double>(values)] };
        var timer = new DispatcherTimer().IntervalMs(1500).OnTick(() =>
        {
            values.Add(Random.Next(0, 10));
            if (values.Count > 10) values.RemoveAt(0);
        });
        timer.Start();
        Timers.Add(timer);
        return chart;
    }
}
