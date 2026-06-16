using Aprillz.MewUI.MewCharts.Painting;

using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using LiveChartsCore.Themes;
using LiveChartsCore.VisualElements;
using LiveChartsCore.VisualStates;

namespace Aprillz.MewUI.MewCharts;

/// <summary>
/// Entry point that configures LiveCharts to use the MewUI backend. The MewUI analog of
/// <c>LiveChartsSkiaSharp</c>. Called from chart control static constructors.
/// </summary>
public static class LiveChartsMewUI
{
    private static bool _initialized;
    private static readonly object _lock = new();

    /// <summary>Configures LiveCharts with the MewUI provider, default theme and mappers (idempotent).</summary>
    public static LiveChartsSettings EnsureInitialized()
    {
        if (_initialized) return LiveCharts.DefaultSettings;
        lock (_lock)
        {
            if (_initialized) return LiveCharts.DefaultSettings;
            _initialized = true;
            LiveCharts.Configure(settings => settings.UseMewDefaults());
        }
        return LiveCharts.DefaultSettings;
    }

    /// <summary>Applies the MewUI backend defaults: provider, theme and mappers.</summary>
    public static LiveChartsSettings UseMewDefaults(this LiveChartsSettings settings)
    {
        if (!LiveCharts.DefaultSettings.HasBackedDefined) _ = settings.HasProvider(new MewChartEngine());
        if (!LiveCharts.DefaultSettings.HasThemeDefined) _ = settings.AddMewTheme();
        if (!LiveCharts.DefaultSettings.HasMappersDefined) _ = settings.AddDefaultMappers();
        return settings;
    }

