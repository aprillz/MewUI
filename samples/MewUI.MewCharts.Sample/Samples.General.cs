using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Drawing.Geometries;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;

internal static partial class Samples
{
    // General/RealTime: a line whose values update on a UI-thread timer.
    internal static FrameworkElement RealTime()
    {
        var values = new ObservableCollection<ObservableValue>();
        for (var i = 0; i < 10; i++) values.Add(new ObservableValue(Random.Next(0, 10)));

        var chart = new CartesianChart { Series = [new LineSeries<ObservableValue>(values)] };

        var timer = new DispatcherTimer()
            .IntervalMs(1000)
            .OnTick(() =>
            {
                values.Add(new ObservableValue(Random.Next(0, 10)));
                if (values.Count > 12) values.RemoveAt(0);
            });
        timer.Start();
        Timers.Add(timer);

        return chart;
    }

    // General/NullPoints: lines whose null values leave gaps.
    internal static FrameworkElement NullPoints() => new CartesianChart
    {
        Series =
        [
            new LineSeries<double?>(5, 4, null, 3, 2, 6, 5, 6, 2),
            new LineSeries<double?>(2, 6, 5, 3, null, 5, 2, 4, null),
        ],
    };

    // General/Sections: highlight bands behind a scatter series.
    internal static FrameworkElement GeneralSections() => new CartesianChart
    {
        Series = [new ScatterSeries<ObservablePoint>(new ObservablePoint[]
        {
            new(2.2, 5.4), new(4.5, 2.5), new(4.2, 7.4), new(6.4, 9.9), new(8.9, 3.9), new(9.9, 5.2),
        })],
        Sections =
        [
            new RectangularSection { Xi = 3, Xj = 4, Fill = new SolidColorPaint(new Color(80, 255, 0, 0)) },
            new RectangularSection { Xi = 5, Xj = 6, Yi = 2, Yj = 8, Fill = new SolidColorPaint(new Color(80, 0, 0, 255)) },
        ],
    };

    // General/Sections2: a scatter with several highlight bands.
    internal static FrameworkElement GeneralSections2() => new CartesianChart
    {
        Series = [new ScatterSeries<ObservablePoint>(new ObservablePoint[]
        {
            new(2.2, 5.4), new(4.5, 2.5), new(4.2, 7.4), new(6.4, 9.9), new(4.2, 9.2), new(5.8, 3.5),
            new(7.3, 5.8), new(8.9, 3.9), new(6.1, 4.6), new(9.4, 7.7), new(8.4, 8.5), new(3.6, 9.6),
        })],
        Sections =
        [
            new RectangularSection { Xi = 3, Xj = 4, Fill = new SolidColorPaint(new Color(60, 255, 0, 0)) },
            new RectangularSection { Yi = 6, Yj = 8, Fill = new SolidColorPaint(new Color(60, 0, 0, 255)) },
            new RectangularSection { Xi = 7, Xj = 9, Yi = 2, Yj = 4, Fill = new SolidColorPaint(new Color(60, 0, 160, 0)) },
        ],
    };

    // General/Legends: a legend pinned to the right of the chart.
    internal static FrameworkElement GeneralLegends() => new CartesianChart
    {
        Series =
        [
            new LineSeries<double>(2, 1, 3, 5, 3, 4, 6) { Name = "Series 1" },
            new LineSeries<double>(4, 2, 5, 2, 4, 5, 3) { Name = "Series 2" },
        ],
        LegendPosition = LegendPosition.Right,
    };

    // General/Tooltips: a chart with the tooltip pinned to the top (shows on hover).
    internal static FrameworkElement GeneralTooltips() => new CartesianChart
    {
        Series =
        [
            new LineSeries<double>(2, 1, 3, 5, 3, 4, 6) { Name = "Series 1" },
            new ColumnSeries<double>(4, 2, 5, 2, 4, 5, 3) { Name = "Series 2" },
        ],
        TooltipPosition = TooltipPosition.Top,
    };

