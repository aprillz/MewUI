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
        Title = MyChartTitle(),
        Series =
        [
            new LineSeries<double>(2, 1, 3, 5, 3, 4, 6) { Fill = null, GeometrySize = 20 },
            new LineSeries<int, StarGeometry>(4, 2, 5, 2, 4, 5, 3) { Fill = null, GeometrySize = 20 },
        ],
    };

    // Lines/Straight
    internal static FrameworkElement LinesStraight() => new CartesianChart
    {
        Series =
        [
            new LineSeries<double>(5, 0, 5, 0, 5, 0) { Fill = null, GeometrySize = 0, LineSmoothness = 0 },
            new LineSeries<double>(7, 2, 7, 2, 7, 2) { Fill = null, GeometrySize = 0, LineSmoothness = 1 },
        ],
    };

    // Lines/Area
    internal static FrameworkElement LinesArea() => new CartesianChart
    {
        DrawMarginFrame = SampleDrawMarginFrame(),
        Series =
        [
            new LineSeries<double>(-2, -1, 3, 5, 3, 4, 6)
            {
                Fill = new SolidColorPaint(Color.FromHex("#6495ED")),
                Stroke = null,
                GeometryFill = null,
                GeometryStroke = null,
            },
        ],
    };

    // Lines/XY
    internal static FrameworkElement LinesXY() => new CartesianChart
    {
        Series = [new LineSeries<ObservablePoint>(new ObservablePoint[]
        {
            new(0, 4), new(1, 3), new(3, 8), new(18, 6), new(20, 12),
        })],
    };

    // Lines/Properties
    internal static FrameworkElement LinesProperties()
    {
        var smoothness = 0.5;
        var geometrySize = 20d;
        var series = new LineSeries<double>(-2, -1, 3, 5, 3, 4, 6)
        {
            Stroke = PropertiesPaint(Color.FromHex("#000000")),
            Fill = PropertiesPaint(Color.FromHex("#30000000")),
            GeometryStroke = PropertiesPaint(Color.FromHex("#000000")),
            GeometryFill = PropertiesPaint(Color.FromHex("#30000000")),
            LineSmoothness = smoothness,
            GeometrySize = geometrySize,
        };
        var chart = new CartesianChart { Series = [series] };

        return WithActions(chart,
            ("new values", () => series.Values = RandomWalk(10, -5, 10)),
            ("fill", () => series.Fill = PropertiesPaint(RandomOpaqueColor())),
            ("stroke", () => series.Stroke = PropertiesPaint(RandomOpaqueColor())),
            ("geo fill", () => series.GeometryFill = PropertiesPaint(RandomOpaqueColor())),
            ("geom stroke", () => series.GeometryStroke = PropertiesPaint(RandomOpaqueColor())),
            ("+ smooth", () =>
            {
                if (smoothness != 1) series.LineSmoothness = smoothness += 0.1;
            }),
            ("- smooth", () =>
            {
                if (smoothness != 0) series.LineSmoothness = smoothness -= 0.1;
            }),
            ("+ size", () =>
            {
                if (geometrySize != 60) series.GeometrySize = geometrySize += 10;
            }),
            ("- size", () =>
            {
                if (geometrySize != 0) series.GeometrySize = geometrySize -= 10;
            }));
    }

    // Lines/Padding
    internal static FrameworkElement LinesPadding() => new CartesianChart
    {
        DrawMarginFrame = SampleDrawMarginFrame(),
        Series =
        [
            new LineSeries<double>(2, 1, 3, 5, 3, 4, 6)
            {
                GeometryStroke = null,
                GeometryFill = null,
                DataPadding = new LvcPoint(0, 0),
            },
        ],
    };

    // Lines/Zoom
    internal static FrameworkElement LinesZoom() => new CartesianChart
    {
        ZoomMode = ZoomAndPanMode.X,
        Series = [new LineSeries<int>(ZoomRandomWalk())],
    };

    // Lines/Custom
    internal static FrameworkElement LinesCustom() => new CartesianChart
    {
        Series =
        [
            new LineSeries<double>(2, 1, 4, 2, 2, -5, -2) { Fill = null, GeometrySize = 20 },
            new LineSeries<double, StarGeometry>(3, 3, -3, -2, -4, -3, -1) { Fill = null, GeometrySize = 20 },
            new LineSeries<double, SvgGeometry>(-2, 2, 1, 3, -1, 4, 3) { GeometrySvg = SVGPoints.Pin, Fill = null, GeometrySize = 20 },
            new LineSeries<double, LinesMyGeometry>(4, 5, 2, 4, 3, 2, 1) { Fill = null, GeometrySize = 20 },
        ],
    };

    // Lines/CustomPoints
    internal static FrameworkElement LinesCustomPoints()
    {
        LiveChartsMewUI.EnsureInitialized();
        LiveCharts.Configure(config => config.HasMap<CustomPointsDataPoint>((dataPoint, index) => new(index, dataPoint.Value)));

        var series = new LineSeries<CustomPointsDataPoint, ArrowGeometry>(new CustomPointsDataPoint[]
        {
            new(4, 0), new(6, 20), new(8, 90), new(2, 176), new(7, 55), new(9, 226), new(3, 320),
        })
        {
            GeometrySize = 50,
            GeometryStroke = null,
            GeometryFill = new SolidColorPaint(Color.FromHex("#C71585")),
            Fill = null,
        };
        series.PointMeasured += point =>
            point.Context.Visual!.RotateTransform = ((CustomPointsDataPoint)point.Context.DataSource!).Rotation;

        return new CartesianChart { Series = [series] };
    }

    // Lines/AutoUpdate
    internal static FrameworkElement LinesAutoUpdate() =>
        AutoUpdateChart("Ana", (name, values) => new LineSeries<ObservableValue>(values) { Name = name });

    // Shared by Lines/Basic and StepLines/Basic.
    private static LabelVisual MyChartTitle() => new()
    {
        Text = "My chart title",
        Paint = new SolidColorPaint(Color.FromHex("#303030")),
        TextSize = 25,
        Padding = new Padding(15),
    };

    // Shared by the Area and Padding samples.
    private static DrawMarginFrame SampleDrawMarginFrame() => new()
    {
        Stroke = new SolidColorPaint(Color.FromHex("#64b4b4b4")),
        Fill = new SolidColorPaint(Color.FromHex("#32dcdcdc")),
    };

    // Upstream Lines/Properties StringToPaintConverter creates every paint with a stroke width of 3.
    private static SolidColorPaint PropertiesPaint(Color color) => new(color, 3);

    // Upstream GetRandomColor: Next(0, 255) per channel, fully opaque.
    private static Color RandomOpaqueColor() =>
        new((byte)Random.Next(0, 255), (byte)Random.Next(0, 255), (byte)Random.Next(0, 255));

    // Upstream ChangeValuesInstance: a random walk of Next(minStep, maxStep) increments.
    private static double[] RandomWalk(int count, int minStep, int maxStep)
    {
        var total = 0;
        var values = new double[count];
        for (var index = 0; index < count; index++)
        {
            total += Random.Next(minStep, maxStep);
            values[index] = total;
        }
        return values;
    }

    // Upstream Zoom Fetch: 100 values, each Next(-90, 100) away from the previous one.
    private static int[] ZoomRandomWalk()
    {
        var total = 0;
        var values = new int[100];
        for (var index = 0; index < values.Length; index++)
        {
            total += Random.Next(-90, 100);
            values[index] = total;
        }
        return values;
    }

    // Upstream AutoUpdate view model: item commands act on the first series, series are added and removed at the end.
    private static FrameworkElement AutoUpdateChart(string firstName, Func<string, ObservableCollection<ObservableValue>, ISeries> createSeries)
    {
        ObservableCollection<ObservableValue> firstValues = [new(2), new(5), new(4)];
        var series = new ObservableCollection<ISeries>
        {
            createSeries(firstName, firstValues),
            createSeries("Mary", [new(5), new(4), new(1)]),
        };
        var chart = new CartesianChart { Series = series };

        return WithActions(chart,
            ("Add item", () => firstValues.Add(new ObservableValue { Value = Random.Next(0, 10) })),
            ("Replace item", () => firstValues[Random.Next(0, firstValues.Count - 1)] = new(Random.Next(1, 10))),
            ("Remove item", () =>
            {
                if (firstValues.Count >= 2) firstValues.RemoveAt(0);
            }),
            ("Add Series", () => series.Add(createSeries(
                $"User #{series.Count}",
                [new(Random.Next(0, 10)), new(Random.Next(0, 10)), new(Random.Next(0, 10))]))),
            ("Remove Series", () =>
            {
                if (series.Count != 1) series.RemoveAt(series.Count - 1);
            }));
    }
}
