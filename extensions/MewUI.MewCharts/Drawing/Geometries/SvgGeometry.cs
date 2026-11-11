using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>A point-marker geometry whose shape is an SVG path scaled to fit the geometry bounds.</summary>
/// <remarks>Series select it through <see cref="LiveChartsCore.ISeries.GeometrySvg"/>.</remarks>
public class SvgGeometry : BaseSvgPathGeometry, IVariableSvgPath
{
    /// <summary>Gets or sets the SVG path data that defines the shape; null or empty draws nothing.</summary>
    public string? SVGPath
    {
        get;
        set
        {
            if (value == field) return;
            field = value;
            SetPath(value);
        }
    }
}
