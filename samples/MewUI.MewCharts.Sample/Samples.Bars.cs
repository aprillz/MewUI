using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Drawing;
using Aprillz.MewUI.MewCharts.Drawing.Geometries;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel;
using LiveChartsCore.Measure;
using LiveChartsCore.Themes;

// Bars/AutoUpdate model: a named set of observable values, one column series per entry.
sealed class BarsAutoUpdateData(string name, ObservableValue[] points)
{
    public string Name { get; } = name;
    public ObservableCollection<ObservableValue> Values { get; } = new(points);
}

// Bars/Race model: an observable value that also carries the pilot name and bar color.
sealed class PilotInfo : ObservableValue
{
    public PilotInfo(string name, int value, SolidColorPaint paint)
    {
        Name = name;
        Paint = paint;
        Value = value;
    }

    public string Name { get; set; }
    public SolidColorPaint Paint { get; set; }
}

// Bars/Custom geometry (upstream MyGeometry): hatches the bar with a horizontal line every 5 pixels.
sealed class BarsHatchGeometry : BoundedDrawnGeometry, IDrawnElement<MewDrawingContext>
{
    public void Draw(MewDrawingContext context)
    {
        var lineY = Y;
        while (lineY < Y + Height)
        {
            context.DrawLine(new Point(X, lineY), new Point(X + Width, lineY));
            lineY += 5;
        }
    }
}

internal static partial class Samples
{
    // Bars/Basic
    internal static FrameworkElement BarsBasic() => new CartesianChart
    {
        Series =
        [
            new ColumnSeries<double>(2, 5, 4) { Name = "Mary" },
            new ColumnSeries<double>(3, 1, 6) { Name = "Ana" },
        ],
        // Force the step to the minimum to show all the labels always.
        XAxes =
        [
            new Axis
            {
                Labels = ["Category 1", "Category 2", "Category 3"],
                LabelsRotation = 0,
                SeparatorsPaint = new SolidColorPaint(new Color(200, 200, 200)),
                SeparatorsAtCenter = false,
                TicksPaint = new SolidColorPaint(new Color(35, 35, 35)),
                TicksAtCenter = true,
                MinStep = 1,
                ForceStepToMin = true,
            },
        ],
        LegendPosition = LegendPosition.Right,
    };

    // Bars/Spacing
    internal static FrameworkElement BarsSpacing() => new CartesianChart
    {
        Series = [new ColumnSeries<double>(20, 50, 40, 20, 40, 30, 50, 20, 50, 40) { Padding = 0, MaxBarWidth = 99999 }],
    };

    // Bars/WithBackground
    internal static FrameworkElement BarsWithBackground() => new CartesianChart
    {
        Series =
        [
            new ColumnSeries<double>(20, 50, 40, 20, 40, 30, 50, 20, 50, 40)
            {
                IsHoverable = false,
                Fill = new SolidColorPaint(new Color(50, 180, 180, 180)),
                IgnoresBarPosition = true,
            },
            new ColumnSeries<double>(3, 10, 5, 3, 7, 3, 8) { IgnoresBarPosition = true },
        ],
        YAxes = [new Axis { MinLimit = 0, MaxLimit = 10 }],
    };

    // Bars/Layered
    internal static FrameworkElement BarsLayered() => new CartesianChart
    {
        Series =
        [
            new ColumnSeries<int>(6, 3, 5, 7, 3, 4, 6, 3) { MaxBarWidth = 999999, IgnoresBarPosition = true },
            new ColumnSeries<int>(2, 4, 8, 9, 5, 2, 4, 7) { MaxBarWidth = 30, IgnoresBarPosition = true },
        ],
    };

    // Bars/RowsWithLabels: the theme paints End/Start labels dark and Middle labels light.
    internal static FrameworkElement BarsRows()
    {
        return new CartesianChart
        {
            Series =
            [
                new RowSeries<int>(8, -3, 4) { ShowDataLabels = true, DataLabelsSize = 14, DataLabelsPosition = DataLabelsPosition.End },
                new RowSeries<int>(4, -6, 5) { ShowDataLabels = true, DataLabelsSize = 14, DataLabelsPosition = DataLabelsPosition.Middle },
                new RowSeries<int>(6, -9, 3) { ShowDataLabels = true, DataLabelsSize = 14, DataLabelsPosition = DataLabelsPosition.Start },
            ],
        };
    }

    // Bars/Custom: default, diamond, SVG star and hatch geometries.
    internal static FrameworkElement BarsCustom() => new CartesianChart
    {
        Series =
        [
            new ColumnSeries<double>(2, 1, 4),
            new ColumnSeries<double, DiamondGeometry>(4, 3, 6),
            new ColumnSeries<double, SvgGeometry>(-2, 2, 1) { GeometrySvg = SVGPoints.Star },
            new ColumnSeries<double, BarsHatchGeometry>(1, 2, 3),
        ],
    };

