using Aprillz.MewUI.Geometry;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Geometry.Test;

[TestClass]
public sealed class ShapeGeometryHitTestingTests
{
    [TestMethod]
    public void EnableShapeHitTesting_UsesRenderedFillAndStrokeForEveryShape()
    {
        PathShape fillShape = CreateShape(CreateTriangle(), new SolidColorBrush(Color.FromRgb(0, 0, 0)));

        Assert.AreSame(fillShape, fillShape.HitTest(new Point(90, 90)));

        GeometryServices.EnableShapeHitTesting();

        Assert.IsNull(fillShape.HitTest(new Point(90, 90)));
        Assert.AreSame(fillShape, fillShape.HitTest(new Point(10, 10)));

        PathShape strokeShape = CreateShape(CreateLine(), fill: null);
        strokeShape.Stroke = new SolidColorBrush(Color.FromRgb(0, 0, 0));
        strokeShape.StrokeThickness = 8;

        Assert.AreSame(strokeShape, strokeShape.HitTest(new Point(50, 3)));
        Assert.IsNull(strokeShape.HitTest(new Point(50, 10)));
    }

    [TestMethod]
    public void RenderedGeometry_AppliesStretchAndLayoutTranslation()
    {
        var shape = new PathShape
        {
            Data = PathGeometry.FromRect(0, 0, 10, 10),
            Stretch = Stretch.Fill,
        };
        shape.Arrange(new Rect(20, 30, 100, 50));

        PathGeometry? renderedGeometry = shape.RenderedGeometry;

        Assert.IsNotNull(renderedGeometry);
        Assert.AreEqual(new Rect(20, 30, 100, 50), renderedGeometry.GetBounds());

        renderedGeometry.MoveTo(200, 200);

        Assert.AreEqual(new Rect(20, 30, 100, 50), shape.RenderedGeometry!.GetBounds());
    }

    private static PathShape CreateShape(PathGeometry geometry, Brush? fill)
    {
        var shape = new PathShape
        {
            Data = geometry,
            Fill = fill,
        };
        shape.Arrange(new Rect(0, 0, 100, 100));
        return shape;
    }

    private static PathGeometry CreateTriangle()
    {
        var geometry = new PathGeometry();
        geometry.MoveTo(0, 0);
        geometry.LineTo(100, 0);
        geometry.LineTo(0, 100);
        geometry.Close();
        return geometry;
    }

    private static PathGeometry CreateLine()
    {
        var geometry = new PathGeometry();
        geometry.MoveTo(0, 0);
        geometry.LineTo(100, 0);
        return geometry;
    }
}
