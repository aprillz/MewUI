using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;

namespace Aprillz.MewUI.MewCharts;

/// <summary>A polar axis rendered with the MewUI backend.</summary>
public class PolarAxis : CorePolarAxis<LabelGeometry, LineGeometry, CircleGeometry>
{
    static PolarAxis() => LiveChartsMewUI.EnsureInitialized();
}