    // Bars/DelayedAnimation: each bar's transition is delayed by its index.
    internal static FrameworkElement BarsDelayedAnimation()
    {
        static float[] FetchValues(float offset)
        {
            var values = new List<float>();
            var easing = EasingFunctions.BounceInOut;
            var position = 0f;
            while (position <= 1)
            {
                values.Add(easing(position + offset));
                position += 0.025f;
            }

            return [.. values];
        }

        var values1 = FetchValues(0);
        var values2 = FetchValues(-0.15f);
        var elasticOut = EasingFunctions.BuildCustomElasticOut(1.5f, 0.60f);

        float DelayedEase(float progress, float delay)
        {
            if (progress <= delay) return 0f;

            var remapped = (progress - delay) / (1f - delay);
            return elasticOut(Math.Clamp(remapped, 0f, 1f));
        }

        void OnPointMeasured(ChartPoint point)
        {
            var index = point.Context.Entity.MetaData!.EntityIndex;
            var delay = index / (float)values1.Length;
            var animation = new Animation(progress => DelayedEase(progress, delay), TimeSpan.FromSeconds(3));
            point.Context.Visual?.SetTransition(animation);
        }

        var series1 = new ColumnSeries<float>(values1) { Padding = 2 };
        var series2 = new ColumnSeries<float>(values2) { Padding = 0 };
        series1.PointMeasured += OnPointMeasured;
        series2.PointMeasured += OnPointMeasured;

        return new CartesianChart { Series = [series1, series2] };
    }

    // Bars/AutoUpdate: item buttons change the first series, series buttons add or remove series.
    internal static FrameworkElement BarsAutoUpdate()
    {
        var data = new List<BarsAutoUpdateData>
        {
            new("Juana", [new(2), new(5), new(4)]),
            new("La cubana", [new(5), new(4), new(1)]),
        };
        var series = new ObservableCollection<ISeries>();
        foreach (var entry in data)
            series.Add(new ColumnSeries<ObservableValue>(entry.Values) { Name = entry.Name });

        ObservableValue[] FetchValues() => [new(Random.Next(0, 10)), new(Random.Next(0, 10)), new(Random.Next(0, 10))];

        void AddSeries()
        {
            var entry = new BarsAutoUpdateData($"User #{data.Count}", FetchValues());
            data.Add(entry);
            series.Add(new ColumnSeries<ObservableValue>(entry.Values) { Name = entry.Name });
        }

        void RemoveSeries()
        {
            if (data.Count == 1) return;

            data.RemoveAt(data.Count - 1);
            series.RemoveAt(series.Count - 1);
        }

        void AddItem() => data[0].Values.Add(new ObservableValue { Value = Random.Next(0, 10) });

        void RemoveItem()
        {
            if (data[0].Values.Count < 2) return;

            data[0].Values.RemoveAt(0);
        }

        void ReplaceItem()
        {
            var randomIndex = Random.Next(0, data[0].Values.Count - 1);
            data[0].Values[randomIndex] = new(Random.Next(1, 10));
        }

        return WithActions(
            new CartesianChart { Series = series },
            ("Add item", AddItem),
            ("Replace item", ReplaceItem),
            ("Remove item", RemoveItem),
            ("Add Series", AddSeries),
            ("Remove Series", RemoveSeries));
    }

    // Bars/Race: random increments every 100 ms after a 1 s delay, re-sorted by value on every step.
    internal static FrameworkElement BarsRace()
    {
        var paints = Enumerable.Range(0, 7)
            .Select(index => new SolidColorPaint(MewPaint.ToMewColor(ColorPalletes.MaterialDesign500[index])))
            .ToArray();

        PilotInfo[] data =
        [
            new("Tsunoda", 500, paints[0]),
            new("Sainz", 450, paints[1]),
            new("Riccardo", 520, paints[2]),
            new("Bottas", 550, paints[3]),
            new("Perez", 660, paints[4]),
            new("Verstapen", 920, paints[5]),
            new("Hamilton", 1000, paints[6]),
        ];

        var series = new RowSeries<PilotInfo>(data)
        {
            DataLabelsFormatter = point => point.Model is null ? string.Empty : point.Model.Name,
            ShowDataLabels = true,
            DataLabelsPosition = DataLabelsPosition.End,
            DataLabelsTranslate = new LvcPoint(-1, 0),
            MaxBarWidth = 50,
            Padding = 10,
        };
        series.PointMeasured += point =>
        {
            var pilot = point.Model;
            if (point.Visual is null || pilot is null) return;

            point.Visual.Fill = pilot.Paint;
        };

        var chart = new CartesianChart
        {
            Series = [series],
            TooltipPosition = TooltipPosition.Hidden,
            XAxes = [new Axis { SeparatorsPaint = new SolidColorPaint(new Color(220, 220, 220)) }],
            YAxes = [new Axis { IsVisible = false }],
        };

        var raceTimer = new DispatcherTimer().IntervalMs(100).OnTick(() =>
        {
            foreach (var item in data)
                item.Value += Random.Next(0, 100);

            data = [.. data.OrderBy(item => item.Value)];
            series.Values = data;
        });
        var startTimer = new DispatcherTimer().IntervalMs(1000);
        startTimer.OnTick(() =>
        {
            startTimer.Stop();
            raceTimer.Start();
        });
        startTimer.Start();
        Timers.Add(startTimer);
        Timers.Add(raceTimer);
        return chart;
    }
}
