using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Geometry;

/// <summary>Enables geometry features that integrate with MewUI elements.</summary>
public static class GeometryServices
{
    private static readonly object _syncRoot = new();
    private static readonly IShapeHitTestProvider _shapeHitTestProvider = new GeometryShapeHitTestProvider();
    private static IDisposable? _shapeHitTestRegistration;

    /// <summary>Enables precise fill and stroke hit testing for all shapes in the process.</summary>
    public static void EnableShapeHitTesting()
    {
        lock (_syncRoot)
        {
            _shapeHitTestRegistration ??= ShapeHitTesting.Register(_shapeHitTestProvider);
        }
    }

    private sealed class GeometryShapeHitTestProvider : IShapeHitTestProvider
    {
        public ShapeHitTestResult HitTest(Shape shape, PathGeometry? renderedGeometry, Point point)
        {
            if (renderedGeometry == null)
            {
                return ShapeHitTestResult.Miss;
            }

            if (shape.Fill != null && renderedGeometry.FillContains(point))
            {
                return ShapeHitTestResult.Hit;
            }

            Brush? stroke = shape.Stroke;
            if (stroke != null && shape.StrokeThickness > 0)
            {
                var pen = new Pen(stroke, shape.StrokeThickness, shape.StrokeStyle);
                if (renderedGeometry.StrokeContains(pen, point))
                {
                    return ShapeHitTestResult.Hit;
                }
            }

            return ShapeHitTestResult.Miss;
        }
    }
}