    // General/Visibility (interactive): toggle each series on/off.
    internal static FrameworkElement GeneralVisibilityInteractive()
    {
        var s1 = new ColumnSeries<double>(2, 5, 4, 3) { Name = "Series 1" };
        var s2 = new ColumnSeries<double>(1, 2, 3, 4) { Name = "Series 2" };
        var s3 = new ColumnSeries<double>(4, 3, 2, 1) { Name = "Series 3" };
        var chart = new CartesianChart { Series = [s1, s2, s3], LegendPosition = LegendPosition.Bottom };
        return WithActions(chart,
            ("Toggle 1", () => s1.IsVisible = !s1.IsVisible),
            ("Toggle 2", () => s2.IsVisible = !s2.IsVisible),
            ("Toggle 3", () => s3.IsVisible = !s3.IsVisible));
    }

    // General/VisualElements: standalone label, geometry and section visuals overlaid on a chart.
    internal static FrameworkElement GeneralVisualElements() => new CartesianChart
    {
        Series = [new ColumnSeries<double>(2, 5, 4, 6, 3, 5)],
        VisualElements =
        [
            new LabelVisual
            {
                Text = "Visual elements",
                TextSize = 18,
                X = 0,
                Y = 7,
                LocationUnit = MeasureUnit.ChartValues,
                Paint = new SolidColorPaint(new Color(255, 90, 90, 90)),
            },
            new GeometryVisual<RoundedRectangleGeometry>
            {
                Width = 40,
                Height = 40,
                X = 2,
                Y = 6,
                LocationUnit = MeasureUnit.ChartValues,
                Fill = new SolidColorPaint(new Color(120, 33, 150, 243)),
            },
        ],
    };

    // General/UserDefinedTypes: plotting a custom type via a configured map, with a custom tooltip.
    internal static FrameworkElement GeneralUserDefinedTypes()
    {
        LiveChartsMewUI.EnsureInitialized();
        LiveCharts.Configure(config => config.HasMap<City>((city, index) => new(index, city.Population)));
        return new CartesianChart
        {
            Series = [new ColumnSeries<City>(new City[]
            {
                new("Tokyo", 4), new("New York", 6), new("Seoul", 2), new("Moscow", 8), new("Shanghai", 3), new("Guadalajara", 4),
            })
            {
                YToolTipLabelFormatter = point => $"{point.Model!.Population}M people in {point.Model!.Name}",
            }],
        };
    }

    // General/ConditionalDraw: each bar conditionally enters a red "Danger" visual state when value > 5.
    internal static FrameworkElement GeneralConditionalDraw() => new CartesianChart
    {
        Series = [new ConditionalColumnSeries([new(2), new(7), new(4), new(8), new(3), new(6)])],
    };

    // General/Scrollable: a main chart shows an X window; the lower "scrollbar" chart shows all the
    // data with a draggable highlight thumb that pans the main chart's window (drag the lower chart).
    internal static FrameworkElement GeneralScrollableInteractive()
    {
        var trend = 1000;
        var rnd = new Random(7);
        var values = new ObservablePoint[500];
        for (var i = 0; i < 500; i++) values[i] = new ObservablePoint(i, trend += rnd.Next(-20, 20));

        var mainX = new Axis { MinLimit = 0, MaxLimit = 100 };
        var main = new CartesianChart
        {
            Series = [new LineSeries<ObservablePoint>(values) { GeometrySize = 0 }],
            XAxes = [mainX],
            TooltipPosition = TooltipPosition.Hidden,
        };

        var thumb = new RectangularSection { Xi = 0, Xj = 100, Fill = new SolidColorPaint(new Color(60, 90, 90, 90)) };
        var scrollbar = new ScrollbarChart(mainX, thumb, 0, 499)
        {
            Series = [new LineSeries<ObservablePoint>(values) { GeometrySize = 0 }],
            Sections = [thumb],
            TooltipPosition = TooltipPosition.Hidden,
        };

        // Main chart plus a lower "scrollbar" chart (drag it to pan); returned as one main element.
        return Charts(main, scrollbar);
    }

