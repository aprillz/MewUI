using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Renders a straight line between two points.
/// </summary>
public class Line : Shape
{
    private PathGeometry? _cachedGeometry;
    private double _cachedX1, _cachedY1, _cachedX2, _cachedY2;

    public static readonly MewProperty<double> X1Property =
        MewProperty<double>.Register<Line>(nameof(X1), 0.0, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<double> Y1Property =
        MewProperty<double>.Register<Line>(nameof(Y1), 0.0, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<double> X2Property =
        MewProperty<double>.Register<Line>(nameof(X2), 0.0, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<double> Y2Property =
        MewProperty<double>.Register<Line>(nameof(Y2), 0.0, MewPropertyOptions.AffectsLayout);

    /// <summary>Gets or sets the start point X coordinate.</summary>
    public double X1
    {
        get => GetValue(X1Property);
        set => SetValue(X1Property, value);
    }

    /// <summary>Gets or sets the start point Y coordinate.</summary>
    public double Y1
    {
        get => GetValue(Y1Property);
        set => SetValue(Y1Property, value);
    }

    /// <summary>Gets or sets the end point X coordinate.</summary>
    public double X2
    {
        get => GetValue(X2Property);
        set => SetValue(X2Property, value);
    }

    /// <summary>Gets or sets the end point Y coordinate.</summary>
    public double Y2
    {
        get => GetValue(Y2Property);
        set => SetValue(Y2Property, value);
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
