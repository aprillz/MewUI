using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Shapes;

/// <summary>
/// Fluent API extension methods for shape elements.
/// </summary>
public static class ShapeExtensions
{
    #region Shape Common

    /// <summary>Sets the fill brush.</summary>
    public static T Fill<T>(this T shape, IBrush brush) where T : Shape
    {
        shape.Fill = brush;
        return shape;
    }

    /// <summary>Sets the fill to a solid color.</summary>
    public static T Fill<T>(this T shape, Color color) where T : Shape
    {
        shape.Fill = new SimpleSolidColorBrush(color);
        return shape;
    }

    /// <summary>Sets the stroke brush and thickness.</summary>
    public static T Stroke<T>(this T shape, IBrush brush, double thickness = 1) where T : Shape
    {
        shape.Stroke = brush;
        shape.StrokeThickness = thickness;
        return shape;
    }

    /// <summary>Sets the stroke to a solid color with the given thickness.</summary>
    public static T Stroke<T>(this T shape, Color color, double thickness = 1) where T : Shape
    {
        shape.Stroke = new SimpleSolidColorBrush(color);
        shape.StrokeThickness = thickness;
        return shape;
    }

    /// <summary>Sets the stroke style (line cap, line join, dash pattern).</summary>
    public static T StrokeStyle<T>(this T shape, StrokeStyle style) where T : Shape
    {
        shape.StrokeStyle = style;
        return shape;
    }

    /// <summary>Sets the stretch mode.</summary>
    public static T Stretch<T>(this T shape, Stretch stretch) where T : Shape
    {
        shape.Stretch = stretch;
        return shape;
    }

    #endregion

    #region Path

    /// <summary>Sets the path data geometry.</summary>
    public static Path Data(this Path path, PathGeometry geometry)
    {
        path.Data = geometry;
        return path;
    }

    /// <summary>Sets the path data from an SVG path data string.</summary>
    public static Path Data(this Path path, string svgPathData)
    {
        path.Data = PathGeometry.Parse(svgPathData);
        return path;
    }

    #endregion

    #region Rectangle

    /// <summary>Sets the corner radii.</summary>
    public static Rectangle CornerRadius(this Rectangle rect, double radiusX, double radiusY)
    {
        rect.RadiusX = radiusX;
        rect.RadiusY = radiusY;
        return rect;
    }

    /// <summary>Sets equal corner radius for both axes.</summary>
    public static Rectangle CornerRadius(this Rectangle rect, double radius)
    {
        rect.RadiusX = radius;
        rect.RadiusY = radius;
        return rect;
    }

    #endregion

    #region Line

    /// <summary>Sets the line start and end points.</summary>
    public static Line Points(this Line line, double x1, double y1, double x2, double y2)
    {
        line.X1 = x1;
        line.Y1 = y1;
        line.X2 = x2;
        line.Y2 = y2;
        return line;
    }

    #endregion
}
