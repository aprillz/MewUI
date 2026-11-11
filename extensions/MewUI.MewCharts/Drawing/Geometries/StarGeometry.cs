using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>A point-marker geometry drawn as a five-pointed star (<see cref="SVGPoints.Star"/>) scaled to fit the geometry bounds.</summary>
public class StarGeometry : BaseSvgPathGeometry
{
    /// <summary>Initializes the geometry with the star path.</summary>
    public StarGeometry() : base(SVGPoints.Star) { }
}
