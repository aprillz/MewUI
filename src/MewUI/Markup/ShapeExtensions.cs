using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;


/// <summary>
/// Fluent API extension methods for shape elements.
/// </summary>
public static class ShapeExtensions
{
    #region Shape Common

    /// <summary>Sets the fill brush.</summary>
    /// <typeparam name="T">Shape type.</typeparam>
    /// <param name="shape">Target shape.</param>
    /// <param name="brush">Fill brush.</param>
    /// <returns>The shape for chaining.</returns>
    public static T Fill<T>(this T shape, Brush brush) where T : Shape
    {
        shape.Fill = brush;
        return shape;
    }

    /// <summary>Sets the fill to a solid color.</summary>
    /// <typeparam name="T">Shape type.</typeparam>
    /// <param name="shape">Target shape.</param>
    /// <param name="color">Fill color.</param>
    /// <returns>The shape for chaining.</returns>
    public static T Fill<T>(this T shape, Color color) where T : Shape
    {
        shape.Fill = new SolidColorBrush(color);
        return shape;
    }

    /// <summary>Sets the stroke brush and thickness.</summary>
    /// <typeparam name="T">Shape type.</typeparam>
    /// <param name="shape">Target shape.</param>
    /// <param name="brush">Stroke brush.</param>
    /// <param name="thickness">Stroke thickness.</param>
    /// <returns>The shape for chaining.</returns>
    public static T Stroke<T>(this T shape, Brush brush, double thickness = 1) where T : Shape
    {
        shape.Stroke = brush;
        shape.StrokeThickness = thickness;
        return shape;
    }

    /// <summary>Sets the stroke to a solid color with the given thickness.</summary>
    /// <typeparam name="T">Shape type.</typeparam>
    /// <param name="shape">Target shape.</param>
    /// <param name="color">Stroke color.</param>
    /// <param name="thickness">Stroke thickness.</param>
    /// <returns>The shape for chaining.</returns>
    public static T Stroke<T>(this T shape, Color color, double thickness = 1) where T : Shape
    {
        shape.Stroke = new SolidColorBrush(color);
        shape.StrokeThickness = thickness;
        return shape;
    }

    /// <summary>Sets the stroke style (line cap, line join, dash pattern).</summary>
    /// <typeparam name="T">Shape type.</typeparam>
    /// <param name="shape">Target shape.</param>
    /// <param name="style">Stroke style.</param>
    /// <returns>The shape for chaining.</returns>
    public static T StrokeStyle<T>(this T shape, StrokeStyle style) where T : Shape
    {
        shape.StrokeStyle = style;
        return shape;
    }

    /// <summary>Sets the stretch mode.</summary>
    /// <typeparam name="T">Shape type.</typeparam>
    /// <param name="shape">Target shape.</param>
    /// <param name="stretch">Stretch mode.</param>
    /// <returns>The shape for chaining.</returns>
    public static T Stretch<T>(this T shape, Stretch stretch) where T : Shape
    {
        shape.Stretch = stretch;
        return shape;
    }

    #endregion

    #region Path

    /// <summary>Sets the path data geometry.</summary>
    /// <param name="path">Target path shape.</param>
    /// <param name="geometry">Path geometry.</param>
    /// <returns>The path shape for chaining.</returns>
    public static PathShape Data(this PathShape path, PathGeometry geometry)
    {
        path.Data = geometry;
        return path;
    }

    /// <summary>Sets the path data from an SVG path data string.</summary>
    /// <param name="path">Target path shape.</param>
    /// <param name="svgPathData">SVG path data.</param>
    /// <returns>The path shape for chaining.</returns>
    public static PathShape Data(this PathShape path, string svgPathData)
    {
        path.Data = PathGeometry.Parse(svgPathData);
        return path;
    }

    #endregion

    #region Rectangle

    /// <summary>Sets the corner radii.</summary>
    /// <param name="rect">Target rectangle.</param>
    /// <param name="radiusX">Horizontal corner radius.</param>
    /// <param name="radiusY">Vertical corner radius.</param>
    /// <returns>The rectangle for chaining.</returns>
    public static Rectangle CornerRadius(this Rectangle rect, double radiusX, double radiusY)
    {
        rect.RadiusX = radiusX;
        rect.RadiusY = radiusY;
        return rect;
    }

    /// <summary>Sets equal corner radius for both axes.</summary>
    /// <param name="rect">Target rectangle.</param>
    /// <param name="radius">Corner radius.</param>
    /// <returns>The rectangle for chaining.</returns>
    public static Rectangle CornerRadius(this Rectangle rect, double radius)
    {
        rect.RadiusX = radius;
        rect.RadiusY = radius;
        return rect;
    }

    #endregion

    #region Line

    /// <summary>Sets the line start and end points.</summary>
    /// <param name="line">Target line.</param>
    /// <param name="x1">Start point X coordinate.</param>
    /// <param name="y1">Start point Y coordinate.</param>
    /// <param name="x2">End point X coordinate.</param>
    /// <param name="y2">End point Y coordinate.</param>
    /// <returns>The line for chaining.</returns>
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
