using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>A point-marker geometry drawn as a cross (<see cref="SVGPoints.Cross"/>) scaled to fit the geometry bounds.</summary>
public class CrossGeometry : BaseSvgPathGeometry
{
    /// <summary>Initializes the geometry with the cross path.</summary>
    public CrossGeometry() : base(SVGPoints.Cross) { }
}
