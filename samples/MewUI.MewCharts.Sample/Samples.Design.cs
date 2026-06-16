using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;

internal static partial class Samples
{
    // Design/LinearGradients
    internal static FrameworkElement DesignLinearGradients() => new CartesianChart
    {
        Series =
        [
            new ColumnSeries<double>(3, 5, 4, 6, 2, 4, 6)
            {
                Fill = new LinearGradientPaint([new Color(255, 33, 150, 243), new Color(255, 244, 67, 54)]),
            },
        ],
    };

    // Design/RadialGradients
    internal static FrameworkElement DesignRadialGradients() => new PieChart
    {
        Series =
        [
            new PieSeries<double>(8) { Fill = new RadialGradientPaint(new Color(255, 130, 170, 255), new Color(255, 30, 60, 160)) },
            new PieSeries<double>(6) { Fill = new RadialGradientPaint(new Color(255, 170, 255, 170), new Color(255, 30, 140, 60)) },
            new PieSeries<double>(4) { Fill = new RadialGradientPaint(new Color(255, 255, 200, 140), new Color(255, 200, 90, 20)) },
        ],
    };

    // Design/StrokeDashArray
    internal static FrameworkElement DesignStrokeDashArray() => new CartesianChart
    {
        Series =
        [
            new LineSeries<double>(2, 1, 3, 5, 3, 4, 6)
            {
                GeometrySize = 0,
                Fill = null,
                Stroke = new SolidColorPaint(new Color(255, 33, 150, 243), 3) { DashArray = [6, 4] },
            },
        ],
    };
}
