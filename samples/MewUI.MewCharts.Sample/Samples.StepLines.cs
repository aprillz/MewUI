using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Drawing.Geometries;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;

using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;

internal static partial class Samples
{
    // StepLines/Basic
    internal static FrameworkElement StepLinesBasic() => new CartesianChart
    {
        Title = MyChartTitle(),
        Series = [new StepLineSeries<double>(2, 1, 3, 4, 3, 4, 6) { Fill = null, GeometrySize = 20 }],
    };

    // StepLines/Area
    internal static FrameworkElement StepLinesArea() => new CartesianChart
    {
        DrawMarginFrame = SampleDrawMarginFrame(),
        Series =
        [
            new StepLineSeries<double>(-2, -1, 3, 5, 3, 4, 6)
            {
                Fill = new SolidColorPaint(Color.FromHex("#6495ED")),
                Stroke = null,
                GeometryFill = null,
                GeometryStroke = null,
            },
        ],
    };

    // StepLines/Properties
    internal static FrameworkElement StepLinesProperties()
    {
        var geometrySize = 20d;
        var series = new StepLineSeries<double>(-2, -1, 3, 5, 3, 4, 6)
        {
            Stroke = new SolidColorPaint(Color.FromHex("#000000")),
            Fill = new SolidColorPaint(Color.FromHex("#30000000")),
            GeometryStroke = new SolidColorPaint(Color.FromHex("#000000")),
            GeometryFill = new SolidColorPaint(Color.FromHex("#30000000")),
            GeometrySize = geometrySize,
        };
        var chart = new CartesianChart { Series = [series] };

        return WithActions(chart,
            ("new values", () => series.Values = RandomWalk(10, -5, 10)),
            ("fill", () => series.Fill = new SolidColorPaint(RandomOpaqueColor())),
            ("stroke", () => series.Stroke = new SolidColorPaint(RandomOpaqueColor())),
            ("geo fill", () => series.GeometryFill = new SolidColorPaint(RandomOpaqueColor())),
            ("geom stroke", () => series.GeometryStroke = new SolidColorPaint(RandomOpaqueColor())),
            ("+ size", () =>
            {
                if (geometrySize != 60) series.GeometrySize = geometrySize += 10;
            }),
            ("- size", () =>
            {
                if (geometrySize != 0) series.GeometrySize = geometrySize -= 10;
            }));
    }

    // StepLines/AutoUpdate
    internal static FrameworkElement StepLinesAutoUpdate() =>
        AutoUpdateChart("Ana", (name, values) => new StepLineSeries<ObservableValue>(values) { Name = name });

    // StepLines/Custom
    internal static FrameworkElement StepLinesCustom() => new CartesianChart
    {
        Series =
        [
            new StepLineSeries<double>(2, 1, 4, 2, 2, -5, -2) { Fill = null, GeometrySize = 20 },
            new StepLineSeries<double, StarGeometry>(3, 3, -3, -2, -4, -3, -1) { Fill = null, GeometrySize = 20 },
            new StepLineSeries<double, SvgGeometry>(-2, 2, 1, 3, -1, 4, 3) { GeometrySvg = SVGPoints.Pin, Fill = null, GeometrySize = 20 },
            new StepLineSeries<double, LinesMyGeometry>(4, 5, 2, 4, 3, 2, 1) { Fill = null, GeometrySize = 20 },
        ],
    };

    // StepLines/Zoom
    internal static FrameworkElement StepLinesZoom() => new CartesianChart
    {
        ZoomMode = ZoomAndPanMode.X,
        Series = [new StepLineSeries<int>(ZoomRandomWalk())],
    };
}
