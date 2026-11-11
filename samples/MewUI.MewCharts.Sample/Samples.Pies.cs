using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel;
using LiveChartsCore.Measure;

internal static partial class Samples
{
    // The upstream pie views render in charts of roughly 400 px and the gallery cells in about 160 px,
    // so their absolute pixel sizes are multiplied by this factor to keep the proportions.
    private const double PIES_SCALE = 0.4;

    // Same idea for the gauge views the upstream caps at 300 px wide.
    private const double PIES_GAUGE_SMALL_SCALE = 0.6;

    // And for the gauge views the upstream caps at 400 px wide.
    private const double PIES_GAUGE_LARGE_SCALE = 0.5;

    // The angular gauge chart is 165 px high here against roughly 380 px upstream.
    private const double PIES_ANGULAR_SCALE = 0.43;

    // Pies/Gauge: the sliders keep the upstream 0..50 range and their value is multiplied by this factor.
    private const double PIES_GAUGE_SLIDER_SCALE = 0.35;
    private const double PIES_GAUGE_SLIDER_MAX = 50;

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
        // Upstream title: size 25, padding 15; scaled so it leaves room for the pie in the small cell.
        Title = new LabelVisual
        {
            Text = "My chart title",
            Paint = new SolidColorPaint(new Color(0, 0, 0)),
            TextSize = 16,
            Padding = new LiveChartsCore.Drawing.Padding(8),
        },
    };

    // Pies/Custom
    internal static FrameworkElement PiesCustom()
    {
        static string Format(ChartPoint point)
        {
            var primaryValue = point.Coordinate.PrimaryValue;
            var stacked = point.StackedValue!;
            return $"{primaryValue}/{stacked.Total}{Environment.NewLine}{stacked.Share:P2}";
        }

        PieSeries<double> Slice(string name, double value, double offset) => new(value)
        {
            Name = name,
            OuterRadiusOffset = offset * PIES_SCALE,
            // Upstream leaves the default size (16), scaled so two-line labels fit their slices.
            DataLabelsSize = 10,
            ShowDataLabels = true,
            ToolTipLabelFormatter = Format,
            DataLabelsFormatter = Format,
        };

        return new PieChart
        {
            InitialRotation = -90,
            Series = [Slice("Mary", 10, 0), Slice("John", 20, 50), Slice("Alice", 30, 100), Slice("Bob", 40, 150)],
        };
    }

    // Pies/Doughnut
    internal static FrameworkElement PiesDoughnut()
    {
        PieSeries<double> Slice(string name, double value) => new(value) { Name = name, MaxRadialColumnWidth = 60 * PIES_SCALE };
        return new PieChart
        {
            Series =
            [
                Slice("Mary", 10), Slice("John", 20), Slice("Alice", 30), Slice("Bob", 40), Slice("Charlie", 50),
            ],
        };
    }

    // Pies/Pushout
    internal static FrameworkElement PiesPushout()
    {
        PieSeries<double> Slice(string name, double value, double pushout) => new(value) { Name = name, Pushout = pushout * PIES_SCALE };
        return new PieChart
        {
            Series =
            [
                Slice("Maria", 8, 30), Slice("Susan", 6, 0), Slice("Charles", 5, 0), Slice("Fiona", 3, 0), Slice("George", 3, 0),
            ],
        };
    }

    // Pies/NightingaleRose
    internal static FrameworkElement PiesNightingale()
    {
        PieSeries<double> Petal(string name, double value, double offset) => new(value)
        {
            Name = name,
            OuterRadiusOffset = offset * PIES_SCALE,
            InnerRadius = 50 * PIES_SCALE,
        };
        return new PieChart
        {
            Series = [Petal("Mary", 10, 0), Petal("John", 20, 50), Petal("Alice", 30, 100), Petal("Bob", 40, 150)],
        };
    }

    // Pies/OutLabels
    internal static FrameworkElement PiesOutLabels()
    {
        // Upstream wraps this label at the max width, which the label geometry here does not do, so the break is in the text.
        static string Format(ChartPoint point) =>
            $"This slide takes {point.Coordinate.PrimaryValue}{Environment.NewLine}out of {point.StackedValue!.Total} parts";

        static string TooltipFormat(ChartPoint point) => $"{point.StackedValue!.Share:P2}";

        PieSeries<double> Slice(string name, double value) => new(value)
        {
            Name = name,
            // Upstream size 15 and default padding; scaled because the engine reserves 1.5x the label diagonal around the pie.
            DataLabelsSize = 8,
            DataLabelsPadding = new LiveChartsCore.Drawing.Padding(3, 4, 3, 4),
            ShowDataLabels = true,
            DataLabelsPosition = PolarLabelsPosition.Outer,
            DataLabelsFormatter = Format,
            ToolTipLabelFormatter = TooltipFormat,
        };

        return new PieChart
        {
            IsClockwise = false,
            InitialRotation = -90,
            Series = [Slice("Maria", 8), Slice("Susan", 6), Slice("Charles", 5), Slice("Fiona", 3), Slice("George", 3)],
        };
    }

    // Pies/Nested: a hierarchical (country > zone > person) pie. Each PieNestedData is a series whose
    // 3-slot Values array stacks onto a concentric ring; the sums align so the rings nest hierarchically.
    internal static FrameworkElement PiesNested()
    {
        PieSalesRecord[] data =
        [
            new("Brazil", "North", "John", 10), new("Brazil", "North", "Mary", 5),
            new("Brazil", "South", "John", 20), new("Brazil", "South", "Mary", 8),
            new("Colombia", "East", "Carla", 15), new("Colombia", "East", "Charles", 15),
            new("Colombia", "West", "Carla", 25), new("Colombia", "West", "Charles", 25),
            new("Mexico", "Central", "Sophia", 30), new("Mexico", "Central", "Petter", 5),
            new("Mexico", "North", "Sophia", 30), new("Mexico", "North", "Petter", 5),
        ];

        double Sum(string country, string? zone = null, string? name = null) => data
            .Where(record => record.Country == country && (zone is null || record.Zone == zone) && (name is null || record.Name == name))
            .Sum(record => record.Value);

        PieNestedData[] pieData =
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

        PieSeries<double?> ToSeries(PieNestedData item)
        {
            var series = new PieSeries<double?>
            {
                Name = item.Name,
                Values = item.Values,
                Stroke = new SolidColorPaint(new Color(255, 255, 255), 2),
                Fill = new SolidColorPaint(Color.FromHex(item.Color)),
                HoverPushout = 0,
                DataLabelsFormatter = item.Formatter,
                // Upstream size 20, scaled because the engine reserves 1.5x the label diagonal around the pie.
                DataLabelsSize = 10,
                DataLabelsPosition = PolarLabelsPosition.Outer,
                IsVisibleAtLegend = item.IsTotal,
            };

            if (item.IsTotal)
            {
                series.ShowDataLabels = true;
            }
            return series;
        }

        return new PieChart
        {
            InitialRotation = 120,
            LegendPosition = LegendPosition.Right,
            Series = pieData.Select(ToSeries).ToArray(),
        };
    }

    // Pies/Icons: browser-share pie whose data labels draw the SVG icon and the name (PieIconSeries).
    internal static FrameworkElement PiesIcons()
    {
        // Upstream icon 50 and name size 16, scaled to the small cell.
        const float ICON_SIZE = 30;
        const float NAME_SIZE = 10;
        PieIconSeries Slice(string name, string icon, double value) => new(name, icon, value, ICON_SIZE, NAME_SIZE)
        {
            ShowDataLabels = true,
        };

        return new PieChart
        {
            Series =
            [
                Slice("Chrome", PieIcons.Chrome, 65.72),
                Slice("Safari", PieIcons.Safari, 18.22),
                Slice("Edge", PieIcons.Edge, 5.31),
            ],
        };
    }

    // Pies/AutoUpdate: buttons add and remove series and randomize the values.
    internal static FrameworkElement PiesAutoUpdate()
    {
        var series = new ObservableCollection<ISeries>();
        var allValues = new List<ObservableCollection<ObservableValue>>();

        void AddSlice(string name, double value)
        {
            ObservableCollection<ObservableValue> values = [new(value)];
            allValues.Add(values);
            series.Add(new PieSeries<ObservableValue>(values) { Name = name });
        }

        AddSlice("Ana", 2);
        AddSlice("Juan", 8);

        var chart = new PieChart { Series = series };
        return WithActions(chart,
            ("Add series", () =>
            {
                if (series.Count != 15) AddSlice($"Slice #{series.Count}", Random.Next(2, 9));
            }),
            ("Remove series", () =>
            {
                if (series.Count != 1)
                {
                    series.RemoveAt(series.Count - 1);
                    allValues.RemoveAt(allValues.Count - 1);
                }
            }),
            ("Update Series", () =>
            {
                foreach (var values in allValues)
                    foreach (var item in values)
                        item.Value = Random.Next(1, 10);
            }));
    }

    // Pies/Gauge: four gauge values on stacked rings plus six sliders that restyle them.
    internal static FrameworkElement PiesGauge()
    {
        // The view model starts the radii at 100 and the sliders stop at 50, so the two-way binding clamps them.
        var innerRadius = Math.Min(100, PIES_GAUGE_SLIDER_MAX);
        var offsetRadius = 10d;
        var backgroundInnerRadius = Math.Min(100, PIES_GAUGE_SLIDER_MAX);
        var backgroundOffsetRadius = 20d;

        void SetStyle(PieSeries<ObservableValue> series)
        {
            series.InnerRadius = innerRadius * PIES_GAUGE_SLIDER_SCALE;
            series.RelativeInnerRadius = offsetRadius * PIES_GAUGE_SLIDER_SCALE;
            series.RelativeOuterRadius = offsetRadius * PIES_GAUGE_SLIDER_SCALE;
            series.DataLabelsFormatter = point => point.Coordinate.PrimaryValue.ToString();
            series.DataLabelsPosition = PolarLabelsPosition.Start;
            // Upstream size 30, scaled to the small cell.
            series.DataLabelsSize = 12;
        }

        void SetBackgroundStyle(PieSeries<ObservableValue> series)
        {
            series.Fill = new SolidColorPaint(new Color(10, 0, 0, 0));
            series.InnerRadius = backgroundInnerRadius * PIES_GAUGE_SLIDER_SCALE;
            series.RelativeInnerRadius = backgroundOffsetRadius * PIES_GAUGE_SLIDER_SCALE;
            series.RelativeOuterRadius = backgroundOffsetRadius * PIES_GAUGE_SLIDER_SCALE;
        }

        var gaugeSeries = GaugeGenerator.BuildSolidGauge(
            new GaugeItem(10, SetStyle),
            new GaugeItem(25, SetStyle),
            new GaugeItem(50, SetStyle),
            new GaugeItem(50, SetStyle),
            new GaugeItem(GaugeItem.Background, SetBackgroundStyle));

        var chart = new PieChart
        {
            Series = gaugeSeries,
            MaxValue = 60,
            MaxAngle = 270,
            InitialRotation = 135,
        };

        FrameworkElement Labeled(string caption, double minimum, double maximum, double value, Action<double> onChanged)
        {
            var slider = new Slider().Minimum(minimum).Maximum(maximum).Value(value).Width(134).Height(16);
            slider.ValueChanged += onChanged;
            return new StackPanel().Vertical().Children(new TextBlock().Text(caption).FontSize(ThemeFontSize.Small), slider);
        }

        var controls = new StackPanel()
            .Vertical()
            .Spacing(2)
            .Children(
                Labeled("Initial rotation", -360, 720, chart.InitialRotation, value => chart.InitialRotation = value),
                Labeled("Max angle", 0, 360, chart.MaxAngle, value => chart.MaxAngle = value),
                Labeled("Inner radius", 0, PIES_GAUGE_SLIDER_MAX, innerRadius, value =>
                {
                    innerRadius = value;
                    foreach (var item in gaugeSeries.Where(candidate => !candidate.IsFillSeries))
                        item.InnerRadius = value * PIES_GAUGE_SLIDER_SCALE;
                }),
                Labeled("Offset radius", 0, PIES_GAUGE_SLIDER_MAX, offsetRadius, value =>
                {
                    offsetRadius = value;
                    foreach (var item in gaugeSeries.Where(candidate => !candidate.IsFillSeries))
                    {
                        item.RelativeInnerRadius = value * PIES_GAUGE_SLIDER_SCALE;
                        item.RelativeOuterRadius = value * PIES_GAUGE_SLIDER_SCALE;
                    }
                }),
                Labeled("Background inner radius", 0, PIES_GAUGE_SLIDER_MAX, backgroundInnerRadius, value =>
                {
                    backgroundInnerRadius = value;
                    foreach (var item in gaugeSeries.Where(candidate => candidate.IsFillSeries))
                        item.InnerRadius = value * PIES_GAUGE_SLIDER_SCALE;
                }),
                Labeled("Background offset radius", 0, PIES_GAUGE_SLIDER_MAX, backgroundOffsetRadius, value =>
                {
                    backgroundOffsetRadius = value;
                    foreach (var item in gaugeSeries.Where(candidate => candidate.IsFillSeries))
                    {
                        item.RelativeInnerRadius = value * PIES_GAUGE_SLIDER_SCALE;
                        item.RelativeOuterRadius = value * PIES_GAUGE_SLIDER_SCALE;
                    }
                }));

        // The upstream view puts the sliders in a column left of the chart.
        return new StackPanel()
            .Horizontal()
            .Spacing(6)
            .Children(controls, chart.Width(160).Height(200));
    }

    // Pies/Gauge1 "Basic gauge": one value, full circle, big centered label (theme colors it).
    internal static FrameworkElement PiesGauge1() => new PieChart
    {
        Series = GaugeGenerator.BuildSolidGauge(
            new GaugeItem(30, series =>
            {
                series.MaxRadialColumnWidth = 50 * PIES_GAUGE_SMALL_SCALE;
                series.DataLabelsSize = 50 * PIES_GAUGE_SMALL_SCALE;
            })),
        InitialRotation = -90,
        MinValue = 0,
        MaxValue = 100,
    };

    // Pies/Gauge2 "270 degrees gauge": a 270-degree gauge with a centered themed label and a background ring.
    internal static FrameworkElement PiesGauge2() => new PieChart
    {
        Series = GaugeGenerator.BuildSolidGauge(
            new GaugeItem(30, series =>
            {
                series.Fill = new SolidColorPaint(new Color(154, 205, 50));
                series.ShowDataLabels = true;
                series.DataLabelsSize = 50 * PIES_GAUGE_SMALL_SCALE;
                series.DataLabelsPosition = PolarLabelsPosition.ChartCenter;
                series.InnerRadius = 75 * PIES_GAUGE_SMALL_SCALE;
            }),
            new GaugeItem(GaugeItem.Background, series =>
            {
                series.InnerRadius = 75 * PIES_GAUGE_SMALL_SCALE;
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
            series.InnerRadius = 20 * PIES_GAUGE_LARGE_SCALE;
            series.RelativeOuterRadius = 8 * PIES_GAUGE_LARGE_SCALE;
            series.RelativeInnerRadius = 8 * PIES_GAUGE_LARGE_SCALE;
        }

        return new PieChart
        {
            Series = GaugeGenerator.BuildSolidGauge(
                new GaugeItem(30, series => SetStyle("Vanesa", series)),
                new GaugeItem(50, series => SetStyle("Charles", series)),
                new GaugeItem(70, series => SetStyle("Ana", series)),
                new GaugeItem(GaugeItem.Background, series => series.InnerRadius = 20 * PIES_GAUGE_LARGE_SCALE)),
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
            series.DataLabelsSize = 20 * PIES_GAUGE_LARGE_SCALE;
            series.DataLabelsPosition = PolarLabelsPosition.End;
            series.DataLabelsFormatter = point => point.Coordinate.PrimaryValue.ToString();
            series.InnerRadius = 20 * PIES_GAUGE_LARGE_SCALE;
            series.MaxRadialColumnWidth = 5 * PIES_GAUGE_LARGE_SCALE;
        }

        return new PieChart
        {
            Series = GaugeGenerator.BuildSolidGauge(
                new GaugeItem(50, series => SetStyle("Vanessa", series)),
                new GaugeItem(80, series => SetStyle("Charles", series)),
                new GaugeItem(95, series => SetStyle("Ana", series)),
                new GaugeItem(GaugeItem.Background, series => series.Fill = null)),
            InitialRotation = -90,
            MaxAngle = 350,
            MinValue = 0,
            MaxValue = 100,
        };
    }

    // Pies/Gauge5 "Auto updates on gauges": a button sets two values to random numbers.
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

        return WithActions(chart, ("Change Value", () =>
        {
            north.Value.Value = Random.Next(0, 100);
            south.Value.Value = Random.Next(0, 100);
        }));
    }

    // Pies/AngularGauge: a "Change value" button moves the needle.
    internal static FrameworkElement AngularGaugeInteractive()
    {
        void SetStyle(PieSeries<ObservableValue> series)
        {
            series.OuterRadiusOffset = 130 * PIES_ANGULAR_SCALE;
            series.MaxRadialColumnWidth = 20 * PIES_ANGULAR_SCALE;
            series.CornerRadius = 0;
        }

        var needle = new NeedleVisual { Value = 45, Width = 20 * PIES_ANGULAR_SCALE };
        var chart = new PieChart
        {
            // The upstream view declares no background series, so the generator's one is hidden.
            Series = GaugeGenerator.BuildAngularGaugeSections(
                new GaugeItem(60, SetStyle),
                new GaugeItem(30, SetStyle),
                new GaugeItem(10, SetStyle),
                new GaugeItem(GaugeItem.Background, series => series.Fill = null)),
            VisualElements =
            [
                needle,
                new AngularTicksVisual
                {
                    // Upstream label size 16, scaled with a floor so the numbers stay readable.
                    LabelsSize = 10,
                    LabelsOuterOffset = 15 * PIES_ANGULAR_SCALE,
                    OuterOffset = 65 * PIES_ANGULAR_SCALE,
                    TicksLength = 20 * PIES_ANGULAR_SCALE,
                },
            ],
            InitialRotation = -225,
            MaxAngle = 270,
            MinValue = 0,
            MaxValue = 100,
        };

        return WithActions(chart, ("Change value", () => needle.Value = Random.Next(0, 100)));
    }
}
