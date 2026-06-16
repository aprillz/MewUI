using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;

using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;

internal static partial class Samples
{
    // Pies/Basic
    internal static FrameworkElement PiesBasic() => new PieChart
    {
        Series =
        [
            new PieSeries<double>(10) { Name = "Mary" },
            new PieSeries<double>(20) { Name = "John" },
            new PieSeries<double>(30) { Name = "Alice" },
            new PieSeries<double>(40) { Name = "Bob" },
            new PieSeries<double>(50) { Name = "Charlie" },
        ],
    };

    // Pies/Doughnut
    internal static FrameworkElement PiesDoughnut() => new PieChart
    {
        Series =
        [
            new PieSeries<double>(10) { InnerRadius = 50 }, new PieSeries<double>(20) { InnerRadius = 50 },
            new PieSeries<double>(30) { InnerRadius = 50 }, new PieSeries<double>(40) { InnerRadius = 50 },
        ],
    };

    // Pies/Pushout
    internal static FrameworkElement PiesPushout() => new PieChart
    {
        Series =
        [
            new PieSeries<double>(10), new PieSeries<double>(20),
            new PieSeries<double>(30) { Pushout = 24 }, new PieSeries<double>(40),
        ],
    };

    // Pies/NightingaleRose: a rose (petals of different outer radius) with a common inner hole.
    // Offsets are kept small so every petal's outer radius stays above the inner radius in small cells.
    internal static FrameworkElement PiesNightingale() => new PieChart
    {
        Series =
        [
            new PieSeries<double>(10) { Name = "Mary", InnerRadius = 20, OuterRadiusOffset = 0 },
            new PieSeries<double>(20) { Name = "John", InnerRadius = 20, OuterRadiusOffset = 15 },
            new PieSeries<double>(30) { Name = "Alice", InnerRadius = 20, OuterRadiusOffset = 30 },
            new PieSeries<double>(40) { Name = "Bob", InnerRadius = 20, OuterRadiusOffset = 45 },
        ],
    };

    // Pies/OutLabels: slices with labels drawn outside the pie.
    internal static FrameworkElement PiesOutLabels()
    {
        var labels = new SolidColorPaint(new Color(255, 40, 40, 40));
        PieSeries<double> Slice(double value, string name) => new(value)
        {
            Name = name,
            DataLabelsPaint = labels,
            DataLabelsSize = 15,
            DataLabelsPosition = PolarLabelsPosition.Outer,
            DataLabelsFormatter = point => $"{point.Coordinate.PrimaryValue}",
        };
        return new PieChart
        {
            Series = [Slice(8, "Maria"), Slice(6, "Susan"), Slice(5, "Charles"), Slice(3, "Fiona"), Slice(3, "George")],
        };
    }

    // Pies/Nested: a hierarchical (country > zone > person) pie. Each NestedPieData is a series whose
    // 3-slot Values array stacks onto a concentric ring; the sums align so the rings nest hierarchically.
    internal static FrameworkElement PiesNested()
    {
        SalesRecord[] data =
        [
            new("Brazil", "North", "John", 10), new("Brazil", "North", "Mary", 5),
            new("Brazil", "South", "John", 20), new("Brazil", "South", "Mary", 8),
            new("Colombia", "East", "Carla", 15), new("Colombia", "East", "Charles", 15),
            new("Colombia", "West", "Carla", 25), new("Colombia", "West", "Charles", 25),
            new("Mexico", "Central", "Sophia", 30), new("Mexico", "Central", "Petter", 5),
            new("Mexico", "North", "Sophia", 30), new("Mexico", "North", "Petter", 5),
        ];

        double Sum(string country, string? zone = null, string? name = null) => data
            .Where(x => x.Country == country && (zone is null || x.Zone == zone) && (name is null || x.Name == name))
            .Sum(x => x.Value);

        NestedPieData[] pieData =
        [
            new("Brazil", [null, null, Sum("Brazil")], "#1976d2"),
            new("North", [null, Sum("Brazil", "North"), null], "#1e88e5"),
            new("John", [Sum("Brazil", "North", "John"), null, null], "#2196f3"),
            new("Mary", [Sum("Brazil", "North", "Mary"), null, null], "#42a5f5"),
            new("South", [null, Sum("Brazil", "South"), null], "#64b5f6"),
            new("John", [Sum("Brazil", "South", "John"), null, null], "#90caf9"),
            new("Mary", [Sum("Brazil", "South", "Mary"), null, null], "#bbdefb"),
            new("Colombia", [null, null, Sum("Colombia")], "#d32f2f"),
            new("East", [null, Sum("Colombia", "East"), null], "#e53935"),
            new("Carla", [Sum("Colombia", "East", "Carla"), null, null], "#f44336"),
            new("Charles", [Sum("Colombia", "East", "Charles"), null, null], "#ef5350"),
            new("West", [null, Sum("Colombia", "West"), null], "#e57373"),
            new("Carla", [Sum("Colombia", "West", "Carla"), null, null], "#ef9a9a"),
            new("Charles", [Sum("Colombia", "West", "Charles"), null, null], "#ffcdd2"),
            new("Mexico", [null, null, Sum("Mexico")], "#ffa000"),
            new("Central", [null, Sum("Mexico", "Central"), null], "#ffb300"),
            new("Sophia", [Sum("Mexico", "Central", "Sophia"), null, null], "#ffc107"),
            new("Petter", [Sum("Mexico", "Central", "Petter"), null, null], "#ffca28"),
            new("North", [null, Sum("Mexico", "North"), null], "#ffd54f"),
            new("Sophia", [Sum("Mexico", "North", "Sophia"), null, null], "#ffe082"),
            new("Petter", [Sum("Mexico", "North", "Petter"), null, null], "#ffecb3"),
        ];

        return new PieChart
        {
            Series = pieData.Select(x => new PieSeries<double?>
            {
                Name = x.Name,
                Values = x.Values,
                Stroke = new SolidColorPaint(new Color(255, 255, 255, 255), 2),
                Fill = new SolidColorPaint(Color.FromHex(x.Color)),
                HoverPushout = 0,
                DataLabelsFormatter = x.Formatter,
                DataLabelsSize = 14,
                ShowDataLabels = x.IsTotal,
                DataLabelsPosition = PolarLabelsPosition.Outer,
                IsVisibleAtLegend = x.IsTotal,
            }).ToArray(),
        };
    }

