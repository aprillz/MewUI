using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;

internal static partial class Samples
{
    // A paint from a #RRGGBB or #AARRGGBB string, like the upstream XAML color strings.
    private static SolidColorPaint AxesPaint(string hex, float strokeThickness = 1f) =>
        new(Color.FromHex(hex), strokeThickness);

    // The bounce-in-out curve shared by Axes/MatchScale and Axes/Style (1000 points, step 0.001).
    private static ObservablePoint[] AxesBounceCurve()
    {
        var list = new List<ObservablePoint>();
        var easing = EasingFunctions.BounceInOut;

        for (var position = 0f; position < 1f; position += 0.001f)
        {
            list.Add(new ObservablePoint(position - 0.5, easing(position) - 0.5));
        }

        return [.. list];
    }

    // Axes/NamedLabels
    internal static FrameworkElement AxesNamedLabels() => new CartesianChart
    {
        TooltipPosition = TooltipPosition.Left,
        TooltipBackgroundPaint = AxesPaint("#480032"),
        TooltipTextPaint = AxesPaint("#F2F4FF"),
        TooltipTextSize = 16,
        Series =
        [
            new ColumnSeries<int>(200, 558, 458, 249) { Name = "Sales" },
            new LineSeries<int>(300, 450, 400, 280) { Name = "Projected", Fill = null },
        ],
        XAxes = [new Axis { Labels = ["Anne", "Johnny", "Zac", "Rosa"] }],
        YAxes = [new Axis { Labeler = value => value.ToString("C") }],
    };

    // Axes/LabelsRotation: the Y axis rotation follows a slider.
    internal static FrameworkElement AxesLabelsRotation()
    {
        var yAxis = new Axis { LabelsRotation = 15, SeparatorsPaint = AxesPaint("#eeeeee") };

        var chart = new CartesianChart
        {
            Series =
            [
                new LineSeries<double>(200, 558, 458, 249, 457, 339, 587)
                {
                    XToolTipLabelFormatter = point =>
                        $"This is {Environment.NewLine}" +
                        $"A multi-line label {Environment.NewLine}" +
                        $"With a value of {Environment.NewLine}" + point.Coordinate.PrimaryValue,
                },
            ],
            XAxes =
            [
                new Axis
                {
                    Labeler = value =>
                        $"This is {Environment.NewLine}" +
                        $"A multi-line label {Environment.NewLine}" +
                        $"With a value of {Environment.NewLine}" + value * 100,
                    MinStep = 1,
                    LabelsRotation = 45,
                    SeparatorsPaint = AxesPaint("#eeeeee"),
                },
            ],
            YAxes = [yAxis],
        };

        var rotationText = new TextBlock().Text("15").Width(32);
        var slider = new Slider().Minimum(-360).Maximum(720).Value(15).Width(150);
        slider.ValueChanged += value =>
        {
            yAxis.LabelsRotation = value;
            rotationText.Text = value.ToString("0.##");
        };

        return WithControls(chart, new TextBlock().Text("Rotation"), rotationText, slider);
    }

    // Axes/LabelsFormat
    internal static FrameworkElement AxesLabelsFormat() => new CartesianChart
    {
        Series =
        [
            new ColumnSeries<double>(426, 583, 104),
            new ColumnSeries<double>(200, 558, 458) { Fill = null },
        ],
        XAxes = [new Axis { Name = "Salesman/woman", Labels = ["Sergio", "Lando", "Lewis"] }],
        YAxes =
        [
            new Axis
            {
                Name = "Sales",
                NamePadding = new Padding(0, 15),
                Labeler = value => value.ToString("C2"),
                LabelsPaint = new SolidColorPaint(new Color(255, 0, 0, 255)) { FontFamily = "Times New Roman", FontWeight = FontWeight.ExtraBold, Italic = true },
            },
        ],
    };

    // Axes/LabelsFormat2
    internal static FrameworkElement AxesLabelsFormat2() => new CartesianChart
    {
        Series =
        [
            new ColumnSeries<double>(426, 583, 104),
            new ColumnSeries<double>(200, 558, 458) { Fill = null },
        ],
        XAxes = [new Axis { Name = "Salesman/woman", Labels = ["王", "赵", "张"] }],
        YAxes =
        [
            new Axis
            {
                Name = "Sales",
                NamePadding = new Padding(0, 15),
                Labeler = value => value.ToString("C2"),
                LabelsPaint = new SolidColorPaint(new Color(255, 0, 0, 255)) { FontFamily = "Times New Roman", FontWeight = FontWeight.ExtraBold, Italic = true },
            },
        ],
    };

