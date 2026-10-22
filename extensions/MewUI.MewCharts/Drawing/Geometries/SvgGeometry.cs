using Aprillz.MewUI.Rendering;

using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>A point-marker geometry whose shape is an SVG path scaled to fit the geometry bounds.</summary>
/// <remarks>Series select it through <see cref="LiveChartsCore.ISeries.GeometrySvg"/>.</remarks>
public class SvgGeometry : BoundedDrawnGeometry, IDrawnElement<MewDrawingContext>, IVariableSvgPath
{
    private PathGeometry? _path;
    private Rect _bounds;

    /// <summary>Gets or sets whether the path is stretched to fill the bounds on both axes; false (the default) preserves the aspect ratio.</summary>
    public bool FitToSize { get; set; }

    /// <summary>Gets or sets the SVG path data that defines the shape; null or empty draws nothing.</summary>
    public string? SVGPath
    {
        get;
        set
        {
            if (value == field) return;
            field = value;
            if (string.IsNullOrEmpty(value))
            {
                _path = null;
            }
            else
            {
                _path = SvgPathData.Parse(value);
                _bounds = _path.GetBounds();
            }
        }
    }

    /// <summary>Draws the path centered in the bounds and scaled to fit them with the active paint; draws nothing until a path is set.</summary>
    public virtual void Draw(MewDrawingContext context)
    {
        if (_path is null || _bounds.Width <= 0 || _bounds.Height <= 0) return;

        var strokeWidth = context.ActiveStrokeThickness;

        context.G.Save();
        context.G.Translate(X + Width / 2.0, Y + Height / 2.0);

        if (FitToSize)
        {
            context.G.Scale(Width / (_bounds.Width + strokeWidth), Height / (_bounds.Height + strokeWidth));
        }
        else
        {
            var maxDimension = _bounds.Width < _bounds.Height ? _bounds.Height : _bounds.Width;
            var scale = Width / (maxDimension + strokeWidth);
            context.G.Scale(scale, scale);
        }

        context.G.Translate(-(_bounds.X + _bounds.Width / 2.0), -(_bounds.Y + _bounds.Height / 2.0));
        context.DrawPath(_path);
        context.G.Restore();
    }
}
