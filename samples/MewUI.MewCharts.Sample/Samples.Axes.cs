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
    // Axes/NamedLabels
    internal static FrameworkElement AxesNamedLabels() => new CartesianChart
    {
        Series = [new ColumnSeries<double>(2, 5, 4, 6, 3)],
        XAxes = [new Axis { Name = "Salesman", Labels = ["Sun", "Mon", "Tue", "Wed", "Thu"] }],
        YAxes = [new Axis { Name = "Sales" }],
    };

    // Axes/LabelsRotation
    internal static FrameworkElement AxesLabelsRotation() => new CartesianChart
    {
        Series = [new ColumnSeries<double>(2, 5, 4, 6, 3)],
        XAxes = [new Axis { Labels = ["January", "February", "March", "April", "May"], LabelsRotation = 45 }],
    };

    // Axes/LabelsFormat: a currency-formatted Y axis labeler.
    internal static FrameworkElement AxesLabelsFormat() => new CartesianChart
    {
        Series =
        [
            new ColumnSeries<double>(426, 583, 104),
            new ColumnSeries<double>(200, 558, 458),
        ],
        XAxes = [new Axis { Name = "Salesman", Labels = ["Sergio", "Lando", "Lewis"] }],
        YAxes = [new Axis { Name = "Sales", Labeler = value => value.ToString("C2") }],
    };

    // Axes/LabelsFormat2: named X labels with a currency-formatted Y axis.
    internal static FrameworkElement AxesLabelsFormat2() => new CartesianChart
    {
        Series = [new ColumnSeries<double>(426, 583, 104), new ColumnSeries<double>(200, 558, 458)],
        XAxes = [new Axis { Labels = ["Wang", "Zhao", "Zhang"] }],
        YAxes = [new Axis { Labeler = value => value.ToString("C2") }],
    };

    // Axes/Multiple: three series each scaled on its own Y axis.
    internal static FrameworkElement AxesMultiple() => new CartesianChart
    {
        Series =
        [
            new LineSeries<double>(14, 13, 14, 15, 17) { Name = "Tens", ScalesYAt = 0 },
            new LineSeries<double>(533, 586, 425, 579, 518) { Name = "Hundreds", ScalesYAt = 1 },
            new LineSeries<double>(5493, 7843, 4368, 9018, 3902) { Name = "Thousands", ScalesYAt = 2 },
        ],
        YAxes = [new Axis { Name = "Tens" }, new Axis { Name = "Hundreds" }, new Axis { Name = "Thousands" }],
    };

    // Axes/DateTimeScaled: a line over a date-time X axis.
    internal static FrameworkElement AxesDateTimeScaled() => new CartesianChart
    {
        Series = [new LineSeries<DateTimePoint>(new DateTimePoint[]
        {
            new() { DateTime = new(2021, 1, 1), Value = 3 },
            new() { DateTime = new(2021, 1, 2), Value = 6 },
            new() { DateTime = new(2021, 1, 3), Value = 5 },
            new() { DateTime = new(2021, 1, 4), Value = 3 },
            new() { DateTime = new(2021, 1, 5), Value = 5 },
            new() { DateTime = new(2021, 1, 6), Value = 8 },
            new() { DateTime = new(2021, 1, 7), Value = 6 },
        })],
        XAxes = [new DateTimeAxis(TimeSpan.FromDays(1), date => date.ToString("MMM dd"))],
    };

    // Axes/TimeSpanScaled: a line over a time-span X axis.
    internal static FrameworkElement AxesTimeSpanScaled() => new CartesianChart
    {
        Series = [new LineSeries<TimeSpanPoint>(new TimeSpanPoint[]
        {
            new() { TimeSpan = TimeSpan.FromMilliseconds(1), Value = 10 },
            new() { TimeSpan = TimeSpan.FromMilliseconds(2), Value = 6 },
            new() { TimeSpan = TimeSpan.FromMilliseconds(3), Value = 3 },
            new() { TimeSpan = TimeSpan.FromMilliseconds(4), Value = 12 },
            new() { TimeSpan = TimeSpan.FromMilliseconds(5), Value = 8 },
        })],
        XAxes = [new TimeSpanAxis(TimeSpan.FromMilliseconds(1), value => $"{value:fff}ms")],
    };

    // Axes/ColorsAndPosition: a red Y axis placed at the end (right) of the chart.
    internal static FrameworkElement AxesColorsAndPosition()
    {
        var red = new SolidColorPaint(new Color(255, 255, 0, 0));
        return new CartesianChart
        {
            Series = [new ColumnSeries<double>(2, 3, 8)],
            YAxes = [new Axis { Position = AxisPosition.End, LabelsPaint = red, SeparatorsPaint = red, TicksPaint = red }],
        };
    }

    // Axes/CustomSeparatorsInterval: the Y axis draws separators only at explicit values.
    internal static FrameworkElement AxesCustomSeparators() => new CartesianChart
    {
        Series = [new ColumnSeries<int>(10, 55, 45, 68, 60, 70, 75, 78)],
        YAxes = [new Axis { CustomSeparators = [0, 10, 25, 50, 100], SeparatorsPaint = new SolidColorPaint(new Color(100, 0, 0, 0)) }],
    };

    // Axes/MatchScale: X and Y share the same screen-to-data ratio (a square-units plot).
    internal static FrameworkElement AxesMatchScale()
    {
        var points = new List<ObservablePoint>();
        var fx = EasingFunctions.BounceInOut;
        for (var x = 0f; x < 1f; x += 0.02f) points.Add(new ObservablePoint(x - 0.5, fx(x) - 0.5));
        return new CartesianChart
        {
            MatchAxesScreenDataRatio = true,
            Series = [new LineSeries<ObservablePoint>(points) { GeometrySize = 0 }],
        };
    }

    // Axes/Crosshairs: axes with crosshair paints (the crosshair follows the pointer on hover).
    internal static FrameworkElement AxesCrosshairs() => new CartesianChart
    {
        Series = [new ColumnSeries<double>(200, 558, 458, 249, 457, 339, 587)],
        XAxes = [new Axis { CrosshairPaint = new SolidColorPaint(new Color(255, 255, 0, 0), 1) }],
        YAxes = [new Axis
        {
            CrosshairPaint = new SolidColorPaint(new Color(255, 255, 0, 0), 1),
            CrosshairLabelsPaint = new SolidColorPaint(new Color(255, 255, 255, 255)),
            Labeler = value => value.ToString("N2"),
        }],
    };

    // Axes/Style: a curve over fully styled axes (separators, sub-separators, zero line, ticks) and a frame.
    internal static FrameworkElement AxesStyle()
    {
        var gray = new Color(255, 195, 195, 195);
        var gray1 = new Color(255, 130, 130, 130);
        var gray2 = new Color(255, 225, 225, 225);

        Axis StyledAxis(string name) => new()
        {
            Name = name,
            NamePaint = new SolidColorPaint(gray1),
            LabelsPaint = new SolidColorPaint(gray),
            SeparatorsPaint = new SolidColorPaint(gray, 1),
            SubseparatorsPaint = new SolidColorPaint(gray2, 0.5f),
            SubseparatorsCount = 9,
            ZeroPaint = new SolidColorPaint(gray1, 2),
            TicksPaint = new SolidColorPaint(gray, 1.5f),
            SubticksPaint = new SolidColorPaint(gray, 1),
        };

        var points = new List<ObservablePoint>();
        var fx = EasingFunctions.BounceInOut;
        for (var x = 0f; x < 1f; x += 0.01f) points.Add(new ObservablePoint(x - 0.5, fx(x) - 0.5));

        return new CartesianChart
        {
            Series = [new LineSeries<ObservablePoint>(points)
            {
                GeometrySize = 0,
                Stroke = new SolidColorPaint(new Color(255, 33, 150, 243), 4),
                Fill = null,
            }],
            XAxes = [StyledAxis("X Axis")],
            YAxes = [StyledAxis("Y Axis")],
            DrawMarginFrame = new DrawMarginFrame { Stroke = new SolidColorPaint(gray, 2) },
        };
    }

    // Axes/Logarithmic: values are mapped into log space (Y -> log_base(Y)) so the axis labeler
    // (base^value) recovers the real magnitude. EnsureInitialized must run before HasMap so the
    // built-in mappers (double, etc.) are still registered.
    internal static FrameworkElement AxesLogarithmic()
    {
        const double logBase = 10;
        LiveChartsMewUI.EnsureInitialized();
        LiveCharts.Configure(config => config.HasMap<LogarithmicPoint>(
            (point, index) => new(point.X, Math.Log(point.Y, logBase))));

        return new CartesianChart
        {
            Series = [new LineSeries<LogarithmicPoint>([
                new(1, 1), new(2, 10), new(3, 100), new(4, 1000),
                new(5, 10000), new(6, 100000), new(7, 1000000), new(8, 10000000),
            ])],
            YAxes = [new LogarithmicAxis(logBase)],
        };
    }

    // Axes/Paging: buttons page through the data by setting the X axis min/max limits.
    internal static FrameworkElement AxesPaging()
    {
        var rnd = new Random(11);
        var trend = 100;
        var values = new int[100];
        for (var i = 0; i < 100; i++) { trend += rnd.Next(-30, 50); values[i] = trend; }

        var xAxis = new Axis();
        var chart = new CartesianChart { Series = [new ColumnSeries<int>(values)], XAxes = [xAxis] };

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
            var rnd = new Random(length);
            var t = 0;
            var values = new int[length];
            for (var i = 0; i < length; i++) { t += rnd.Next(-90, 100); values[i] = t; }
            return values;
        }

        var x1 = new Axis();
        var x2 = new Axis();
        SharedAxes.Set(x1, x2);

        var top = new CartesianChart { Series = [new LineSeries<int>(Fetch(100))], XAxes = [x1], ZoomMode = ZoomAndPanMode.X };
        var bottom = new CartesianChart { Series = [new ColumnSeries<int>(Fetch(100))], XAxes = [x2], ZoomMode = ZoomAndPanMode.X };

        // The two charts share an X axis (zoom/pan either to move both); returned as one main element.
        return Charts(top, bottom);
    }
}
