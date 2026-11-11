using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Drawing;
using Aprillz.MewUI.MewCharts.Drawing.Geometries;
using Aprillz.MewUI.MewCharts.Drawing.Layouts;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;
using Aprillz.MewUI.Rendering;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Drawing.Layouts;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;

internal static partial class Samples
{
    // Financial/BasicCandlesticks
    internal static FrameworkElement FinancialCandlesticks() => new CartesianChart
    {
        Series =
        [
            new CandlesticksSeries<FinancialPoint>(new ObservableCollection<FinancialPoint>
            {
                new() { Date = new DateTime(2021, 1, 1), High = 523, Open = 500, Close = 450, Low = 400 },
                new() { Date = new DateTime(2021, 1, 2), High = 500, Open = 450, Close = 425, Low = 400 },
                new() { Date = new DateTime(2021, 1, 3), High = 490, Open = 425, Close = 400, Low = 380 },
                new() { Date = new DateTime(2021, 1, 4), High = 420, Open = 400, Close = 420, Low = 380 },
                new() { Date = new DateTime(2021, 1, 5), High = 520, Open = 420, Close = 490, Low = 400 },
                new() { Date = new DateTime(2021, 1, 6), High = 580, Open = 490, Close = 560, Low = 440 },
            }),
        ],
        XAxes = [new DateTimeAxis(TimeSpan.FromDays(1), value => value.ToString("yyyy MMM dd")) { LabelsRotation = 0 }],
    };

    // Box/Basic
    internal static FrameworkElement BoxBasic() => new CartesianChart
    {
        Series =
        [
            new BoxSeries<BoxValue>(new BoxValue[]
            {
                new(100, 80, 60, 20, 70),
                new(90, 70, 50, 30, 60),
                new(80, 60, 40, 10, 50),
            }) { Name = "Year 2023" },
            new BoxSeries<BoxValue>(new BoxValue[]
            {
                new(90, 70, 50, 30, 60),
                new(80, 60, 40, 10, 50),
                new(70, 50, 30, 20, 40),
            }) { Name = "Year 2024" },
            new BoxSeries<BoxValue>(new BoxValue[]
            {
                new(80, 60, 40, 10, 50),
                new(70, 50, 30, 20, 40),
                new(60, 40, 20, 10, 30),
            }) { Name = "Year 2025" },
        ],
        XAxes =
        [
            new Axis
            {
                Labels = ["Apperitizers", "Mains", "Desserts"],
                LabelsRotation = 0,
                SeparatorsPaint = new SolidColorPaint(new Color(200, 200, 200)),
                SeparatorsAtCenter = false,
                TicksPaint = new SolidColorPaint(new Color(35, 35, 35)),
                TicksAtCenter = true,
            },
        ],
        LegendPosition = LegendPosition.Right,
    };

    // Heat/Basic
    internal static FrameworkElement HeatBasic() => new CartesianChart
    {
        Series =
        [
            new HeatSeries<WeightedPoint>(new WeightedPoint[]
            {
                // Charles
                new(0, 0, 150), new(0, 1, 123), new(0, 2, 310), new(0, 3, 225), new(0, 4, 473), new(0, 5, 373),
                // Richard
                new(1, 0, 432), new(1, 1, 312), new(1, 2, 135), new(1, 3, 78), new(1, 4, 124), new(1, 5, 423),
                // Ana
                new(2, 0, 543), new(2, 1, 134), new(2, 2, 524), new(2, 3, 315), new(2, 4, 145), new(2, 5, 80),
                // Mari
                new(3, 0, 90), new(3, 1, 123), new(3, 2, 70), new(3, 3, 123), new(3, 4, 432), new(3, 5, 142),
            })
            {
                HeatMap = [new LvcColor(255, 241, 118), new LvcColor(47, 79, 79), new LvcColor(0, 0, 255)],
            },
        ],
        XAxes = [new Axis { Labels = ["Charles", "Richard", "Ana", "Mari"] }],
        YAxes = [new Axis { Labels = ["Jan", "Feb", "Mar", "Apr", "May", "Jun"] }],
        LegendPosition = LegendPosition.Right,
        Legend = new MewHeatLegend { BadgePadding = new Padding(30, 20, 8, 20) },
    };

    // Error/Basic: columns, a line and a date scatter, each showing error bars.
    internal static FrameworkElement ErrorBasic()
    {
        ErrorValue[] values1 =
        [
            // (Y, Y+- error)
            new(65, 6),
            // (Y, Y+ error, Y- error)
            new(70, 15, 4),
            new(35, 4),
            new(70, 6),
            new(30, 5),
            new(60, 4, 16),
            new(65, 6),
        ];

        ErrorPoint[] values2 =
        [
            // (X, Y, X+- error, Y+- error)
            new(0, 50, 0.2, 8),
            // (X, Y, X- error, X+ error, Y+ error, Y- error)
            new(1, 45, 0.1, 0.3, 15, 4),
            new(2, 25, 0.3, 4),
            new(3, 30, 0.2, 6),
            new(4, 70, 0.2, 8),
            new(5, 30, 0.4, 4),
            new(6, 50, 0.3, 6),
        ];

        ErrorDateTimePoint[] values3 =
        [
            new(DateTime.Today.AddDays(0), 50, 0.2, 8),
            new(DateTime.Today.AddDays(1), 45, 0.1, 0.3, 15, 4),
            new(DateTime.Today.AddDays(2), 25, 0.3, 4),
            new(DateTime.Today.AddDays(3), 30, 0.2, 6),
            new(DateTime.Today.AddDays(4), 70, 0.2, 8),
            new(DateTime.Today.AddDays(5), 30, 0.4, 4),
            new(DateTime.Today.AddDays(6), 50, 0.3, 6),
        ];

        // The MewCharts theme has no error bar rule, so apply the upstream theme paint explicitly.

        var columns = new CartesianChart
        {
            Series =
            [
                new ColumnSeries<ErrorValue>(values1) { ShowError = true, Padding = 0 },
                new ColumnSeries<ErrorPoint>(values2) { ShowError = true, Padding = 0 },
            ],
        };

        var line = new CartesianChart
        {
            Series = [new LineSeries<ErrorValue>(values1) { ShowError = true, GeometrySize = 4, Fill = null }],
        };

        var scatter = new CartesianChart
        {
            Series = [new ScatterSeries<ErrorDateTimePoint>(values3) { ShowError = true, GeometrySize = 10 }],
            XAxes = [new DateTimeAxis(TimeSpan.FromDays(1), date => date.ToString("MMMM dd"))],
        };

        return Charts(columns, line, scatter);
    }
}
