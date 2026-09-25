using System.Numerics;

using Aprillz.MewUI.Geometry;
using Aprillz.MewUI.Rendering;

[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]

namespace Aprillz.MewUI.Geometry.Test;

[TestClass]
public sealed class PathGeometryOperationsTests
{
    [TestMethod]
    public void GetTightBounds_UsesBezierExtrema()
    {
        var geometry = new PathGeometry();
        geometry.MoveTo(0, 0);
        geometry.BezierTo(0, 100, 100, 100, 100, 0);

        Rect bounds = geometry.GetTightBounds();

        AssertRect(bounds, 0, 0, 100, 75, 1e-9);
    }

    [TestMethod]
    public void GetFlattenedPathGeometry_PreservesFiguresAndRemovesCurves()
    {
        var geometry = new PathGeometry { FillRule = FillRule.EvenOdd };
        geometry.MoveTo(0, 0);
        geometry.BezierTo(0, 100, 100, 100, 100, 0);
        geometry.Close();

        PathGeometry flattened = geometry.GetFlattenedPathGeometry(0.1, ToleranceType.Absolute);

        Assert.AreEqual(FillRule.EvenOdd, flattened.FillRule);
        Assert.IsGreaterThan(3, flattened.Commands.Length);
        Assert.IsFalse(HasCommand(flattened, PathCommandType.BezierTo));
        Assert.AreEqual(PathCommandType.Close, flattened.Commands[^1].Type);
    }

    [TestMethod]
    public void FillContains_ClosesOpenFigureAndIncludesToleranceBoundary()
    {
        PathGeometry geometry = CreateOpenRectangle(0, 0, 10, 10);

        Assert.IsTrue(geometry.FillContains(new Point(5, 5)));
        Assert.IsTrue(geometry.FillContains(new Point(10.1, 5), 0.2, ToleranceType.Absolute));
        Assert.IsFalse(geometry.FillContains(new Point(10.3, 5), 0.2, ToleranceType.Absolute));
    }

    [TestMethod]
    public void FillContains_UsesConfiguredFillRule()
    {
        var geometry = new PathGeometry();
        AddRectangle(geometry, 0, 0, 10, 10);
        AddRectangle(geometry, 2, 2, 6, 6);

        geometry.FillRule = FillRule.EvenOdd;
        Assert.IsFalse(geometry.FillContains(new Point(5, 5)));

        geometry.FillRule = FillRule.NonZero;
        Assert.IsTrue(geometry.FillContains(new Point(5, 5)));
    }

    [TestMethod]
    public void GetArea_NormalizesOverlappingFigures()
    {
        var geometry = new PathGeometry();
        AddRectangle(geometry, 0, 0, 10, 10);
        AddRectangle(geometry, 5, 0, 10, 10);

        Assert.AreEqual(150, geometry.GetArea(), 1e-6);
    }

    [TestMethod]
    public void FillContainsWithDetail_ClassifiesRelationships()
    {
        PathGeometry outer = PathGeometry.FromRect(0, 0, 10, 10);
        PathGeometry inner = PathGeometry.FromRect(2, 2, 2, 2);
        PathGeometry partial = PathGeometry.FromRect(8, 0, 5, 5);
        PathGeometry separate = PathGeometry.FromRect(20, 0, 2, 2);
        PathGeometry touching = PathGeometry.FromRect(10, 0, 2, 2);

        Assert.AreEqual(IntersectionDetail.FullyContains, outer.FillContainsWithDetail(inner));
        Assert.AreEqual(IntersectionDetail.FullyInside, inner.FillContainsWithDetail(outer));
        Assert.AreEqual(IntersectionDetail.Intersects, outer.FillContainsWithDetail(outer));
        Assert.AreEqual(IntersectionDetail.Intersects, outer.FillContainsWithDetail(partial));
        Assert.AreEqual(IntersectionDetail.Empty, outer.FillContainsWithDetail(separate));
        Assert.AreEqual(IntersectionDetail.Empty, outer.FillContainsWithDetail(touching));
        Assert.IsTrue(outer.Encloses(inner));
        Assert.IsFalse(outer.Intersects(separate));
    }

    [TestMethod]
    public void Combine_ProducesExpectedAreasAndAppliesTransform()
    {
        PathGeometry left = PathGeometry.FromRect(0, 0, 10, 10);
        PathGeometry right = PathGeometry.FromRect(5, 0, 10, 10);

        Assert.AreEqual(150, PathGeometryOperations.Combine(left, right, GeometryCombineMode.Union).GetArea(), 1e-6);
        Assert.AreEqual(50, PathGeometryOperations.Combine(left, right, GeometryCombineMode.Intersect).GetArea(), 1e-6);
        Assert.AreEqual(100, PathGeometryOperations.Combine(left, right, GeometryCombineMode.Xor).GetArea(), 1e-6);
        Assert.AreEqual(50, PathGeometryOperations.Combine(left, right, GeometryCombineMode.Exclude).GetArea(), 1e-6);

        PathGeometry scaled = PathGeometryOperations.Combine(
            left,
            right,
            GeometryCombineMode.Union,
            Matrix3x2.CreateScale(2));
        Assert.AreEqual(600, scaled.GetArea(), 1e-5);
    }