    // Axes/Multiple: four series over three Y axes, each axis colored like its series.
    internal static FrameworkElement AxesMultiple()
    {
        const string BLUE = "#1976D2";
        const string RED = "#E53935";
        const string YELLOW = "#C6A700";

        LineSeries<double> MultipleSeries(string name, double[] values, string hex, int yAxisIndex) => new(values)
        {
            Name = name,
            Stroke = AxesPaint(hex, 2),
            GeometryStroke = AxesPaint(hex, 2),
            ScalesYAt = yAxisIndex,
            GeometrySize = 10,
            Fill = null,
            LineSmoothness = 1,
        };

        Axis MultipleAxis(string name, string hex, Padding padding, AxisPosition position, bool showSeparatorLines) => new()
        {
            Name = name,
            Padding = padding,
            Position = position,
            ShowSeparatorLines = showSeparatorLines,
            NamePaint = AxesPaint(hex),
            LabelsPaint = AxesPaint(hex),
            TicksPaint = AxesPaint(hex),
            SubticksPaint = AxesPaint(hex),
            NameTextSize = 14,
            NamePadding = new Padding(0, 20),
            TextSize = 12,
            DrawTicksPath = true,
        };

        return new CartesianChart
        {
            LegendBackgroundPaint = AxesPaint("#eeeeee"),
            LegendTextPaint = AxesPaint("#323232"),
            LegendTextSize = 16,
            LegendPosition = LegendPosition.Left,
            Series =
            [
                MultipleSeries("Tens", [14, 13, 14, 15, 17], BLUE, 0),
                MultipleSeries("Tens 2", [11, 12, 13, 10, 13], BLUE, 0),
                MultipleSeries("Hundreds", [533, 586, 425, 579, 518], RED, 1),
                MultipleSeries("Thousands", [5493, 7843, 4368, 9018, 3902], YELLOW, 2),
            ],
            YAxes =
            [
                MultipleAxis("Tens", BLUE, new Padding(0, 0, 20, 0), AxisPosition.Start, true),
                MultipleAxis("Hundreds", RED, new Padding(20, 0, 0, 0), AxisPosition.End, false),
                MultipleAxis("Thousands", YELLOW, new Padding(20, 0, 0, 0), AxisPosition.End, false),
            ],
        };
    }

    // Axes/DateTimeScaled: columns over a date-time X axis with a one-day interval.
    internal static FrameworkElement AxesDateTimeScaled() => new CartesianChart
    {
        Series = [new ColumnSeries<DateTimePoint>(new DateTimePoint[]
        {
            new() { DateTime = new(2021, 1, 1), Value = 3 },
            new() { DateTime = new(2021, 1, 2), Value = 6 },
            new() { DateTime = new(2021, 1, 3), Value = 5 },
            new() { DateTime = new(2021, 1, 4), Value = 3 },
            new() { DateTime = new(2021, 1, 5), Value = 5 },
            new() { DateTime = new(2021, 1, 6), Value = 8 },
            new() { DateTime = new(2021, 1, 7), Value = 6 },
        })],
        XAxes = [new DateTimeAxis(TimeSpan.FromDays(1), date => date.ToString("MMMM dd"))],
    };

    // Axes/TimeSpanScaled: columns over a time-span X axis with a one-millisecond interval.
    internal static FrameworkElement AxesTimeSpanScaled() => new CartesianChart
    {
        Series = [new ColumnSeries<TimeSpanPoint>(new TimeSpanPoint[]
        {
            new() { TimeSpan = TimeSpan.FromMilliseconds(1), Value = 10 },
            new() { TimeSpan = TimeSpan.FromMilliseconds(2), Value = 6 },
            new() { TimeSpan = TimeSpan.FromMilliseconds(3), Value = 3 },
            new() { TimeSpan = TimeSpan.FromMilliseconds(4), Value = 12 },
            new() { TimeSpan = TimeSpan.FromMilliseconds(5), Value = 8 },
        })],
        XAxes = [new TimeSpanAxis(TimeSpan.FromMilliseconds(1), value => $"{value:fff}ms")],
    };