    // General/MultiThreading: several background threads add/remove points concurrently; the chart's
    // SyncContext is the same lock object, so reads (render) and writes (threads) are serialized.
    internal static FrameworkElement GeneralMultiThreading()
    {
        var values = new ObservableCollection<int>();
        var sync = new object();
        var current = 0;
        for (var i = 0; i < 600; i++) { current += Random.Shared.Next(-9, 10); values.Add(current); }

        var chart = new CartesianChart
        {
            Series = [new LineSeries<int>(values) { GeometrySize = 0, LineSmoothness = 0 }],
            SyncContext = sync,
        };

        for (var t = 0; t < 8; t++)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                while (true)
                {
                    await Task.Delay(10);
                    lock (sync)
                    {
                        current += Random.Shared.Next(-9, 10);
                        values.Add(current);
                        values.RemoveAt(0);
                    }
                }
            });
        }

        return chart;
    }

    // General/MultiThreading2: same threaded pattern with a faster cadence and fewer points.
    internal static FrameworkElement GeneralMultiThreading2()
    {
        var values = new ObservableCollection<int>();
        var sync = new object();
        var current = 10;
        for (var i = 0; i < 250; i++) { current = Math.Clamp(current + Random.Shared.Next(-3, 4), 0, 30); values.Add(current); }

        var chart = new CartesianChart
        {
            Series = [new ColumnSeries<int>(values)],
            SyncContext = sync,
        };

        for (var t = 0; t < 4; t++)
        {
            _ = Task.Run(async () =>
            {
                await Task.Delay(1000);
                while (true)
                {
                    await Task.Delay(20);
                    lock (sync)
                    {
                        current = Math.Clamp(current + Random.Shared.Next(-3, 4), 0, 30);
                        values.Add(current);
                        values.RemoveAt(0);
                    }
                }
            });
        }

        return chart;
    }

    // General/ChartToImage: a chart (the official sample also exports it to a PNG).
    internal static FrameworkElement GeneralChartToImage() => new CartesianChart
    {
        Series = [new LineSeries<double>(2, 1, 3, 5, 3, 4, 6), new ColumnSeries<double>(5, 4, 2, 6, 3, 2, 5)],
    };

    // General/DrawOnCanvas: a chart (the official sample also draws directly on the canvas).
    internal static FrameworkElement GeneralDrawOnCanvas() => new CartesianChart
    {
        Series = [new LineSeries<double>(2, 1, 3, 5, 3, 4, 6)],
    };

    // General/TemplatedTooltips: a chart with the default tooltip (templating is backend-specific).
    internal static FrameworkElement GeneralTemplatedTooltips() => new CartesianChart
    {
        Series = [new ColumnSeries<double>(3, 7, 2, 9, 4) { Name = "Series" }],
        TooltipPosition = TooltipPosition.Top,
    };

    // General/TemplatedLegends: a chart with the default legend (templating is backend-specific).
    internal static FrameworkElement GeneralTemplatedLegends() => new CartesianChart
    {
        Series = [new LineSeries<double>(2, 1, 3) { Name = "A" }, new LineSeries<double>(4, 2, 5) { Name = "B" }],
        LegendPosition = LegendPosition.Right,
    };

    // General/TooltipHoverArea: two column series (the official sample widens the hover area).
    internal static FrameworkElement GeneralTooltipHoverArea() => new CartesianChart
    {
        Series =
        [
            new ColumnSeries<double>(37.833, 27.058, 21.516, 20.742, 15.029) { Name = "Asia" },
            new ColumnSeries<double>(12.537, 9.304, 5.383, 3.769, 3.223) { Name = "Europe" },
        ],
        TooltipPosition = TooltipPosition.Top,
    };
}
