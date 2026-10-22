using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Sketches;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A tooltip that shows nothing; assign it to <see cref="ChartViewBase.Tooltip"/> to suppress tooltips.</summary>
public sealed class MewTooltip : IChartTooltip
{
    /// <summary>Does nothing.</summary>
    public void Show(IEnumerable<ChartPoint> foundPoints, Chart chart) { }

    /// <summary>Does nothing.</summary>
    public void Hide(Chart chart) { }
}

/// <summary>A legend that draws nothing and takes no space; assign it to <see cref="ChartViewBase.Legend"/> to suppress the legend.</summary>
public sealed class MewLegend : IChartLegend
{
    /// <summary>Does nothing.</summary>
    public void Draw(Chart chart) { }

    /// <summary>Returns an empty size.</summary>
    public LvcSize Measure(Chart chart) => new(0, 0);

    /// <summary>Does nothing.</summary>
    public void Hide(Chart chart) { }
}