    // Axes/ColorsAndPosition: both axes share a position and a color that the buttons change.
    internal static FrameworkElement AxesColorsAndPosition()
    {
        var xAxis = new Axis { Name = "X axis", TextSize = 20, Position = AxisPosition.End };
        var yAxis = new Axis { Name = "Y axis", TextSize = 20, Position = AxisPosition.End };
        var axes = new[] { xAxis, yAxis };

        void SetColor(Color color)
        {
            foreach (var axis in axes)
            {
                axis.LabelsPaint = new SolidColorPaint(color);
                axis.SeparatorsPaint = new SolidColorPaint(color);
            }
        }

        SetColor(Color.FromHex("#ff0000"));

        var chart = new CartesianChart
        {
            Series = [new ColumnSeries<double>(2, 3, 8)],
            XAxes = [xAxis],
            YAxes = [yAxis],
        };

        return WithActions(chart,
            ("Toggle position", () =>
            {
                var position = xAxis.Position == AxisPosition.End ? AxisPosition.Start : AxisPosition.End;
                foreach (var axis in axes) axis.Position = position;
            }),
            ("Set new color", () => SetColor(new Color(
                (byte)Random.Next(0, 255),
                (byte)Random.Next(0, 255),
                (byte)Random.Next(0, 255)))));
    }

    // Axes/CustomSeparatorsInterval: the Y axis draws separators only at explicit values.
    internal static FrameworkElement AxesCustomSeparators() => new CartesianChart
    {
        Series = [new LineSeries<int>(10, 55, 45, 68, 60, 70, 75, 78)],
        YAxes =
        [
            new Axis
            {
                CustomSeparators = [0, 10, 25, 50, 100],
                MinLimit = 0,
                MaxLimit = 100,
                SeparatorsPaint = AxesPaint("#64b4b4b4"),
            },
        ],
    };

    // Axes/MatchScale: X and Y share the same screen-to-data ratio (a square-units plot).
    internal static FrameworkElement AxesMatchScale() => new CartesianChart
    {
        MatchAxesScreenDataRatio = true,
        DrawMarginFrame = new DrawMarginFrame { Stroke = AxesPaint("#64b4b4b4", 2) },
        ZoomMode = ZoomAndPanMode.Both,
        TooltipPosition = TooltipPosition.Hidden,
        Series =
        [
            new LineSeries<ObservablePoint>(AxesBounceCurve())
            {
                Stroke = AxesPaint("#2196f3", 4),
                Fill = null,
                GeometryStroke = null,
                GeometryFill = null,
            },
        ],
        XAxes = [new Axis { Name = "XAxis", SeparatorsPaint = AxesPaint("#64b4b4b4"), MinStep = 0.1, ForceStepToMin = true }],
        YAxes = [new Axis { Name = "YAxis", SeparatorsPaint = AxesPaint("#64b4b4b4"), MinStep = 0.1, ForceStepToMin = true }],
    };

    // Axes/Crosshairs: axes with crosshair paints (the crosshair follows the pointer on hover).
    internal static FrameworkElement AxesCrosshairs() => new CartesianChart
    {
        Series = [new LineSeries<double>(200, 558, 458, 249, 457, 339, 587)],
        XAxes =
        [
            new Axis
            {
                CrosshairLabelsBackground = new LvcColor(255, 140, 0),
                CrosshairLabelsPaint = AxesPaint("#8B0000"),
                CrosshairPaint = AxesPaint("#FF8C00"),
                Labeler = value => value.ToString("N2"),
            },
        ],
        YAxes =
        [
            new Axis
            {
                CrosshairLabelsBackground = new LvcColor(255, 140, 0),
                CrosshairLabelsPaint = AxesPaint("#8B0000"),
                CrosshairPaint = AxesPaint("#FF8C00"),
                CrosshairSnapEnabled = true,
                Labeler = value => value.ToString("N2"),
            },
        ],
    };

    // Axes/Style: a curve over fully styled axes on a dark background; the 20 margin is a border padding around the chart.
    internal static FrameworkElement AxesStyle()
    {
        const string GRAY = "#c3c3c3";
        const string GRAY1 = "#a0a0a0";
        const string GRAY2 = "#5a5a5a";

        Axis StyledAxis(string name) => new()
        {
            Name = name,
            NamePaint = AxesPaint(GRAY1),
            TextSize = 18,
            LabelsPaint = AxesPaint(GRAY),
            SeparatorsPaint = AxesPaint(GRAY, 1),
            SubseparatorsPaint = AxesPaint(GRAY2, 0.5f),
            SubseparatorsCount = 9,
            ZeroPaint = AxesPaint(GRAY1, 2),
            TicksPaint = AxesPaint(GRAY, 1.5f),
            SubticksPaint = AxesPaint(GRAY, 1),
        };

        var chart = new CartesianChart
        {
            Background = Color.FromHex("#303030"),
            DrawMarginFrame = new DrawMarginFrame { Stroke = AxesPaint(GRAY, 2) },
            ZoomMode = ZoomAndPanMode.Both,
            TooltipPosition = TooltipPosition.Hidden,
            Series =
            [
                new LineSeries<ObservablePoint>(AxesBounceCurve())
                {
                    Stroke = AxesPaint("#2196F3", 4),
                    Fill = null,
                    GeometryFill = null,
                    GeometryStroke = null,
                },
            ],
            XAxes = [StyledAxis("X Axis")],
            YAxes = [StyledAxis("Y Axis")],
        };

        return new Border { Padding = new Thickness(20), Child = chart };
    }

