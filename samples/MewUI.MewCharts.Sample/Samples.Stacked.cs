using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Views;

internal static partial class Samples
{
    // StackedBars/Basic
    internal static FrameworkElement StackedBarsBasic() => new CartesianChart
    {
        Series =
        [
            new StackedColumnSeries<double>(3, 5, 3, 2, 5) { Name = "A" },
            new StackedColumnSeries<double>(4, 2, 3, 2, 3) { Name = "B" },
            new StackedColumnSeries<double>(4, 6, 6, 5, 4) { Name = "C" },
        ],
    };

    // StackedBars/Groups: four stacked series split into two stack groups.
    internal static FrameworkElement StackedBarsGroups() => new CartesianChart
    {
        Series =
        [
            new StackedColumnSeries<int>(3, 5, 3) { StackGroup = 0 },
            new StackedColumnSeries<int>(4, 2, 3) { StackGroup = 0 },
            new StackedColumnSeries<int>(4, 6, 6) { StackGroup = 1 },
            new StackedColumnSeries<int>(2, 5, 4) { StackGroup = 1 },
        ],
        XAxes = [new Axis { Labels = ["Category 1", "Category 2", "Category 3"], LabelsRotation = -15 }],
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

    // StackedArea/StepArea: stacked step-area series.
    internal static FrameworkElement StackedStepArea() => new CartesianChart
    {
        Series =
        [
            new StackedStepAreaSeries<int>(3, 2, 3, 5, 3, 4, 6),
            new StackedStepAreaSeries<int>(6, 5, 6, 3, 8, 5, 2),
            new StackedStepAreaSeries<int>(4, 8, 2, 8, 9, 5, 3),
        ],
    };
}
