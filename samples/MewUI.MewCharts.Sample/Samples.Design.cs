using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;
using Aprillz.MewUI.Rendering;

using LiveChartsCore.Measure;

internal static partial class Samples
{
    // Design/LinearGradients
    internal static FrameworkElement DesignLinearGradients() => new CartesianChart
    {
        LegendPosition = LegendPosition.Right,
        Series =
        [
            new ColumnSeries<int>(3, 7, 2, 9, 4)
            {
                Name = "John",
                Fill = new LinearGradientPaint(Color.FromHex("#FF8C94"), Color.FromHex("#DCEDC2"), new Point(0.5, 0), new Point(0.5, 1)),
            },
            new LineSeries<int>(4, 2, 8, 5, 3)
            {
                Name = "Charles",
                GeometrySize = 22,
                Fill = null,
                Stroke = new LinearGradientPaint(Color.FromHex("#2D4059"), Color.FromHex("#FFD360")) { StrokeThickness = 10 },
                GeometryStroke = new LinearGradientPaint(Color.FromHex("#2D4059"), Color.FromHex("#FFD360")) { StrokeThickness = 10 },
            },
        ],
    };

    // Design/RadialGradients
    internal static FrameworkElement DesignRadialGradients() => new PieChart
    {
        LegendPosition = LegendPosition.Right,
        Series =
        [
            new PieSeries<double>(7)
            {
                Name = "Maria",
                Pushout = 10,
                OuterRadiusOffset = 20,
                Fill = new RadialGradientPaint(Color.FromHex("#B3E5FC"), Color.FromHex("#01579B")),
            },
            new PieSeries<double>(3)
            {
                Name = "Charles",
                Fill = new RadialGradientPaint(Color.FromHex("#FFCDD2"), Color.FromHex("#B71C1C")),
            },
        ],
    };

    // Design/StrokeDashArray: the upstream dash of 30, 20 pixels at width 10 is 3, 2 in stroke-width units.
    internal static FrameworkElement DesignStrokeDashArray() => new CartesianChart
    {
        Series =
        [
            new LineSeries<int>(4, 2, 8, 5, 3)
            {
                LineSmoothness = 1,
                GeometrySize = 22,
                Stroke = new SolidColorPaint(Color.FromHex("#6495ED"), 10) { StrokeCap = StrokeLineCap.Round, DashArray = [3, 2] },
                Fill = null,
            },
        ],
    };
}
