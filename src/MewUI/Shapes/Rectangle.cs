using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Shapes;

/// <summary>
/// Renders a rectangle, optionally with rounded corners.
/// </summary>
public class Rectangle : Shape
{
    private PathGeometry? _cachedGeometry;
    private Size _cachedSize;
    private double _cachedRx;
    private double _cachedRy;
    private double _cachedStroke;

    /// <summary>
    /// Gets or sets the X-axis corner radius.
    /// </summary>
    public double RadiusX
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets or sets the Y-axis corner radius.
    /// </summary>
    public double RadiusY
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateVisual();
            }
        }
    }

    /// <inheritdoc/>
    protected override PathGeometry? GetDefiningGeometry()
    {
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;

        var size = new Size(bounds.Width, bounds.Height);
        if (size == _cachedSize && RadiusX == _cachedRx && RadiusY == _cachedRy
            && _cachedStroke == StrokeThickness && _cachedGeometry != null)
            return _cachedGeometry;

        _cachedSize = size;
        _cachedRx = RadiusX;
        _cachedRy = RadiusY;
        _cachedStroke = StrokeThickness;

        // Deflate by half stroke so the stroke stays within the element bounds.
        double hs = (Stroke != null && StrokeThickness > 0) ? StrokeThickness * 0.5 : 0;
        double w = Math.Max(0, size.Width - StrokeThickness);
        double h = Math.Max(0, size.Height - StrokeThickness);

        _cachedGeometry = (RadiusX > 0 || RadiusY > 0)
            ? PathGeometry.FromRoundedRect(new Rect(hs, hs, w, h), RadiusX, RadiusY)
            : PathGeometry.FromRect(new Rect(hs, hs, w, h));

        return _cachedGeometry;
    }
}
