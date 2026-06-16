using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Views;

using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;

internal static partial class Samples
{
    // Financial/BasicCandlesticks
    internal static FrameworkElement FinancialCandlesticks() => new CartesianChart
    {
        Series =
        [
            new CandlesticksSeries<FinancialPoint>(new FinancialPoint[]
            {
                new(new DateTime(2021, 1, 1), 523, 500, 480, 440),
                new(new DateTime(2021, 1, 2), 500, 480, 460, 430),
                new(new DateTime(2021, 1, 3), 490, 460, 470, 440),
                new(new DateTime(2021, 1, 4), 520, 470, 500, 460),
                new(new DateTime(2021, 1, 5), 540, 500, 520, 490),
            }),
        ],
        XAxes = [new DateTimeAxis(TimeSpan.FromDays(1), d => d.ToString("MM-dd"))],
    };

    // Box/Basic
    internal static FrameworkElement BoxBasic() => new CartesianChart
    {
        Series =
        [
            new BoxSeries<BoxValue>(new BoxValue[]
            {
                new(80, 60, 40, 20, 50), new(70, 55, 35, 25, 45), new(90, 70, 50, 30, 60),
            }),
        ],
    };

    // Heat/Basic
    internal static FrameworkElement HeatBasic() => new CartesianChart
    {
        Series =
        [
            new HeatSeries<WeightedPoint>(new WeightedPoint[]
            {
                new(0, 0, 8), new(0, 1, 4), new(0, 2, 6),
                new(1, 0, 3), new(1, 1, 9), new(1, 2, 2),
                new(2, 0, 7), new(2, 1, 1), new(2, 2, 5),
            })
            {
                HeatMap = [new LvcColor(33, 150, 243), new LvcColor(255, 235, 59), new LvcColor(244, 67, 54)],
            },
        ],
    };

    // Error/Basic: a column series and a line series carrying error bars.
    internal static FrameworkElement ErrorBasic() => new CartesianChart
    {
        Series =
        [
            new ColumnSeries<ErrorValue>(new ErrorValue[]
            {
                new(65, 6), new(70, 15, 4), new(35, 4), new(70, 6), new(30, 5), new(60, 4, 16), new(65, 6),
            }),
            new LineSeries<ErrorPoint>(new ErrorPoint[]
            {
                new(0, 50, 0.2, 8), new(1, 45, 0.1, 0.3, 15, 4), new(2, 25, 0.3, 4), new(3, 30, 0.2, 6),
                new(4, 70, 0.2, 8), new(5, 30, 0.4, 4), new(6, 50, 0.3, 6),
            }),
        ],
    };
}
