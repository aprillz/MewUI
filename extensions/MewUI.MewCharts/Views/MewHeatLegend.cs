using Aprillz.MewUI.MewCharts.Drawing;
using Aprillz.MewUI.MewCharts.Drawing.Geometries;
using Aprillz.MewUI.MewCharts.Drawing.Layouts;
using Aprillz.MewUI.MewCharts.Painting;

using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Drawing.Layouts;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using LiveChartsCore.Motion;
using LiveChartsCore.Painting;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>
/// A legend for heat series: a gradient badge of the first visible heat series' heat map, labeled with the
/// minimum and maximum weights. Assign it to <see cref="ChartViewBase.Legend"/>.
/// </summary>
public class MewHeatLegend : Container, IChartLegend
{
    private bool _isInitialized;
    private DrawnTask? _drawnTask;

    /// <summary>Gets or sets the padding around the gradient badge; the default is 10 on every side.</summary>
    public Padding BadgePadding { get; set; } = new(10);

    /// <summary>Gets or sets the badge thickness in DIPs; null (the default) sizes it to the weight labels.</summary>
    public double? BadgeWidth { get; set; }

    /// <summary>Gets or sets the formatter of the minimum and maximum weight labels.</summary>
    public Func<double, string> Formatter { get; set; } = Labelers.SixRepresentativeDigits;

    /// <summary>Places the legend at the chart's legend position and adds it to the canvas, or removes it when the legend position is hidden.</summary>
    public virtual void Draw(Chart chart)
    {
        if (!_isInitialized)
        {
            Initialize(chart);
            _isInitialized = true;
        }

        if (_drawnTask is null || _drawnTask.IsEmpty)
        {
            _drawnTask = chart.Canvas.AddGeometry(CanvasZone.Overlay, this);
            _drawnTask.ZIndex = 10099;
        }

        var legendPosition = chart.GetLegendPosition();
        X = legendPosition.X;
        Y = legendPosition.Y;

        if (chart.LegendPosition == LegendPosition.Hidden && _drawnTask is not null)
        {
            chart.Canvas.RemovePaintTask(_drawnTask);
            _drawnTask = null;
        }
    }

    /// <summary>Builds the legend layout for the chart's heat series and returns its size.</summary>
    public virtual LvcSize Measure(Chart chart)
    {
        Content = (IDrawnElement<MewDrawingContext>)GetLayout(chart);
        return Measure();
    }

    /// <summary>Removes the legend from the chart canvas.</summary>
    public virtual void Hide(Chart chart)
    {
        if (_drawnTask is not null)
        {
            chart.Canvas.RemovePaintTask(_drawnTask);
            _drawnTask = null;
        }
    }

    /// <summary>Builds the badge and its weight labels, or hides the legend when the chart has no visible heat series.</summary>
    protected virtual Layout<MewDrawingContext> GetLayout(Chart chart)
    {
        var theme = chart.GetTheme();

        var textSize = (float)chart.View.LegendTextSize;
        if (textSize < 0) textSize = theme.LegendTextSize;

        var isVertical = chart.LegendPosition is LegendPosition.Left or LegendPosition.Right;

        var layout = new AbsoluteLayout { Padding = BadgePadding };

        var heatSeries = (IHeatSeries?)chart.Series.FirstOrDefault(series => series.IsVisibleAtLegend && series is IHeatSeries);
        var gradient = heatSeries?.HeatMap.Select(MewPaint.ToMewColor).ToArray();

        if (heatSeries is null || gradient is null || gradient.Length == 0)
        {
            Hide(chart);
            return layout;
        }

        var labelWidth = (float)LiveCharts.DefaultSettings.MaxTooltipsAndLegendsLabelsWidth;
        var minLabel = new LabelGeometry
        {
            Padding = new Padding(8, 4),
            Text = Formatter(heatSeries.WeightBounds.Min),
            Paint = new SolidColorPaint(GetContrastingColor(gradient[0])),
            TextSize = textSize,
            MaxWidth = labelWidth,
            VerticalAlign = Align.Start,
            HorizontalAlign = Align.Start,
        };

        var maxLabel = new LabelGeometry
        {
            Padding = new Padding(8, 4),
            Text = Formatter(heatSeries.WeightBounds.Max),
            Paint = new SolidColorPaint(GetContrastingColor(gradient[^1])),
            TextSize = textSize,
            MaxWidth = labelWidth,
            VerticalAlign = Align.Start,
            HorizontalAlign = Align.Start,
        };

        var minSize = minLabel.Measure();
        var maxSize = maxLabel.Measure();

        var thickness = (float?)BadgeWidth ?? (isVertical
            ? Math.Max(maxSize.Width, minSize.Width)
            : Math.Max(maxSize.Height, minSize.Height));

        var padding = isVertical
            ? BadgePadding.Top + BadgePadding.Bottom
            : BadgePadding.Left + BadgePadding.Right;

        layout.Children.Add(new RectangleGeometry
        {
            Width = isVertical ? thickness : chart.DrawMarginSize.Width - padding,
            Height = isVertical ? chart.DrawMarginSize.Height - padding : thickness,
            Fill = new LinearGradientPaint(
                gradient,
                isVertical ? new Point(0.5, 0) : new Point(0, 0.5),
                isVertical ? new Point(0.5, 1) : new Point(1, 0.5)),
        });

        layout.Children.Add(minLabel);
        layout.Children.Add(maxLabel);

        if (isVertical)
        {
            minLabel.X = Math.Abs(minSize.Width - thickness) * 0.5f;
            minLabel.Y = 0;
            maxLabel.X = Math.Abs(maxSize.Width - thickness) * 0.5f;
            maxLabel.Y = chart.DrawMarginSize.Height - padding - maxSize.Height;
        }
        else
        {
            minLabel.X = 0;
            minLabel.Y = Math.Abs(minSize.Height - thickness) * 0.5f;
            maxLabel.X = chart.DrawMarginSize.Width - padding - maxSize.Width;
            maxLabel.Y = Math.Abs(maxSize.Height - thickness) * 0.5f;
        }

        return new StackLayout
        {
            Padding = BadgePadding,
            Children = [layout],
        };
    }

    /// <summary>Applies the legend background paint from the view or the theme.</summary>
    protected virtual void Initialize(Chart chart)
    {
        Geometry.Fill = chart.View.LegendBackgroundPaint ?? chart.GetTheme().LegendBackgroundPaint;
    }

    private static Color GetContrastingColor(Color color)
    {
        var luma = (299 * color.R + 587 * color.G + 114 * color.B) / 1000;
        return luma > 128 ? new Color(0, 0, 0) : new Color(255, 255, 255);
    }
}
