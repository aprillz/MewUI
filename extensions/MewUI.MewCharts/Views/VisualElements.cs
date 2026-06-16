using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.VisualElements;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A rectangular section (axis-aligned highlight band) rendered with the MewUI backend.</summary>
public class RectangularSection : CoreSection<RoundedRectangleGeometry, LabelGeometry>
{
}

/// <summary>A frame drawn around the chart's draw margin, rendered with the MewUI backend.</summary>
public class DrawMarginFrame : CoreDrawMarginFrame<RoundedRectangleGeometry>
{
}

/// <summary>A standalone text visual (chart titles, annotations) rendered with the MewUI backend.</summary>
public class LabelVisual : BaseLabelVisual<LabelGeometry>
{
}

/// <summary>A standalone geometry visual rendered with the MewUI backend.</summary>
/// <typeparam name="TGeometry">The geometry type to draw.</typeparam>
public class GeometryVisual<TGeometry> : GeometryVisual<TGeometry, LabelGeometry>
    where TGeometry : BoundedDrawnGeometry, new()
{
}

/// <summary>An angular-gauge needle visual rendered with the MewUI backend.</summary>
/// <typeparam name="TNeedle">The needle geometry type.</typeparam>
public class NeedleVisual<TNeedle> : BaseNeedleVisual<TNeedle, LabelGeometry>
    where TNeedle : BaseNeedleGeometry, new()
{
}

/// <summary>An angular-gauge needle visual using the default <see cref="NeedleGeometry"/>.</summary>
public class NeedleVisual : NeedleVisual<NeedleGeometry>
{
}

/// <summary>An angular-gauge tick ring (arc, ticks and labels) rendered with the MewUI backend.</summary>
public class AngularTicksVisual : BaseAngularTicksVisual<ArcGeometry, LineGeometry, LabelGeometry>
{
}
