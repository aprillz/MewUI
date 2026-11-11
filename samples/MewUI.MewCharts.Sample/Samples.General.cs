using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;

using GeneralSamples;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;
using LiveChartsCore.VisualStates;

internal static partial class Samples
{
    // General/FirstChart: a line and a column series with the legend on the right.
    internal static FrameworkElement GeneralFirstChart() => new CartesianChart
    {
        LegendPosition = LegendPosition.Right,
        Series =
        [
            new LineSeries<double>(5, 10, 8, 4) { Name = "Mary" },
            new ColumnSeries<double>(4, 7, 3, 8) { Name = "Ana" },
        ],
    };

    // General/RealTime: a point every 100 ms from a timer, trimmed to the last 250, on a seconds axis.
    internal static FrameworkElement RealTime()
    {
        var values = new ObservableCollection<DateTimePoint>();
        var sync = new object();

        var xAxis = new DateTimeAxis(TimeSpan.FromSeconds(1), RealTimeFormatter)
        {
            AnimationsSpeed = TimeSpan.Zero,
            SeparatorsPaint = new SolidColorPaint(Color.FromHex("#64B4B4B4")),
            CustomSeparators = [],
        };

        var chart = new CartesianChart
        {
            SyncContext = sync,
            Series =
            [
                new LineSeries<DateTimePoint>(values)
                {
                    Fill = null,
                    GeometryFill = null,
                    GeometryStroke = null,
                },
            ],
            XAxes = [xAxis],
        };

        var timer = new DispatcherTimer()
            .IntervalMs(100)
            .OnTick(() =>
            {
                lock (sync)
                {
                    values.Add(new DateTimePoint(DateTime.Now, Random.Next(0, 10)));
                    if (values.Count > 250) values.RemoveAt(0);

                    xAxis.CustomSeparators = GetRealTimeSeparators();
                }
            });
        timer.Start();
        Timers.Add(timer);

        return chart;
    }

    private static double[] GetRealTimeSeparators()
    {
        var now = DateTime.Now;

        return
        [
            now.AddSeconds(-25).Ticks,
            now.AddSeconds(-20).Ticks,
            now.AddSeconds(-15).Ticks,
            now.AddSeconds(-10).Ticks,
            now.AddSeconds(-5).Ticks,
            now.Ticks,
        ];
    }

    private static string RealTimeFormatter(DateTime date)
    {
        var secondsAgo = (DateTime.Now - date).TotalSeconds;

        return secondsAgo < 1
            ? "now"
            : $"{secondsAgo:N0}s ago";
    }

    // General/NullPoints: null values and null points leave gaps in the lines.
    internal static FrameworkElement NullPoints()
    {
        ObservablePoint?[] values3 =
        [
            new ObservablePoint { X = 0, Y = 1 },
            new ObservablePoint { X = 1, Y = 4 },
            null,
            new ObservablePoint { X = 4, Y = 5 },
            new ObservablePoint { X = 6, Y = 1 },
            new ObservablePoint { X = 8, Y = 6 },
        ];

        return new CartesianChart
        {
            Series =
            [
                new LineSeries<double?>(5, 4, null, 3, 2, 6, 5, 6, 2),
                new LineSeries<double?>(2, 6, 5, 3, null, 5, 2, 4, null),
                new LineSeries<ObservablePoint?>(values3),
            ],
        };
    }

    // General/Sections: an X band, an X and Y box and an open-ended labelled band behind a scatter series.
    internal static FrameworkElement GeneralSections() => new CartesianChart
    {
        Series = [new ScatterSeries<ObservablePoint>(new ObservablePoint[]
        {
            new(2.2, 5.4), new(4.5, 2.5), new(4.2, 7.4), new(6.4, 9.9), new(8.9, 3.9), new(9.9, 5.2),
        })],
        Sections =
        [
            new RectangularSection
            {
                Xi = 3,
                Xj = 4,
                Fill = new SolidColorPaint(Color.FromHex("#FFCDD2")),
            },
            new RectangularSection
            {
                Xi = 5,
                Xj = 6,
                Yi = 2,
                Yj = 8,
                Fill = new SolidColorPaint(Color.FromHex("#BBDEFB")),
            },
            new RectangularSection
            {
                Xi = 8,
                Xj = double.NaN,
                Label = "A section here!",
                LabelSize = 14,
                LabelPaint = new SolidColorPaint(Color.FromHex("#FF6F00")),
                Fill = new SolidColorPaint(Color.FromHex("#F9FBE7")),
            },
        ],
    };

