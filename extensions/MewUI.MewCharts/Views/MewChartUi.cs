using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Sketches;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>Placeholder no-op tooltip. Replaced by a real MewUI tooltip in a later milestone.</summary>
public sealed class MewTooltip : IChartTooltip
{
    public void Show(IEnumerable<ChartPoint> foundPoints, Chart chart) { }

    public void Hide(Chart chart) { }
}

/// <summary>Placeholder no-op legend (takes no space). Replaced by a real MewUI legend later.</summary>
public sealed class MewLegend : IChartLegend
{
    public void Draw(Chart chart) { }

    public LvcSize Measure(Chart chart) => new(0, 0);

    public void Hide(Chart chart) { }
}
