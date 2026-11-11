using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Drawing;
using LiveChartsCore.Measure;

// Events/OverrideFind: hover matches on X only, press matches on Y only.
sealed class OverrideFindColumnSeries<TModel> : ColumnSeries<TModel>
{
    protected override IEnumerable<ChartPoint> FindPointsInPosition(
        Chart chart, LvcPoint pointerPosition, FindingStrategy strategy, FindPointFor findPointFor)
    {
        return Fetch(chart).Where(point =>
        {
            var hoverArea = (RectangleHoverArea?)point.Context.HoverArea;
            if (hoverArea is null) return false;

            var isInsideX = hoverArea.X <= pointerPosition.X && pointerPosition.X <= hoverArea.X + hoverArea.Width;
            var isInsideY = hoverArea.Y <= pointerPosition.Y && pointerPosition.Y <= hoverArea.Y + hoverArea.Height;

            return findPointFor == FindPointFor.HoverEvent
                ? isInsideX
                : isInsideY;
        });
    }
}

internal static partial class Samples
{
    // Events/OverrideFind
    internal static FrameworkElement EventsOverrideFind() => new CartesianChart
    {
        Series =
        [
            new OverrideFindColumnSeries<int> { Values = [9, 5, 7, 3, 7, 3] },
            new OverrideFindColumnSeries<int> { Values = [8, 2, 3, 2, 5, 2] },
        ],
    };

    // Events/Tutorial: pressing a bar toggles a yellow fill, hovering a bar draws a black stroke.
    internal static FrameworkElement EventsTutorialInteractive()
    {
        var activePoints = new HashSet<ChartPoint>();

        var chart = new CartesianChart
        {
            Series =
            [
                new ColumnSeries<double>(1, 5, 4, 3) { Stroke = new SolidColorPaint(new Color(0, 0, 0, 0)) },
                new ColumnSeries<double>(3, 2, 6, 2) { Stroke = new SolidColorPaint(new Color(0, 0, 0, 0)) },
            ],
            FindingStrategy = FindingStrategy.ExactMatch,
        };

        chart.MouseDown += args =>
        {
            var position = args.GetPosition(chart);
            var foundPoints = chart.GetPointsAt(new LvcPointD(position.X, position.Y));

            foreach (var point in foundPoints)
            {
                var visual = point.Context.Visual!;

                if (!activePoints.Contains(point))
                {
                    visual.Fill = new SolidColorPaint(new Color(255, 255, 0));
                    activePoints.Add(point);
                }
                else
                {
                    // Clear the fill to the default value.
                    visual.Fill = null;
                    activePoints.Remove(point);
                }
            }
        };

        chart.HoveredPointsChanged += (_, newPoints, oldPoints) =>
        {
            foreach (var hovered in newPoints ?? [])
                hovered.Context.Visual!.Stroke = new SolidColorPaint(new Color(0, 0, 0), 3);

            foreach (var hovered in oldPoints ?? [])
                hovered.Context.Visual!.Stroke = null;
        };

        return chart;
    }

    // Events/AddPointOnClick: pressing the chart adds a point at the pointer position.
    internal static FrameworkElement EventsAddPointOnClickInteractive()
    {
        var points = new ObservableCollection<ObservablePoint>
        {
            new(0, 5),
            new(3, 8),
            new(7, 9),
        };

        var chart = new CartesianChart
        {
            Series = [new LineSeries<ObservablePoint>(points) { Fill = null, DataPadding = new LvcPoint(5, 5) }],
            TooltipPosition = TooltipPosition.Hidden,
        };

        chart.MouseDown += args =>
        {
            var position = args.GetPosition(chart);
            var scaledPoint = chart.ScalePixelsToData(new LvcPointD(position.X, position.Y));
            points.Add(new ObservablePoint(scaledPoint.X, scaledPoint.Y));
        };

        return chart;
    }
}
