using Aprillz.MewUI.MewCharts.Drawing;
using Aprillz.MewUI.MewCharts.Drawing.Geometries;
using Aprillz.MewUI.MewCharts.Drawing.Layouts;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Painting.ImageFilters;

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
/// The default MewUI tooltip: a pop-up panel listing the hovered points' series and values, with a
/// wedge pointing at the points.
/// </summary>
public class MewDefaultTooltip : Container<PopUpGeometry>, IChartTooltip
{
    private const int PADDING_Y = 4;
    private const int PADDING_X = 8;

    private bool _isInitialized;
    private object? _themeId;
    private DrawnTask? _drawnTask;

    /// <summary>Gets or sets the easing of the show, hide and move animations; the default is <see cref="EasingFunctions.EaseOut"/>.</summary>
    public Func<float, float> Easing { get; set; } = EasingFunctions.EaseOut;

    /// <summary>Gets or sets the duration of the show, hide and move animations; the default is 150 ms.</summary>
    public TimeSpan AnimationsSpeed { get; set; } = TimeSpan.FromMilliseconds(150);

    /// <summary>Gets or sets the wedge length in DIPs; the default is 10.</summary>
    public int Wedge { get; set; } = 10;

    /// <summary>Builds the tooltip content for the points, positions it next to them and adds it to the canvas.</summary>
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
            _drawnTask = chart.Canvas.AddGeometry(CanvasZone.Overlay, this);
            _drawnTask.ZIndex = 10100;
        }

        Opacity = 1;
        ScaleTransform = new LvcPoint(1, 1);

        var layout = GetLayout(foundPoints, chart);

        var placement = chart.AutoToolTipsInfo.ToolTipPlacement;
        AlignWedge(placement, layout);

        Content = (IDrawnElement<MewDrawingContext>)layout;

        var size = Measure();
        var location = foundPoints.GetTooltipLocation(size, chart);

        // GetTooltipLocation moves the tooltip back inside the chart by flipping its placement,
        // and the wedge must then point from the new side.
        if (placement != chart.AutoToolTipsInfo.ToolTipPlacement)
        {
            AlignWedge(chart.AutoToolTipsInfo.ToolTipPlacement, layout);
            size = Measure();
            location = foundPoints.GetTooltipLocation(size, chart);
        }

        X = location.X;
        Y = location.Y;

        chart.Canvas.Invalidate();
    }

    /// <summary>Fades the tooltip out and invalidates the canvas; a null chart is ignored.</summary>
    public virtual void Hide(Chart chart)
    {
        if (chart is null) return;
        Opacity = 0f;
        ScaleTransform = new LvcPoint(0.85f, 0.85f);
        chart.Canvas.Invalidate();
    }

    /// <summary>Builds the layout listing each point's series miniature, name and value.</summary>
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

    /// <summary>Applies the background paint from the view or the theme, the wedge and the animations.</summary>
    protected virtual void Initialize(Chart chart)
    {
        var theme = chart.GetTheme();

        var backgroundPaint =
            chart.View.TooltipBackgroundPaint ??
            theme.TooltipBackgroundPaint ??
            new SolidColorPaint(new Color(230, 235, 235, 235))
            {
                ImageFilter = new DropShadow(2, 2, 6, 6, new Color(100, 50, 0, 0)),
            };

        Geometry.Fill = backgroundPaint;
        Geometry.Wedge = Wedge;
        Geometry.WedgeThickness = 3;

        this.Animate(
            new LiveChartsCore.Drawing.Animation(Easing, AnimationsSpeed),
            OpacityProperty,
            ScaleTransformProperty,
            XProperty,
            YProperty);
    }

    private void AlignWedge(PopUpPlacement placement, Layout<MewDrawingContext> layout)
    {
        Geometry.Placement = placement;

        switch (placement)
        {
            case PopUpPlacement.Top:
                layout.Padding = new Padding(PADDING_X, PADDING_Y, PADDING_X, PADDING_Y + Wedge);
                break;
            case PopUpPlacement.Bottom:
                layout.Padding = new Padding(PADDING_X, PADDING_Y + Wedge, PADDING_X, PADDING_Y);
                break;
            case PopUpPlacement.Left:
                layout.Padding = new Padding(PADDING_X, PADDING_Y, PADDING_X + Wedge, PADDING_Y);
                break;
            case PopUpPlacement.Right:
                layout.Padding = new Padding(PADDING_X + Wedge, PADDING_Y, PADDING_X, PADDING_Y);
                break;
            default:
                break;
        }
    }
}