    // General/Sections2: a dashed line at Y = 8 and a translucent X band over an outlined scatter series.
    internal static FrameworkElement GeneralSections2() => new CartesianChart
    {
        Series =
        [
            new ScatterSeries<ObservablePoint>(new ObservablePoint[]
            {
                new(2.2, 5.4), new(4.5, 2.5), new(4.2, 7.4),
                new(6.4, 9.9), new(4.2, 9.2), new(5.8, 3.5),
                new(7.3, 5.8), new(8.9, 3.9), new(6.1, 4.6),
                new(9.4, 7.7), new(8.4, 8.5), new(3.6, 9.6),
                new(4.4, 6.3), new(5.8, 4.8), new(6.9, 3.4),
                new(7.6, 1.8), new(8.3, 8.3), new(9.9, 5.2),
                new(8.1, 4.7), new(7.4, 3.9), new(6.8, 2.3),
            })
            {
                GeometrySize = 10,
                Stroke = new SolidColorPaint(Color.Blue),
                Fill = null,
            },
        ],
        Sections =
        [
            new RectangularSection
            {
                Yi = 8,
                Yj = 8,
                // Upstream dashes are 6 px; DashArray is in stroke-width units (3 px).
                Stroke = new SolidColorPaint(Color.Red, 3) { DashArray = [2, 2] },
            },
            new RectangularSection
            {
                Xi = 6,
                Xj = 8,
                Fill = new SolidColorPaint(Color.FromHex("#300000FF")),
            },
        ],
    };

    // General/Legends: fourteen named column series and a combo box over the legend position.
    internal static FrameworkElement GeneralLegends()
    {
        (string Name, double[] Values)[] data =
        [
            ("Peru", [3, 7, 3]),
            ("Colombia", [4, 6, 2]),
            ("Mexico", [5, 5, 5]),
            ("Argentina", [6, 4, 6]),
            ("Chile", [7, 3, 7]),
            ("Ecuador", [8, 2, 8]),
            ("Bolivia", [9, 1, 9]),
            ("Venezuela", [10, 0, 10]),
            ("Uruguay", [11, 1, 11]),
            ("Paraguay", [12, 2, 12]),
            ("Brazil", [13, 3, 13]),
            ("Costa Rica", [14, 4, 14]),
            ("Panama", [15, 5, 15]),
            ("Honduras", [16, 6, 16]),
        ];

        LegendPosition[] positions =
        [
            LegendPosition.Hidden,
            LegendPosition.Top,
            LegendPosition.Bottom,
            LegendPosition.Right,
            LegendPosition.Left,
        ];

        var chart = new CartesianChart
        {
            LegendPosition = LegendPosition.Left,
            Series = [.. data.Select(item => new ColumnSeries<double>(item.Values) { Name = item.Name })],
        };

        var positionComboBox = new ComboBox()
            .Width(120)
            .Items(positions, position => position.ToString())
            .SelectedIndex(Array.IndexOf(positions, LegendPosition.Left));
        positionComboBox.SelectionChanged += _ =>
        {
            if (positionComboBox.SelectedIndex >= 0) chart.LegendPosition = positions[positionComboBox.SelectedIndex];
        };

        return WithControls(chart, positionComboBox);
    }

    // General/Tooltips: a column and a line series and a combo box over the tooltip position.
    internal static FrameworkElement GeneralTooltips()
    {
        TooltipPosition[] positions =
        [
            TooltipPosition.Hidden,
            TooltipPosition.Top,
            TooltipPosition.Bottom,
            TooltipPosition.Right,
            TooltipPosition.Left,
            TooltipPosition.Center,
        ];

        var chart = new CartesianChart
        {
            TooltipPosition = TooltipPosition.Top,
            Series =
            [
                new ColumnSeries<double>(3, 7, 3, 1, 4, 5, 6) { Name = "Sales" },
                new LineSeries<double>(2, 1, 3, 5, 3, 4, 6) { Name = "Customers" },
            ],
        };

        var positionComboBox = new ComboBox()
            .Width(120)
            .Items(positions, position => position.ToString())
            .SelectedIndex(Array.IndexOf(positions, TooltipPosition.Top));
        positionComboBox.SelectionChanged += _ =>
        {
            if (positionComboBox.SelectedIndex >= 0) chart.TooltipPosition = positions[positionComboBox.SelectedIndex];
        };

        return WithControls(chart, positionComboBox);
    }