    [TestMethod]
    public void StrokeOperations_RespectFlatAndSquareCaps()
    {
        PathGeometry line = CreateLine(0, 0, 10, 0);
        var flatPen = new Pen(Color.FromRgb(0, 0, 0), 4);
        var squarePen = new Pen(
            Color.FromRgb(0, 0, 0),
            4,
            new StrokeStyle { LineCap = StrokeLineCap.Square, MiterLimit = 10 });

        AssertRect(line.GetRenderBounds(flatPen), 0, -2, 10, 4, 1e-9);
        AssertRect(line.GetRenderBounds(squarePen), -2, -2, 14, 4, 1e-9);
        Assert.IsFalse(line.StrokeContains(flatPen, new Point(-1, 0)));
        Assert.IsTrue(line.StrokeContains(squarePen, new Point(-1, 0)));
    }

    [TestMethod]
    public void StrokeOperations_MatchRoundCapAndMiterBounds()
    {
        PathGeometry line = CreateLine(0, 0, 10, 0);
        var roundPen = new Pen(
            Color.FromRgb(0, 0, 0),
            4,
            new StrokeStyle { LineCap = StrokeLineCap.Round, MiterLimit = 10 });
        AssertRect(line.GetRenderBounds(roundPen), -2, -2, 14, 4, 1e-6);

        var corner = new PathGeometry();
        corner.MoveTo(0, 10);
        corner.LineTo(10, 0);
        corner.LineTo(20, 10);
        var miterPen = new Pen(
            Color.FromRgb(0, 0, 0),
            4,
            new StrokeStyle { LineJoin = StrokeLineJoin.Miter, MiterLimit = 10 });
        AssertRect(
            corner.GetRenderBounds(miterPen),
            -Math.Sqrt(2),
            -2 * Math.Sqrt(2),
            20 + 2 * Math.Sqrt(2),
            10 + 3 * Math.Sqrt(2),
            1e-6);
    }

    [TestMethod]
    public void StrokeContains_RespectsDashPattern()
    {
        PathGeometry line = CreateLine(0, 0, 20, 0);
        var pen = new Pen(
            Color.FromRgb(0, 0, 0),
            2,
            new StrokeStyle { DashArray = new[] { 2.0, 2.0 }, MiterLimit = 10 });

        Assert.IsTrue(line.StrokeContains(pen, new Point(1, 0)));
        Assert.IsFalse(line.StrokeContains(pen, new Point(6, 0)));
        Assert.IsTrue(line.StrokeContains(pen, new Point(9, 0)));
    }

    [TestMethod]
    public void WidenedAndOutlinedPaths_AreClosedStraightNonZeroFigures()
    {
        PathGeometry curve = new();
        curve.MoveTo(0, 0);
        curve.BezierTo(0, 20, 20, 20, 20, 0);
        var pen = new Pen(Color.FromRgb(0, 0, 0), 3);

        AssertClosedStraightNonZero(curve.GetWidenedPathGeometry(pen));
        AssertClosedStraightNonZero(curve.GetOutlinedPathGeometry());
    }

    [TestMethod]
    public void StrokeContainsWithDetail_UsesWidenedRegion()
    {
        PathGeometry line = CreateLine(0, 0, 10, 0);
        var pen = new Pen(Color.FromRgb(0, 0, 0), 4);

        Assert.AreEqual(
            IntersectionDetail.FullyContains,
            line.StrokeContainsWithDetail(pen, PathGeometry.FromRect(2, -1, 2, 2)));
        Assert.AreEqual(
            IntersectionDetail.Empty,
            line.StrokeContainsWithDetail(pen, PathGeometry.FromRect(2, 3, 2, 2)));
    }

    [TestMethod]
    public void StrokeContains_DirectSinkMatchesWidenedPath()
    {
        var geometry = new PathGeometry();
        geometry.MoveTo(0, 10);
        geometry.LineTo(10, 0);
        geometry.LineTo(20, 10);

        StrokeStyle[] styles =
        [
            new StrokeStyle { LineJoin = StrokeLineJoin.Miter, MiterLimit = 10 },
            new StrokeStyle { LineJoin = StrokeLineJoin.Round, LineCap = StrokeLineCap.Round, MiterLimit = 10 },
            new StrokeStyle { DashArray = new[] { 2.0, 1.0 }, DashOffset = -0.5, MiterLimit = 10 },
        ];

        foreach (StrokeStyle style in styles)
        {
            var pen = new Pen(Color.FromRgb(0, 0, 0), 3, style);
            PathGeometry widened = geometry.GetWidenedPathGeometry(pen);
            for (int yCoordinate = -4; yCoordinate <= 14; yCoordinate += 2)
            {
                for (int xCoordinate = -4; xCoordinate <= 24; xCoordinate += 2)
                {
                    Point point = new(xCoordinate, yCoordinate);
                    Assert.AreEqual(
                        widened.FillContains(point),
                        geometry.StrokeContains(pen, point),
                        $"Mismatch at {point} for {style.LineJoin}/{style.LineCap}.");
                }
            }
        }
    }

