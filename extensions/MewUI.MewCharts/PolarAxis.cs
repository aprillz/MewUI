using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;

namespace Aprillz.MewUI.MewCharts;

/// <inheritdoc cref="IPolarAxis" />
public class PolarAxis : CorePolarAxis<LabelGeometry, LineGeometry, CircleGeometry>
{
    static PolarAxis() => LiveChartsMewUI.EnsureInitialized();
}
