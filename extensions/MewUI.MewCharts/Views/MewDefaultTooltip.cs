using Aprillz.MewUI.MewCharts.Drawing;
using Aprillz.MewUI.MewCharts.Drawing.Geometries;
using Aprillz.MewUI.MewCharts.Drawing.Layouts;
using Aprillz.MewUI.MewCharts.Painting;

using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Drawing.Layouts;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Painting;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>
/// The default MewUI tooltip: a rounded panel listing the hovered points' series and values,
/// positioned next to the pointer. (No wedge; simplified from the SkiaSharp tooltip.)
/// </summary>
public class MewDefaultTooltip : Container<RoundedRectangleGeometry>, IChartTooltip
{
    private bool _isInitialized;
    private object? _themeId;
    private DrawnTask? _drawnTask;

    public virtual void Show(IEnumerable<ChartPoint> foundPoints, Chart chart)
    {
        var theme = chart.GetTheme();

        if (!_isInitialized || _themeId != theme.ThemeId || chart.View.TooltipBackgroundPaint is not null)
        {
            Initialize(chart);
            _isInitialized = true;
            _themeId = theme.ThemeId;
        }

        if (_drawnTask is null || _drawnTask.IsEmpty)
        {
            _drawnTask = chart.Canvas.AddGeometry(this);
            _drawnTask.ZIndex = 10100;
        }

        Opacity = 1;
        ScaleTransform = new LvcPoint(1, 1);

        Content = (IDrawnElement<MewDrawingContext>)GetLayout(foundPoints, chart);

        var size = Measure();
        var location = foundPoints.GetTooltipLocation(size, chart);
        X = location.X;
        Y = location.Y;

        chart.Canvas.Invalidate();
    }

    public virtual void Hide(Chart chart)
    {
        if (chart is null) return;
        Opacity = 0f;
        ScaleTransform = new LvcPoint(0.85f, 0.85f);
        chart.Canvas.Invalidate();
    }

    protected virtual Layout<MewDrawingContext> GetLayout(IEnumerable<ChartPoint> foundPoints, Chart chart)
    {
        var theme = chart.GetTheme();

        var textSize = (float)chart.View.TooltipTextSize;
        if (textSize < 0) textSize = theme.TooltipTextSize;

        var fontPaint = chart.View.TooltipTextPaint ?? theme.TooltipTextPaint ?? new SolidColorPaint(new Color(28, 49, 58));
        var labelWidth = (float)LiveCharts.DefaultSettings.MaxTooltipsAndLegendsLabelsWidth;

        var stackLayout = new StackLayout
        {
            Orientation = ContainerOrientation.Vertical,
            HorizontalAlignment = Align.Middle,
            VerticalAlignment = Align.Middle,
            Padding = new Padding(8, 4),
        };

        var tableLayout = new TableLayout
        {
            HorizontalAlignment = Align.Middle,
            VerticalAlignment = Align.Middle,
        };

        var row = 0;
        foreach (var point in foundPoints)
        {
            var series = point.Context.Series;

            if (row == 0)
            {
                var title = series.GetSecondaryToolTipText(point) ?? string.Empty;
                if (title != LiveCharts.IgnoreToolTipLabel)
                {
                    stackLayout.Children.Add(new LabelGeometry
                    {
                        Text = title,
                        Paint = fontPaint,
                        TextSize = textSize,
                        Padding = new Padding(0, 0, 0, 8),
                        MaxWidth = labelWidth,
                        VerticalAlign = Align.Start,
                        HorizontalAlign = Align.Start,
                    });
                }
            }

            var content = series.GetPrimaryToolTipText(point) ?? string.Empty;
            if (content == LiveCharts.IgnoreToolTipLabel) continue;

            var miniature = (IDrawnElement<MewDrawingContext>)series.GetMiniatureGeometry(point);
            _ = tableLayout.AddChild(miniature, row, 0);

            if (series.Name != LiveCharts.IgnoreSeriesName)
            {
                _ = tableLayout.AddChild(new LabelGeometry
                {
                    Text = series.Name ?? string.Empty,
                    Paint = fontPaint,
                    TextSize = textSize,
                    Padding = new Padding(10, 0),
                    MaxWidth = labelWidth,
                    VerticalAlign = Align.Start,
                    HorizontalAlign = Align.Start,
                }, row, 1, horizontalAlign: Align.Start);
            }

            _ = tableLayout.AddChild(new LabelGeometry
            {
                Text = content,
                Paint = fontPaint,
                TextSize = textSize,
                Padding = new Padding(8, 2),
                MaxWidth = labelWidth,
                VerticalAlign = Align.Start,
                HorizontalAlign = Align.Start,
            }, row, 2, horizontalAlign: Align.End);

            row++;
        }

        stackLayout.Children.Add(tableLayout);
        return stackLayout;
    }

    protected virtual void Initialize(Chart chart)
    {
        Geometry.BorderRadius = new LvcPoint(6, 6);
        Geometry.Fill = chart.View.TooltipBackgroundPaint ?? chart.GetTheme().TooltipBackgroundPaint;
    }
}
