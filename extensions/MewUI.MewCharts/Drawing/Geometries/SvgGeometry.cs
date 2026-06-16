using Aprillz.MewUI.Rendering;

using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>
/// A geometry whose shape is defined by an SVG path string, scaled to fit the geometry bounds.
/// Used as a custom point marker (set the series' <c>GeometrySvg</c> property).
/// </summary>
public class SvgGeometry : BoundedDrawnGeometry, IDrawnElement<MewDrawingContext>, IVariableSvgPath
{
    private PathGeometry? _path;
    private Rect _bounds;

    /// <summary>When true, fits both axes independently (may distort); otherwise preserves aspect.</summary>
    public bool FitToSize { get; set; }

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