    // Pies/Icons: browser-share pie with the official SVG icons drawn as slice labels would require a
    // custom label geometry; this renders the equivalent labeled pie (names + shares).
    internal static FrameworkElement PiesIcons()
    {
        var labels = new SolidColorPaint(new Color(255, 255, 255, 255));
        PieSeries<double> Slice(double value, string name) => new(value)
        {
            Name = name,
            DataLabelsPaint = labels,
            DataLabelsPosition = PolarLabelsPosition.Middle,
            DataLabelsFormatter = point => name,
        };
        return new PieChart { Series = [Slice(65.72, "Chrome"), Slice(18.22, "Safari"), Slice(5.31, "Edge")] };
    }

    // Pies/AutoUpdate: pie slices whose values change on a timer.
    internal static FrameworkElement PiesAutoUpdate()
    {
        var v1 = new ObservableValue(2);
        var v2 = new ObservableValue(5);
        var v3 = new ObservableValue(4);
        var chart = new PieChart
        {
            Series =
            [
                new PieSeries<ObservableValue>(v1) { Name = "A" },
                new PieSeries<ObservableValue>(v2) { Name = "B" },
                new PieSeries<ObservableValue>(v3) { Name = "C" },
            ],
        };
        var timer = new DispatcherTimer().IntervalMs(1500).OnTick(() =>
        {
            v1.Value = Random.Next(1, 10);
            v2.Value = Random.Next(1, 10);
            v3.Value = Random.Next(1, 10);
        });
        timer.Start();
        Timers.Add(timer);
        return chart;
    }

    // Pies/Gauge (solid)
    internal static FrameworkElement GaugeSolid() => new PieChart
    {
        Series = GaugeGenerator.BuildSolidGauge(
            new GaugeItem(45, s => { s.Fill = new SolidColorPaint(new Color(255, 33, 150, 243)); s.MaxRadialColumnWidth = 40; }),
            new GaugeItem(GaugeItem.Background, s => { s.Fill = new SolidColorPaint(new Color(40, 33, 150, 243)); s.MaxRadialColumnWidth = 40; })),
        InitialRotation = -90,
        MaxAngle = 270,
        MaxValue = 100,
    };

    // Pies/Gauge1 "Basic gauge": one value, full circle, big centered label (theme colors it).
    internal static FrameworkElement PiesGauge1() => new PieChart
    {
        Series = GaugeGenerator.BuildSolidGauge(
            new GaugeItem(30, series => { series.MaxRadialColumnWidth = 50; series.DataLabelsSize = 50; })),
        InitialRotation = -90,
        MinValue = 0,
        MaxValue = 100,
    };

    // Pies/Gauge2 "270 degrees gauge": a 270-degree gauge with a centered red label and a background ring.
    internal static FrameworkElement PiesGauge2() => new PieChart
    {
        Series = GaugeGenerator.BuildSolidGauge(
            new GaugeItem(30, series =>
            {
                series.Fill = new SolidColorPaint(new Color(255, 154, 205, 50));
                series.DataLabelsSize = 50;
                series.DataLabelsPaint = new SolidColorPaint(new Color(255, 244, 67, 54));
                series.DataLabelsPosition = PolarLabelsPosition.ChartCenter;
                series.InnerRadius = 75;
            }),
            new GaugeItem(GaugeItem.Background, series =>
            {
                series.InnerRadius = 75;
                series.Fill = new SolidColorPaint(new Color(90, 100, 181, 246));
            })),
        InitialRotation = -225,
        MaxAngle = 270,
        MinValue = 0,
        MaxValue = 100,
    };

