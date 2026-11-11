using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>A point-marker geometry drawn as a diamond (<see cref="SVGPoints.Diamond"/>) scaled to fit the geometry bounds.</summary>
public class DiamondGeometry : BaseSvgPathGeometry
{
    /// <summary>Initializes the geometry with the diamond path.</summary>
    public DiamondGeometry() : base(SVGPoints.Diamond) { }
}