    [TestMethod]
    public void NonFiniteCoordinates_ReturnSafeResults()
    {
        var geometry = new PathGeometry();
        geometry.MoveTo(double.NaN, 0);
        geometry.LineTo(10, 0);
        var pen = new Pen(Color.FromRgb(0, 0, 0), 2);

        Assert.AreEqual(Rect.Empty, geometry.GetTightBounds());
        Assert.AreEqual(0, geometry.GetArea());
        Assert.IsFalse(geometry.FillContains(new Point(0, 0)));
        Assert.IsFalse(geometry.StrokeContains(pen, new Point(0, 0)));
        Assert.IsTrue(geometry.GetFlattenedPathGeometry().IsEmpty);
        Assert.IsTrue(geometry.GetWidenedPathGeometry(pen).IsEmpty);
        Assert.IsTrue(geometry.GetOutlinedPathGeometry().IsEmpty);
    }

    [TestMethod]
    public void GeometryOperations_AcceptPrimitiveAndGroupedGeometries()
    {
        var outer = new RectangleGeometry(new Rect(0, 0, 10, 10));
        var inner = new EllipseGeometry(new Rect(2, 2, 6, 6));
        var group = new GeometryGroup { FillRule = FillRule.EvenOdd };
        group.Add(outer);
        group.Add(new RectangleGeometry(new Rect(2, 2, 6, 6)));
        var line = new LineGeometry(new Point(0, 0), new Point(10, 0));
        var pen = new Pen(Color.Black, 2);

        Assert.AreEqual(100, outer.GetArea(), 1e-6);
        Assert.IsTrue(outer.FillContains(new Point(5, 5)));
        Assert.AreEqual(IntersectionDetail.FullyContains, outer.FillContainsWithDetail(inner));
        Assert.AreEqual(64, group.GetArea(), 1e-6);
        Assert.IsTrue(line.StrokeContains(pen, new Point(5, 0)));
        Assert.AreEqual(100, PathGeometryOperations.Combine(outer, group, GeometryCombineMode.Union).GetArea(), 1e-6);
    }

    private static PathGeometry CreateLine(double x1, double y1, double x2, double y2)
    {
        var geometry = new PathGeometry();
        geometry.MoveTo(x1, y1);
        geometry.LineTo(x2, y2);
        return geometry;
    }

    private static PathGeometry CreateOpenRectangle(
        double xCoordinate,
        double yCoordinate,
        double width,
        double height)
    {
        var geometry = new PathGeometry();
        geometry.MoveTo(xCoordinate, yCoordinate);
        geometry.LineTo(xCoordinate + width, yCoordinate);
        geometry.LineTo(xCoordinate + width, yCoordinate + height);
        geometry.LineTo(xCoordinate, yCoordinate + height);
        return geometry;
    }

    private static void AddRectangle(
        PathGeometry geometry,
        double xCoordinate,
        double yCoordinate,
        double width,
        double height)
    {
        geometry.MoveTo(xCoordinate, yCoordinate);
        geometry.LineTo(xCoordinate + width, yCoordinate);
        geometry.LineTo(xCoordinate + width, yCoordinate + height);
        geometry.LineTo(xCoordinate, yCoordinate + height);
        geometry.Close();
    }

    private static void AssertClosedStraightNonZero(PathGeometry geometry)
    {
        Assert.AreEqual(FillRule.NonZero, geometry.FillRule);
        Assert.IsFalse(geometry.IsEmpty);
        Assert.IsFalse(HasCommand(geometry, PathCommandType.BezierTo));

        int moveCount = CountCommands(geometry, PathCommandType.MoveTo);
        int closeCount = CountCommands(geometry, PathCommandType.Close);
        Assert.AreEqual(moveCount, closeCount);
    }

    private static bool HasCommand(PathGeometry geometry, PathCommandType type)
    {
        foreach (PathCommand command in geometry.Commands)
        {
            if (command.Type == type)
            {
                return true;
            }
        }

        return false;
    }

    private static int CountCommands(PathGeometry geometry, PathCommandType type)
    {
        int count = 0;
        foreach (PathCommand command in geometry.Commands)
        {
            if (command.Type == type)
            {
                count++;
            }
        }

        return count;
    }

    private static void AssertRect(
        Rect actual,
        double xCoordinate,
        double yCoordinate,
        double width,
        double height,
        double tolerance)
    {
        Assert.AreEqual(xCoordinate, actual.X, tolerance);
        Assert.AreEqual(yCoordinate, actual.Y, tolerance);
        Assert.AreEqual(width, actual.Width, tolerance);
        Assert.AreEqual(height, actual.Height, tolerance);
    }
}
