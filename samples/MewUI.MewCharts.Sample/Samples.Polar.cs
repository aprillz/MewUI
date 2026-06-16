using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;

using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;

internal static partial class Samples
{
    // Polar/Basic
    internal static FrameworkElement PolarBasic() => new PolarChart
    {
        Series =
        [
            new PolarLineSeries<double>(15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1),
        ],
    };

    // Polar/RadialArea: two closed polar areas over named angle labels.
    internal static FrameworkElement PolarRadialArea() => new PolarChart
    {
        InitialRotation = -45,
        Series =
        [
            new PolarLineSeries<int>(7, 5, 7, 5, 6) { Fill = new SolidColorPaint(new Color(144, 0, 0, 255)), IsClosed = true },
            new PolarLineSeries<int>(2, 7, 5, 9, 7) { Fill = new SolidColorPaint(new Color(144, 255, 0, 0)), IsClosed = true },
        ],
        AngleAxes = [new PolarAxis { Labels = ["first", "second", "third", "forth", "fifth"] }],
    };

    // Polar/Coordinates: a polar line plotted from explicit (angle, radius) points.
    internal static FrameworkElement PolarCoordinates() => new PolarChart
    {
        Series = [new PolarLineSeries<ObservablePolarPoint>(new ObservablePolarPoint[]
        {
            new(0, 10), new(45, 15), new(90, 20), new(135, 25), new(180, 30),
            new(225, 35), new(270, 40), new(315, 45), new(360, 50),
        })],
    };

    // Polar/Test: a closed polar line with data labels and an inner radius.
    internal static FrameworkElement PolarTest() => new PolarChart
    {
        InitialRotation = 15,
        InnerRadius = 50,
        TotalAngle = 360,
        Series =
        [
            new PolarLineSeries<double>(15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1)
            {
                IsClosed = true,
                GeometrySize = 30,
                ShowDataLabels = true,
                DataLabelsSize = 15,
                DataLabelsPaint = new SolidColorPaint(new Color(255, 255, 255, 255)),
                DataLabelsPosition = PolarLabelsPosition.Middle,
            },
        ],
    };
}