    // Pies/Gauge3 "Multiple values gauge": three labeled rings, each label at its arc start.
    internal static FrameworkElement PiesGauge3()
    {
        void SetStyle(string name, PieSeries<ObservableValue> series)
        {
            series.Name = name;
            series.DataLabelsPosition = PolarLabelsPosition.Start;
            series.DataLabelsFormatter = point => $"{point.Coordinate.PrimaryValue} {point.Context.Series.Name}";
            series.InnerRadius = 20;
            series.RelativeOuterRadius = 8;
            series.RelativeInnerRadius = 8;
        }
        return new PieChart
        {
            Series = GaugeGenerator.BuildSolidGauge(
                new GaugeItem(30, series => SetStyle("Vanessa", series)),
                new GaugeItem(50, series => SetStyle("Charles", series)),
                new GaugeItem(70, series => SetStyle("Ana", series)),
                new GaugeItem(GaugeItem.Background, series => { series.InnerRadius = 20; })),
            InitialRotation = 45,
            MaxAngle = 270,
            MinValue = 0,
            MaxValue = 100,
        };
    }

    // Pies/Gauge4 "Slim gauge": thin rings, labels (value only) at each arc end.
    internal static FrameworkElement PiesGauge4()
    {
        void SetStyle(string name, PieSeries<ObservableValue> series)
        {
            series.Name = name;
            series.DataLabelsSize = 20;
            series.DataLabelsPosition = PolarLabelsPosition.End;
            series.DataLabelsFormatter = point => point.Coordinate.PrimaryValue.ToString();
            series.InnerRadius = 20;
            series.MaxRadialColumnWidth = 5;
        }
        return new PieChart
        {
            Series = GaugeGenerator.BuildSolidGauge(
                new GaugeItem(50, series => SetStyle("Vanessa", series)),
                new GaugeItem(80, series => SetStyle("Charles", series)),
                new GaugeItem(95, series => SetStyle("Ana", series)),
                new GaugeItem(GaugeItem.Background, series => { series.Fill = null; })),
            InitialRotation = -90,
            MaxAngle = 350,
            MinValue = 0,
            MaxValue = 100,
        };
    }

    // Pies/Gauge5 "Auto updates on gauges": two values that change on a timer.
    internal static FrameworkElement PiesGauge5()
    {
        var north = new GaugeItem(50, series => { series.Name = "North"; series.DataLabelsPosition = PolarLabelsPosition.Start; });
        var south = new GaugeItem(80, series => { series.Name = "South"; series.DataLabelsPosition = PolarLabelsPosition.Start; });
        var chart = new PieChart
        {
            Series = GaugeGenerator.BuildSolidGauge(north, south),
            InitialRotation = -90,
            MaxAngle = 270,
            MinValue = 0,
            MaxValue = 100,
            LegendPosition = LegendPosition.Bottom,
        };
        var timer = new DispatcherTimer().IntervalMs(1500).OnTick(() =>
        {
            north.Value.Value = Random.Next(0, 100);
            south.Value.Value = Random.Next(0, 100);
        });
        timer.Start();
        Timers.Add(timer);
        return chart;
    }

    // Pies/AngularGauge (interactive): a "Change value" button moves the needle.
    internal static FrameworkElement AngularGaugeInteractive()
    {
        const double sectionsOuter = 130;
        const double sectionsWidth = 20;
        void SetStyle(PieSeries<ObservableValue> series)
        {
            series.OuterRadiusOffset = sectionsOuter;
            series.MaxRadialColumnWidth = sectionsWidth;
            series.CornerRadius = 0;
        }

        var needle = new NeedleVisual { Value = 45 };
        var chart = new PieChart
        {
            Series = GaugeGenerator.BuildAngularGaugeSections(
                new GaugeItem(60, SetStyle), new GaugeItem(30, SetStyle), new GaugeItem(10, SetStyle)),
            VisualElements =
            [
                new AngularTicksVisual { Labeler = value => value.ToString("N1"), LabelsSize = 16, LabelsOuterOffset = 15, OuterOffset = 65, TicksLength = 20 },
                needle,
            ],
            InitialRotation = -225,
            MaxAngle = 270,
            MinValue = 0,
            MaxValue = 100,
        };

        return WithActions(chart, ("Change value", () => needle.Value = Random.Next(0, 100)));
    }
}