    // General/Visibility: one button per column series toggles its visibility.
    internal static FrameworkElement GeneralVisibilityInteractive()
    {
        var series1 = new ColumnSeries<double>(2, 5, 4, 3);
        var series2 = new ColumnSeries<double>(1, 2, 3, 4);
        var series3 = new ColumnSeries<double>(4, 3, 2, 1);
        var chart = new CartesianChart { Series = [series1, series2, series3] };
        return WithActions(chart,
            ("toggle 1", () => series1.IsVisible = !series1.IsVisible),
            ("toggle 2", () => series2.IsVisible = !series2.IsVisible),
            ("toggle 3", () => series3.IsVisible = !series3.IsVisible));
    }

    // General/VisualElements: ten custom visuals drawn on an empty chart.
    internal static FrameworkElement GeneralVisualElements() => new CartesianChart
    {
        VisualElements =
        [
            new RectangleVisual(),
            new ScaledRectangleVisual(),
            new PointerDownAwareVisual(),
            new SvgVisual(),
            new ThemedVisual(),
            new CustomVisual(),
            new AbsoluteVisual(),
            new StackedVisual(),
            new TableVisual(),
            new ContainerVisual(),
        ],
    };

    // General/UserDefinedTypes: a line of cities mapped through LiveCharts.Configure, with a custom tooltip label.
    internal static FrameworkElement GeneralUserDefinedTypes()
    {
        LiveChartsMewUI.EnsureInitialized();
        LiveCharts.Configure(config => config.HasMap<City>((city, index) => new(index, city.Population)));

        return new CartesianChart
        {
            Series =
            [
                new LineSeries<City>(new City[]
                {
                    new("Tokyo", 4), new("New York", 6), new("Seoul", 2),
                    new("Moscow", 8), new("Shanghai", 3), new("Guadalajara", 4),
                })
                {
                    Name = "Population",
                    YToolTipLabelFormatter = point =>
                    {
                        var city = (City)point.Context.DataSource!;
                        return $"{city.Population}M people in {city.Name}";
                    },
                },
            ],
        };
    }

    // General/ConditionalDraw: values above 5 switch the column and its label to the danger states.
    internal static FrameworkElement GeneralConditionalDraw()
    {
        var values = new ObservableCollection<ObservableValue> { new(2), new(3), new(4) };

        var series = new ColumnSeries<ObservableValue>(values)
        {
            ShowDataLabels = true,
            DataLabelsSize = 15,
        };
        series.HasState("Danger", [(nameof(IDrawnElement.Fill), new SolidColorPaint(Color.Red))]);
        series.HasState("LabelDanger",
        [
            (nameof(IDrawnElement.Paint), new SolidColorPaint(Color.Red)),
            (nameof(BaseLabelGeometry.TextSize), 30f),
        ]);
        series.HasState("Hover",
        [
            // Upstream {lvc:Shadow '4, 16, #00f'}: offset 4 on both axes, sigma 16 on both axes, blue.
            (nameof(IDrawnElement.DropShadow), new LvcDropShadow(4, 4, 16, 16, new LvcColor(0, 0, 255))),
        ]);
        series.PointMeasured += point =>
        {
            var context = point.Context;
            if (context.DataSource is not ObservableValue observable) return;

            var states = context.Series.VisualStates;

            if (observable.Value > 5)
            {
                states.SetState("Danger", context.Visual);
                states.SetState("LabelDanger", context.Label);
            }
            else
            {
                states.ClearState("Danger", context.Visual);
                states.ClearState("LabelDanger", context.Label);
            }
        };

        var timer = new DispatcherTimer()
            .IntervalMs(3000)
            .OnTick(() =>
            {
                foreach (var item in values) item.Value = Random.Next(0, 10);
            });
        timer.Start();
        Timers.Add(timer);

        return new CartesianChart
        {
            TooltipPosition = TooltipPosition.Bottom,
            Series = [series],
        };
    }

