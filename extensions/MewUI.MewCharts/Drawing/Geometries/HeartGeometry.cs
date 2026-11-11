using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>A point-marker geometry drawn as a heart (<see cref="SVGPoints.Heart"/>) scaled to fit the geometry bounds.</summary>
public class HeartGeometry : BaseSvgPathGeometry
{
    /// <summary>Initializes the geometry with the heart path.</summary>
    public HeartGeometry() : base(SVGPoints.Heart) { }
}
