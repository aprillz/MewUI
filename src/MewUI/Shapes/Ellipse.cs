using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Shapes;

/// <summary>
/// Renders an ellipse that fills the element bounds.
/// </summary>
public class Ellipse : Shape
{
    private PathGeometry? _cachedGeometry;
    private Size _cachedSize;
    private double _cachedStroke;

    /// <inheritdoc/>
    protected override PathGeometry? GetDefiningGeometry()
    {
        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;

        var size = new Size(bounds.Width, bounds.Height);
        if (size == _cachedSize && _cachedStroke == StrokeThickness && _cachedGeometry != null)
            return _cachedGeometry;

        _cachedSize = size;
        _cachedStroke = StrokeThickness;

        // Deflate by half stroke so the stroke stays within the element bounds.
        double hs = (Stroke != null && StrokeThickness > 0) ? StrokeThickness * 0.5 : 0;
        double w = Math.Max(0, size.Width - StrokeThickness);
        double h = Math.Max(0, size.Height - StrokeThickness);

        _cachedGeometry = PathGeometry.FromEllipse(new Rect(hs, hs, w, h));
        return _cachedGeometry;
    }
}
