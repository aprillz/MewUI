using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Views;

using LiveChartsCore.Kernel;

internal static partial class Samples
{
    // StackedBars/Basic
    internal static FrameworkElement StackedBarsBasic() => new CartesianChart
    {
        Series =
        [
            new StackedColumnSeries<int>(3, 5, -3, 2, 5, -4, -2) { ShowDataLabels = true, YToolTipLabelFormatter = StackedShareFormatter },
            new StackedColumnSeries<int>(4, 2, -3, 2, 3, 4, -2) { ShowDataLabels = true, YToolTipLabelFormatter = StackedShareFormatter },
            new StackedColumnSeries<int>(-2, 6, 6, 5, 4, 3, -2) { ShowDataLabels = true, YToolTipLabelFormatter = StackedShareFormatter },
        ],
    };

    // StackedBars/Groups
    internal static FrameworkElement StackedBarsGroups() => new CartesianChart
    {
        Series =
        [
            new StackedColumnSeries<int>(3, 5, 3) { StackGroup = 0 },
            new StackedColumnSeries<int>(4, 2, 3) { StackGroup = 0 },
            new StackedColumnSeries<int>(4, 6, 6) { StackGroup = 1 },
            new StackedColumnSeries<int>(2, 5, 4) { StackGroup = 1 },
        ],
        XAxes =
        [
            new Axis
            {
                LabelsRotation = -15,
                Labels = ["Category 1", "Category 2", "Category 3"],
                MinStep = 1,
                ForceStepToMin = true,
            },
        ],
    };

    // StackedArea/Basic
    internal static FrameworkElement StackedAreaBasic() => new CartesianChart
    {
        Series =
        [
            new StackedAreaSeries<double>(3, 2, 3, 5, 3, 4, 6),
            new StackedAreaSeries<double>(6, 5, 6, 3, 8, 5, 2),
            new StackedAreaSeries<double>(4, 8, 2, 8, 9, 5, 3),
        ],
    };

    // StackedArea/StepArea
    internal static FrameworkElement StackedStepArea() => new CartesianChart
    {
        Series =
        [
            new StackedStepAreaSeries<double>(3, 2, 3, 5, 3, 4, 6),
            new StackedStepAreaSeries<double>(6, 5, 6, 3, 8, 5, 2),
            new StackedStepAreaSeries<double>(4, 8, 2, 8, 9, 5, 3),
        ],
    };

    // Upstream StackedBars/Basic Formatter: the value and its share of the stack.
    private static string StackedShareFormatter(ChartPoint point) =>
        $"{point.Coordinate.PrimaryValue:N} ({point.StackedValue!.Share:P})";
}
