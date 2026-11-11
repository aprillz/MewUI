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

    // Scatter/Bubbles: StackGroup 1 makes the second and third series share one weight scale.
    internal static FrameworkElement ScatterBubbles() => new CartesianChart
    {
        Series =
        [
            new ScatterSeries<WeightedPoint>(FetchWeightedPoints(1)) { GeometrySize = 100, MinGeometrySize = 5 },
            new ScatterSeries<WeightedPoint>(FetchWeightedPoints(10)) { GeometrySize = 100, MinGeometrySize = 5, StackGroup = 1 },
            new ScatterSeries<WeightedPoint>(FetchWeightedPoints(10)) { GeometrySize = 100, MinGeometrySize = 5, StackGroup = 1 },
        ],
    };

    // Scatter/AutoUpdate
    internal static FrameworkElement ScatterAutoUpdate() =>
        AutoUpdateChart("Juana", (name, values) => new ScatterSeries<ObservableValue>(values) { Name = name });

    // Scatter/Custom
    internal static FrameworkElement ScatterCustom() => new CartesianChart
    {
        Series =
        [
            new ScatterSeries<ObservablePoint, HeartGeometry>(FetchScatterPoints()) { Stroke = null, GeometrySize = 40 },
            new ScatterSeries<ObservablePoint, SvgGeometry>(FetchScatterPoints()) { GeometrySvg = SVGPoints.Pin, GeometrySize = 40 },
            new ScatterSeries<ObservablePoint, ScatterMyGeometry>(FetchScatterPoints()) { GeometrySize = 40 },
        ],
    };

    // Upstream Bubbles Fetch: 10 points at random integer coordinates with weight Next(0, 100) * scale.
    private static WeightedPoint[] FetchWeightedPoints(int scale)
    {
        var values = new WeightedPoint[10];
        for (var index = 0; index < values.Length; index++)
        {
            var pointX = Random.Next(0, 20);
            var pointY = Random.Next(0, 20);
            var weight = Random.Next(0, 100) * scale;
            values[index] = new WeightedPoint(pointX, pointY, weight);
        }
        return values;
    }

    // Upstream Scatter/Custom Fetch: 10 points at random integer coordinates.
    private static ObservablePoint[] FetchScatterPoints()
    {
        var values = new ObservablePoint[10];
        for (var index = 0; index < values.Length; index++)
        {
            var pointX = Random.Next(0, 20);
            var pointY = Random.Next(0, 20);
            values[index] = new ObservablePoint(pointX, pointY);
        }
        return values;
    }
}