    /// <summary>Registers a minimal default theme (colors, animation, axis and line-series rules).</summary>
    public static LiveChartsSettings AddMewTheme(this LiveChartsSettings settings) =>
        settings.HasTheme(theme =>
        {
            _ = theme
                .OnInitialized(() =>
                {
                    theme.AnimationsSpeed = TimeSpan.FromMilliseconds(800);
                    theme.EasingFunction = EasingFunctions.ExponentialOut;
                    theme.Colors = ColorPalletes.MaterialDesign500;
                    theme.VirtualBackroundColor = new LvcColor(255, 255, 255);

                    // Tooltip/legend paints: without a background paint the tooltip's container box
                    // is invisible (only the floating text shows).
                    theme.TooltipBackgroundPaint = new SolidColorPaint(new Color(250, 235, 235, 235));
                    theme.TooltipTextPaint = new SolidColorPaint(new Color(30, 30, 30));
                    theme.LegendTextPaint = new SolidColorPaint(new Color(30, 30, 30));
                })
                .HasDefaultTooltip(() => new Views.MewDefaultTooltip())
                .HasDefaultLegend(() => new Views.MewDefaultLegend())
                .HasRuleForAnySeries(series =>
                {
                    series.Name ??= LiveCharts.IgnoreSeriesName;
                    _ = series.HasState("Hover", [(nameof(DrawnGeometry.Opacity), 0.8f)]);
                })
                .HasRuleForAxes(axis =>
                {
                    axis.TextSize = 16;
                    axis.ShowSeparatorLines = true;
                    axis.NamePaint = new SolidColorPaint(new Color(35, 35, 35));
                    axis.LabelsPaint = new SolidColorPaint(new Color(70, 70, 70));

                    var lineColor = new Color(235, 235, 235);
                    if (axis is ICartesianAxis cartesian)
                    {
                        axis.SeparatorsPaint = cartesian.Orientation == AxisOrientation.X
                            ? null
                            : new SolidColorPaint(lineColor);
                        cartesian.Padding = new Padding(12);
                    }
                    else
                    {
                        axis.SeparatorsPaint = new SolidColorPaint(lineColor);
                    }
                })
                .HasRuleForLineSeries(lineSeries =>
                {
                    var color = MewPaint.ToMewColor(theme.GetSeriesColor(lineSeries));

                    lineSeries.Stroke = new SolidColorPaint(color, 4);
                    lineSeries.Fill = new SolidColorPaint(new Color(50, color.R, color.G, color.B));
                    lineSeries.GeometrySize = 12;
                    lineSeries.GeometryStroke = new SolidColorPaint(color, 4);
                    lineSeries.GeometryFill = new SolidColorPaint(new Color(250, 250, 250));
                })
                .HasRuleForBarSeries(barSeries =>
                {
                    var color = MewPaint.ToMewColor(theme.GetSeriesColor(barSeries));
                    barSeries.Stroke = null;
                    barSeries.Fill = new SolidColorPaint(color);
                    barSeries.Rx = 3;
                    barSeries.Ry = 3;
                })
                .HasRuleForScatterSeries(scatterSeries =>
                {
                    var color = MewPaint.ToMewColor(theme.GetSeriesColor(scatterSeries));
                    scatterSeries.Fill = new SolidColorPaint(color);
                    scatterSeries.GeometrySize = 24;
                })
                .HasRuleForPieSeries(pieSeries =>
                {
                    var color = MewPaint.ToMewColor(theme.GetSeriesColor(pieSeries));
                    pieSeries.Stroke = null;
                    pieSeries.Fill = new SolidColorPaint(color);
                })
                .HasRuleForGaugeSeries(gaugeSeries =>
                {
                    // Gauge series are routed to this rule (not the pie rule); without a fill the
                    // gauge slice geometry is never added to a paint task and stays invisible.
                    var color = MewPaint.ToMewColor(theme.GetSeriesColor(gaugeSeries));
                    gaugeSeries.Stroke = null;
                    gaugeSeries.Fill = new SolidColorPaint(color);
                    gaugeSeries.DataLabelsPosition = PolarLabelsPosition.ChartCenter;
                    gaugeSeries.DataLabelsPaint = new SolidColorPaint(new Color(255, 70, 70, 70));
                    gaugeSeries.CornerRadius = 8;
                })
                .HasRuleForGaugeFillSeries(gaugeFill =>
                {
                    gaugeFill.Fill = new SolidColorPaint(new Color(10, 30, 30, 30));
                })
                .HasRuleFor<BaseLabelVisual>(label =>
                {
                    label.Paint = new SolidColorPaint(new Color(255, 30, 30, 30));
                })
                .HasRuleFor<BaseNeedleVisual>(needle =>
                {
                    needle.Width = 20;
                    needle.Fill = new SolidColorPaint(new Color(255, 30, 30, 30));
                })
                .HasRuleFor<BaseAngularTicksVisual>(ticks =>
                {
                    ticks.Stroke = new SolidColorPaint(new Color(255, 30, 30, 30));
                    ticks.LabelsPaint = new SolidColorPaint(new Color(255, 30, 30, 30));
                })
                .HasRuleForPolarLineSeries(polarLine =>
                {
                    var color = MewPaint.ToMewColor(theme.GetSeriesColor(polarLine));
                    polarLine.Stroke = new SolidColorPaint(color, 4);
                    polarLine.Fill = new SolidColorPaint(new Color(50, color.R, color.G, color.B));
                    polarLine.GeometrySize = 12;
                    polarLine.GeometryStroke = new SolidColorPaint(color, 4);
                    polarLine.GeometryFill = new SolidColorPaint(new Color(250, 250, 250));
                })
                .HasRuleForStepLineSeries(stepLine =>
                {
                    var color = MewPaint.ToMewColor(theme.GetSeriesColor(stepLine));
                    stepLine.Stroke = new SolidColorPaint(color, 4);
                    stepLine.Fill = new SolidColorPaint(new Color(50, color.R, color.G, color.B));
                    stepLine.GeometrySize = 12;
                    stepLine.GeometryStroke = new SolidColorPaint(color, 4);
                    stepLine.GeometryFill = new SolidColorPaint(new Color(250, 250, 250));
                })
                .HasRuleForStackedLineSeries(stackedArea =>
                {
                    var color = MewPaint.ToMewColor(theme.GetSeriesColor(stackedArea));
                    stackedArea.GeometrySize = 0;
                    stackedArea.GeometryStroke = null;
                    stackedArea.GeometryFill = null;
                    stackedArea.Stroke = null;
                    stackedArea.Fill = new SolidColorPaint(color);
                })
                .HasRuleForStackedColumnSeries(stackedColumn =>
                {
                    var color = MewPaint.ToMewColor(theme.GetSeriesColor(stackedColumn));
                    stackedColumn.Stroke = null;
                    stackedColumn.Fill = new SolidColorPaint(color);
                    stackedColumn.Rx = 0;
                    stackedColumn.Ry = 0;
                })
                .HasRuleForBoxSeries(boxSeries =>
                {
                    var color = MewPaint.ToMewColor(theme.GetSeriesColor(boxSeries));
                    boxSeries.MaxBarWidth = 60;
                    boxSeries.Stroke = new SolidColorPaint(new Color(30, 30, 30), 2);
                    boxSeries.Fill = new SolidColorPaint(color);
                })
                .HasRuleForFinancialSeries(financialSeries =>
                {
                    financialSeries.UpFill = new SolidColorPaint(new Color(139, 195, 74));
                    financialSeries.UpStroke = new SolidColorPaint(new Color(139, 195, 74), 3);
                    financialSeries.DownFill = new SolidColorPaint(new Color(239, 83, 80));
                    financialSeries.DownStroke = new SolidColorPaint(new Color(239, 83, 80), 3);
                });
        });
}
