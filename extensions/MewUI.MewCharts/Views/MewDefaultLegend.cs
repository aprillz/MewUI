using Aprillz.MewUI.MewCharts.Drawing;
using Aprillz.MewUI.MewCharts.Drawing.Geometries;
using Aprillz.MewUI.MewCharts.Drawing.Layouts;
using Aprillz.MewUI.MewCharts.Painting;

using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Drawing.Layouts;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using LiveChartsCore.Painting;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>The default MewUI chart legend: a container that lays out each series' miniature and name.</summary>
public class MewDefaultLegend : Container, IChartLegend
{
    private bool _isInitialized;
    private object? _themeId;
    private DrawnTask? _drawnTask;

    public void Draw(Chart chart)
    {
        var theme = chart.GetTheme();

        if (!_isInitialized || _themeId != theme.ThemeId || chart.View.LegendBackgroundPaint is not null)
        {
            Initialize(chart);
            _themeId = theme.ThemeId;
            _isInitialized = true;
        }

        if (_drawnTask is null || _drawnTask.IsEmpty)
        {
            _drawnTask = chart.Canvas.AddGeometry(this);
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

    public LvcSize Measure(Chart chart)
    {
        Content = (IDrawnElement<MewDrawingContext>)GetLayout(chart);
        return Measure();
    }

    public void Hide(Chart chart)
    {
        if (_drawnTask is not null)
        {
            chart.Canvas.RemovePaintTask(_drawnTask);
            _drawnTask = null;
        }
    }

    protected virtual Layout<MewDrawingContext> GetLayout(Chart chart)
    {
        var theme = chart.GetTheme();

        var textSize = (float)chart.View.LegendTextSize;
        if (textSize < 0) textSize = theme.LegendTextSize;

        var fontPaint = chart.View.LegendTextPaint ?? theme.LegendTextPaint ?? new SolidColorPaint(new Color(30, 30, 30));

        var stackLayout = new StackLayout
        {
            Padding = new Padding(15, 4),
            HorizontalAlignment = Align.Start,
            VerticalAlignment = Align.Middle,
            Orientation = chart.LegendPosition is LegendPosition.Left or LegendPosition.Right
                ? ContainerOrientation.Vertical
                : ContainerOrientation.Horizontal,
        };

        if (stackLayout.Orientation == ContainerOrientation.Horizontal)
        {
            stackLayout.MaxWidth = chart.ControlSize.Width;
            stackLayout.MaxHeight = double.MaxValue;
        }
        else
        {
            stackLayout.MaxWidth = double.MaxValue;
            stackLayout.MaxHeight = chart.ControlSize.Height;
        }

        foreach (var series in chart.Series.Where(x => x.IsVisibleAtLegend))
        {
            var miniature = (IDrawnElement<MewDrawingContext>)series.GetMiniatureGeometry(null);
            var label = new LabelGeometry
            {
                Text = series.Name ?? string.Empty,
                Paint = fontPaint,
                TextSize = textSize,
                Padding = new Padding(8, 2, 8, 2),
                MaxWidth = (float)LiveCharts.DefaultSettings.MaxTooltipsAndLegendsLabelsWidth,
                VerticalAlign = Align.Start,
                HorizontalAlign = Align.Start,
            };

            stackLayout.Children.Add(new StackLayout
            {
                Padding = new Padding(12, 6),
                VerticalAlignment = Align.Middle,
                HorizontalAlignment = Align.Middle,
                Children = { miniature, label },
            });
        }

        return stackLayout;
    }

    protected virtual void Initialize(Chart chart)
    {
        Geometry.Fill = chart.View.LegendBackgroundPaint ?? chart.GetTheme().LegendBackgroundPaint;
    }
}
