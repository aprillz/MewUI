using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;

namespace Aprillz.MewUI.MewCharts;

/// <inheritdoc cref="ICartesianAxis" />
public class Axis : CoreAxis<LabelGeometry, LineGeometry>
{
    static Axis() => LiveChartsMewUI.EnsureInitialized();
}