    // Axes/Logarithmic: values are mapped into log space (Y -> log_base(Y)) so the axis labeler
    // (base^value) recovers the real magnitude. EnsureInitialized must run before HasMap so the
    // built-in mappers (double, etc.) are still registered.
    internal static FrameworkElement AxesLogarithmic()
    {
        const double LOG_BASE = 10;
        LiveChartsMewUI.EnsureInitialized();
        LiveCharts.Configure(config => config.HasMap<LogarithmicPoint>(
            (point, index) => new(point.X, Math.Log(point.Y, LOG_BASE))));

        return new CartesianChart
        {
            Series = [new LineSeries<LogarithmicPoint>([
                new(1, 1), new(2, 10), new(3, 100), new(4, 1000),
                new(5, 10000), new(6, 100000), new(7, 1000000), new(8, 10000000),
            ])],
            YAxes =
            [
                new LogarithmicAxis(LOG_BASE)
                {
                    SeparatorsPaint = AxesPaint("#64b4b4b4"),
                    SubseparatorsPaint = AxesPaint("#64b4b4b4"),
                    SubseparatorsCount = 9,
                },
            ],
        };
    }

    // Axes/Paging: buttons page through the data by setting the X axis min/max limits.
    internal static FrameworkElement AxesPaging()
    {
        var random = new Random(11);
        var trend = 100;
        var values = new int[100];
        for (var index = 0; index < 100; index++) { trend += random.Next(-30, 50); values[index] = trend; }

        var xAxis = new Axis();
        var chart = new CartesianChart
        {
            Series = [new ColumnSeries<int>(values)],
            XAxes = [xAxis],
            ZoomMode = ZoomAndPanMode.X,
        };

        void Page(double? min, double? max) { xAxis.MinLimit = min; xAxis.MaxLimit = max; }

        return WithActions(chart,
            ("Page 1", () => Page(-0.5, 10.5)),
            ("Page 2", () => Page(9.5, 20.5)),
            ("Page 3", () => Page(19.5, 30.5)),
            ("Clear", () => Page(null, null)));
    }

    // Axes/Shared: two charts whose X axes are linked, so panning/zooming one moves the other.
    internal static FrameworkElement AxesShared()
    {
        static int[] Fetch(int length)
        {
            var random = new Random(length);
            var total = 0;
            var values = new int[length];
            for (var index = 0; index < length; index++) { total += random.Next(-90, 100); values[index] = total; }
            return values;
        }

        var values1 = Fetch(100);
        var values2 = Fetch(50);
        var max = Math.Max(values1.Length, values2.Length);

        var firstAxis = new Axis
        {
            CrosshairLabelsBackground = new LvcColor(255, 69, 0),
            CrosshairLabelsPaint = AxesPaint("#ffffff"),
            CrosshairPaint = AxesPaint("#40FF4500"),
            Labeler = value => value.ToString("N2"),
            MaxLimit = max,
        };
        var secondAxis = new Axis { CrosshairPaint = AxesPaint("#40FF4500"), MaxLimit = max };
        SharedAxes.Set(firstAxis, secondAxis);

        // Forces the left draw margin to 70 so the axes align whatever the label widths are.
        var top = new CartesianChart
        {
            Series = [new LineSeries<int>(values1)],
            XAxes = [firstAxis],
            DrawMargin = new Margin(70, Margin.Auto, Margin.Auto, Margin.Auto),
            ZoomMode = ZoomAndPanMode.X,
        };
        var bottom = new CartesianChart
        {
            Series = [new ColumnSeries<int>(values2)],
            XAxes = [secondAxis],
            DrawMargin = new Margin(70, Margin.Auto, Margin.Auto, Margin.Auto),
            ZoomMode = ZoomAndPanMode.X,
        };

        return Charts(top, bottom);
    }
}
