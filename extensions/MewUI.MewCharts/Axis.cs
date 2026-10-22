using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;

namespace Aprillz.MewUI.MewCharts;

/// <summary>A Cartesian axis rendered with the MewUI backend.</summary>
public class Axis : CoreAxis<LabelGeometry, LineGeometry>
{
    static Axis() => LiveChartsMewUI.EnsureInitialized();
}
