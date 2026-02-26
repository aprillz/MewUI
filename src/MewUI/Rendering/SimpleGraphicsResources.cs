using System.Numerics;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Backend-agnostic solid-color brush.
/// Used as the DIM fallback when a backend does not provide its own brush implementation.
/// The context's existing <see cref="IGraphicsContext"/> Color-based overloads handle the actual drawing.
/// </summary>
internal sealed class SimpleSolidColorBrush : ISolidColorBrush
{
    public Color Color { get; }
    public SimpleSolidColorBrush(Color color) => Color = color;
    public void Dispose() { }
}

/// <summary>
/// Backend-agnostic pen.
/// Used as the DIM fallback when a backend does not provide its own pen implementation.
/// </summary>
internal sealed class SimplePen : IPen
{
    private readonly bool _ownsBrush;
    private bool _disposed;

    public IBrush Brush { get; }
    public double Thickness { get; }
    public StrokeStyle StrokeStyle { get; }

    public SimplePen(Color color, double thickness, StrokeStyle strokeStyle)
    {
        Brush = new SimpleSolidColorBrush(color);
        Thickness = thickness;
        StrokeStyle = strokeStyle;
        _ownsBrush = true;
    }

    public SimplePen(IBrush brush, double thickness, StrokeStyle strokeStyle)
    {
        Brush = brush;
        Thickness = thickness;
        StrokeStyle = strokeStyle;
        _ownsBrush = false;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_ownsBrush) Brush.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Backend-agnostic linear gradient brush used as a DIM fallback.
/// Backends that support gradient rendering should override the factory method.
/// </summary>
internal sealed class SimpleLinearGradientBrush : ILinearGradientBrush
{
    public Point StartPoint { get; }
    public Point EndPoint { get; }
    public IReadOnlyList<GradientStop> Stops { get; }
    public SpreadMethod SpreadMethod { get; }
    public GradientUnits GradientUnits { get; }
    public Matrix3x2? GradientTransform { get; }

    public SimpleLinearGradientBrush(
        Point startPoint, Point endPoint,
        IReadOnlyList<GradientStop> stops,
        SpreadMethod spreadMethod,
        GradientUnits units,
        Matrix3x2? gradientTransform)
    {
        StartPoint = startPoint;
        EndPoint = endPoint;
        Stops = stops;
        SpreadMethod = spreadMethod;
        GradientUnits = units;
        GradientTransform = gradientTransform;
    }

    public void Dispose() { }
}

/// <summary>
/// Backend-agnostic radial gradient brush used as a DIM fallback.
/// Backends that support gradient rendering should override the factory method.
/// </summary>
internal sealed class SimpleRadialGradientBrush : IRadialGradientBrush
{
    public Point Center { get; }
    public Point GradientOrigin { get; }
    public double RadiusX { get; }
    public double RadiusY { get; }
    public IReadOnlyList<GradientStop> Stops { get; }
    public SpreadMethod SpreadMethod { get; }
    public GradientUnits GradientUnits { get; }
    public Matrix3x2? GradientTransform { get; }

    public SimpleRadialGradientBrush(
        Point center, Point gradientOrigin,
        double radiusX, double radiusY,
        IReadOnlyList<GradientStop> stops,
        SpreadMethod spreadMethod,
        GradientUnits units,
        Matrix3x2? gradientTransform)
    {
        Center = center;
        GradientOrigin = gradientOrigin;
        RadiusX = radiusX;
        RadiusY = radiusY;
        Stops = stops;
        SpreadMethod = spreadMethod;
        GradientUnits = units;
        GradientTransform = gradientTransform;
    }

    public void Dispose() { }
}
