using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Shapes;

/// <summary>
/// Renders a straight line between two points.
/// </summary>
public class Line : Shape
{
    private PathGeometry? _cachedGeometry;
    private double _cachedX1, _cachedY1, _cachedX2, _cachedY2;

    /// <summary>Gets or sets the start point X coordinate.</summary>
    public double X1
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    }

    /// <summary>Gets or sets the start point Y coordinate.</summary>
    public double Y1
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    }

    /// <summary>Gets or sets the end point X coordinate.</summary>
    public double X2
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    }

    /// <summary>Gets or sets the end point Y coordinate.</summary>
    public double Y2
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    }

    /// <inheritdoc/>
    protected override PathGeometry? GetDefiningGeometry()
    {
        if (X1 == _cachedX1 && Y1 == _cachedY1 && X2 == _cachedX2 && Y2 == _cachedY2 && _cachedGeometry != null)
            return _cachedGeometry;

        _cachedX1 = X1; _cachedY1 = Y1;
        _cachedX2 = X2; _cachedY2 = Y2;

        var g = new PathGeometry();
        g.MoveTo(X1, Y1);
        g.LineTo(X2, Y2);
        g.Freeze();
        _cachedGeometry = g;
        return _cachedGeometry;
    }
}