    // General/MapPoints: each column gets a paint chosen by its index when it is measured.
    internal static FrameworkElement GeneralMapPoints()
    {
        SolidColorPaint[] paints =
        [
            new(Color.Red),
            new(Color.Green),
            new(Color.Blue),
            new(Color.Yellow),
        ];

        var series = new ColumnSeries<int>(2, 5, 4, 6, 8, 3, 2, 4, 6)
        {
            ShowDataLabels = true,
            DataLabelsPosition = DataLabelsPosition.Top,
        };
        series.PointMeasured += point =>
        {
            if (point.Context.Visual is null) return;
            point.Context.Visual.Fill = paints[point.Index % paints.Length];
        };

        return new CartesianChart { Series = [series] };
    }

    // General/Scrollable: dragging the lower chart pans the main chart; zooming the main chart moves the thumb.
    internal static FrameworkElement GeneralScrollableInteractive()
    {
        var trend = 1000;
        var values = new ObservablePoint[500];
        for (var index = 0; index < 500; index++) values[index] = new ObservablePoint(index, trend += Random.Next(-20, 20));

        // Upstream binds MinX/MaxX, which start at 0 and 0 (an empty range); the thumb's default 0..100 range is used instead.
        var mainAxis = new Axis { MinLimit = 0, MaxLimit = 100 };
        var thumb = new RectangularSection
        {
            Xi = 0,
            Xj = 100,
            Fill = new SolidColorPaint(Color.FromHex("#64FFCDD2")),
        };
        var viewModel = new ScrollableViewModel(values, mainAxis, thumb);

        // Upstream margins are 100 and 50 px; halved to fit the 300 px cell.
        var drawMargin = new Margin(50, Margin.Auto, 25, Margin.Auto);

        var mainChart = new CartesianChart
        {
            DrawMargin = drawMargin,
            ZoomMode = ZoomAndPanMode.X,
            Series =
            [
                new LineSeries<ObservablePoint>(values)
                {
                    GeometryStroke = null,
                    GeometryFill = null,
                    DataPadding = new LvcPoint(0, 1),
                },
            ],
            XAxes = [mainAxis],
        };
        mainChart.UpdateStarted += viewModel.ChartUpdated;

        var scrollBarChart = new PointerCartesianChart
        {
            DrawMargin = drawMargin,
            TooltipPosition = TooltipPosition.Hidden,
            Series =
            [
                new LineSeries<ObservablePoint>(values)
                {
                    GeometryStroke = null,
                    GeometryFill = null,
                    DataPadding = new LvcPoint(0, 1),
                },
            ],
            XAxes = [new Axis { IsVisible = false }],
            YAxes = [new Axis { IsVisible = false }],
            Sections = [thumb],
        };
        scrollBarChart.PointerPressed += viewModel.PointerDown;
        scrollBarChart.PointerMoved += viewModel.PointerMove;
        scrollBarChart.PointerReleased += viewModel.PointerUp;

        // Upstream gives the scroll bar chart a 100 px row; halved to fit the 200 px cell.
        return new Grid()
            .Rows("*,50")
            .Children(mainChart, scrollBarChart.Row(1));
    }

    // General/MultiThreading: ten tasks add points while holding the lock that is also the chart's SyncContext.
    internal static FrameworkElement GeneralMultiThreading()
    {
        var viewModel = new MultiThreadingViewModel();

        return new CartesianChart
        {
            SyncContext = viewModel.Sync,
            Series =
            [
                new LineSeries<int>(viewModel.Values)
                {
                    GeometryFill = null,
                    GeometryStroke = null,
                    LineSmoothness = 0,
                },
            ],
        };
    }

    // General/MultiThreading2: ten tasks add points through the UI thread dispatcher, without a lock.
    internal static FrameworkElement GeneralMultiThreading2()
    {
        var viewModel = new MultiThreading2ViewModel(InvokeOnUIThread);

        return new CartesianChart
        {
            Series =
            [
                new LineSeries<int>(viewModel.Values)
                {
                    GeometryFill = null,
                    GeometryStroke = null,
                    LineSmoothness = 0,
                },
            ],
        };
    }

