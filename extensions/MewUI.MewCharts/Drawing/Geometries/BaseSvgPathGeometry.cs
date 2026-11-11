using Aprillz.MewUI.Rendering;

using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Drawing.Geometries;

/// <summary>A geometry whose shape is an SVG path scaled to fit the geometry bounds.</summary>
/// <remarks>Subclasses with a fixed shape pass their path to the constructor; <see cref="SvgGeometry"/> takes the path from the series.</remarks>
public class BaseSvgPathGeometry : BoundedDrawnGeometry, IDrawnElement<MewDrawingContext>
{
    // Marker geometries are created per data point, so parse each distinct path once.
    private static readonly Dictionary<string, (PathGeometry Path, Rect Bounds)> _parsed = new(StringComparer.Ordinal);
    private static readonly object _parsedLock = new();

    private PathGeometry? _path;
    private Rect _bounds;

    /// <summary>Gets or sets whether the path is stretched to fill the bounds on both axes; false (the default) preserves the aspect ratio.</summary>
    public bool FitToSize { get; set; }

    /// <summary>Initializes a geometry with no shape.</summary>
    public BaseSvgPathGeometry() { }

    /// <summary>Initializes a geometry with the shape of the given SVG path data.</summary>
    public BaseSvgPathGeometry(string? svgPath) => SetPath(svgPath);

    /// <summary>Replaces the shape with the given SVG path data; null or empty draws nothing.</summary>
    protected void SetPath(string? svgPath)
    {
        if (string.IsNullOrEmpty(svgPath))
        {
            _path = null;
            return;
        }

        lock (_parsedLock)
        {
            if (!_parsed.TryGetValue(svgPath, out var entry))
            {
                var path = SvgPathData.Parse(svgPath);
                entry = (path, path.GetBounds());
                _parsed[svgPath] = entry;
            }
            (_path, _bounds) = entry;
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