    private static void InvokeOnUIThread(Action action)
    {
        if (Application.IsRunning && Application.Current.Dispatcher is IDispatcher dispatcher)
            dispatcher.Invoke(action);
    }

    // General/ChartToImage: a Cartesian and a pie chart, each written to a PNG file once loaded.
    internal static FrameworkElement GeneralChartToImage()
    {
        var cartesianChart = new CartesianChart
        {
            Series =
            [
                new LineSeries<double>(1, 5, 4, 6),
                new ColumnSeries<double>(4, 8, 2, 4),
            ],
        };

        var pieChart = new PieChart
        {
            Series =
            [
                new PieSeries<double>(10),
                new PieSeries<double>(6),
                new PieSeries<double>(4),
            ],
        };

        var cartesianExport = new ChartImageExport(cartesianChart, Path.Combine(Path.GetTempPath(), "CartesianImageFromControl.png"));
        var pieExport = new ChartImageExport(pieChart, Path.Combine(Path.GetTempPath(), "PieImageFromControl.png"));

        var status = new TextBlock()
            .FontSize(ThemeFontSize.Small)
            .TextWrapping(TextWrapping.Wrap)
            .Text("Writing the chart images...");

        var timer = new DispatcherTimer().IntervalMs(250);
        timer.OnTick(() =>
        {
            cartesianExport.TrySave();
            pieExport.TrySave();
            if (!cartesianExport.IsSaved || !pieExport.IsSaved) return;

            status.Text = $"{cartesianExport.Path}\n{pieExport.Path}";
            timer.Stop();
        });
        timer.Start();
        Timers.Add(timer);

        return new Grid()
            .Rows("*,*,Auto")
            .Children(cartesianChart, pieChart.Row(1), status.Row(2));
    }

    // General/DrawOnCanvas: an animated circle drawn at (5, 5) on an empty chart; any press resizes it.
    internal static FrameworkElement GeneralDrawOnCanvas()
    {
        var viewModel = new DrawOnCanvasViewModel();

        var chart = new PointerCartesianChart();
        chart.UpdateStarted += viewModel.ChartUpdated;
        chart.PointerPressed += (sender, _) => viewModel.ChartPressed(sender);

        return chart;
    }

    // General/TemplatedTooltips: a custom tooltip showing each point's value as currency and its SVG shape.
    internal static FrameworkElement GeneralTemplatedTooltips()
    {
        LiveChartsMewUI.EnsureInitialized();
        LiveCharts.Configure(config =>
            config.HasMap<GeometryPoint>((dataPoint, index) => new(index, dataPoint.Value)));

        GeometryPoint[] values =
        [
            new GeometryPoint { Value = 4, Geometry = SVGPoints.Star },
            new GeometryPoint { Value = 2, Geometry = SVGPoints.Square },
            new GeometryPoint { Value = 6, Geometry = SVGPoints.Circle },
            new GeometryPoint { Value = 3, Geometry = SVGPoints.Cross },
            new GeometryPoint { Value = 5, Geometry = SVGPoints.Diamond },
        ];

        return new CartesianChart
        {
            Tooltip = new CustomTooltip(),
            Series = [new ColumnSeries<GeometryPoint>(values)],
        };
    }

    // General/TemplatedLegends: a custom legend with large miniatures and series names.
    internal static FrameworkElement GeneralTemplatedLegends() => new CartesianChart
    {
        LegendPosition = LegendPosition.Right,
        Legend = new CustomLegend(),
        Series =
        [
            new ColumnSeries<double>(2, 1, 3, 5, 3, 4, 6) { Name = "Roger" },
            new ColumnSeries<double>(1, 3, 2, 7, 3, 5, 2) { Name = "Susan" },
        ],
    };

    // General/TooltipHoverArea: the tooltip shows only while the pointer is over a column.
    internal static FrameworkElement GeneralTooltipHoverArea() => new CartesianChart
    {
        Series =
        [
            new CustomColumnSeries<double>(37.833, 27.058, 21.516, 20.742, 15.029) { Name = "Asia" },
            new CustomColumnSeries<double>(12.537, 9.304, 5.383, 3.769, 3.223) { Name = "Europe" },
        ],
    };
}
