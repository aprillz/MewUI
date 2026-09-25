using Aprillz.MewUI.Rendering;

using Clipper2Lib;

using ClipperFillRule = Clipper2Lib.FillRule;
using MewFillRule = Aprillz.MewUI.Rendering.FillRule;

namespace Aprillz.MewUI.Geometry;

internal enum GeometryRelation
{
    Empty,
    FullyInside,
    FullyContains,
    Intersects,
}

internal enum GeometryBooleanOperation
{
    Union,
    Intersect,
    Xor,
    Exclude,
}

internal static class ClipperGeometry
{
    private const int PRECISION = 4;
    private const double MAX_COORDINATE = 230_584_300_921_369.0;
    private const double MIN_AREA_TOLERANCE = 1e-8;

    internal static double GetArea(PathGeometry flattenedGeometry)
    {
        if (!TryNormalize(flattenedGeometry, out var normalized))
        {
            return 0;
        }

        return GetAbsoluteArea(normalized);
    }

    internal static GeometryRelation GetRelation(
        PathGeometry flattenedGeometry,
        PathGeometry flattenedOther)
    {
        if (!TryNormalize(flattenedGeometry, out var geometryPaths) ||
            !TryNormalize(flattenedOther, out var otherPaths) ||
            geometryPaths.Count == 0 ||
            otherPaths.Count == 0)
        {
            return GeometryRelation.Empty;
        }

        var intersection = BooleanOp(ClipType.Intersection, geometryPaths, otherPaths);
        double intersectionArea = GetAbsoluteArea(intersection);
        if (intersectionArea <= MIN_AREA_TOLERANCE)
        {
            return GeometryRelation.Empty;
        }

        double geometryArea = GetAbsoluteArea(geometryPaths);
        double otherArea = GetAbsoluteArea(otherPaths);
        double areaTolerance = Math.Max(
            MIN_AREA_TOLERANCE,
            Math.Max(geometryArea, otherArea) * 1e-12);

        bool containsOther = Math.Abs(intersectionArea - otherArea) <= areaTolerance;
        bool isInsideOther = Math.Abs(intersectionArea - geometryArea) <= areaTolerance;
        if (containsOther && isInsideOther)
        {
            return GeometryRelation.Intersects;
        }
        else if (containsOther)
        {
            return GeometryRelation.FullyContains;
        }
        else if (isInsideOther)
        {
            return GeometryRelation.FullyInside;
        }
        else
        {
            return GeometryRelation.Intersects;
        }
    }

    internal static PathGeometry GetOutline(PathGeometry flattenedGeometry)
    {
        if (!TryNormalize(flattenedGeometry, out var normalized))
        {
            return EmptyGeometry();
        }

        return ToGeometry(normalized);
    }

    internal static PathGeometry Combine(
        PathGeometry flattenedGeometry,
        PathGeometry flattenedOther,
        GeometryBooleanOperation operation)
    {
        if (!TryNormalize(flattenedGeometry, out var geometryPaths) ||
            !TryNormalize(flattenedOther, out var otherPaths))
        {
            return EmptyGeometry();
        }

        ClipType clipType;
        if (operation == GeometryBooleanOperation.Union)
        {
            clipType = ClipType.Union;
        }
        else if (operation == GeometryBooleanOperation.Intersect)
        {
            clipType = ClipType.Intersection;
        }
        else if (operation == GeometryBooleanOperation.Xor)
        {
            clipType = ClipType.Xor;
        }
        else
        {
            clipType = ClipType.Difference;
        }

        return ToGeometry(BooleanOp(clipType, geometryPaths, otherPaths));
    }

    private static bool TryNormalize(PathGeometry geometry, out PathsD normalized)
    {
        normalized = [];
        if (!TryToPaths(geometry, out var paths))
        {
            return false;
        }

        normalized = Clipper.BooleanOp(
            ClipType.Union,
            paths,
            null,
            ToClipperFillRule(geometry.FillRule),
            PRECISION);
        return true;
    }

    private static PathsD BooleanOp(ClipType clipType, PathsD subject, PathsD clip)
        => Clipper.BooleanOp(clipType, subject, clip, ClipperFillRule.NonZero, PRECISION);

    private static bool TryToPaths(PathGeometry geometry, out PathsD paths)
    {
        paths = [];
        PathD? current = null;

        foreach (var command in geometry.Commands)
        {
            if (command.Type == PathCommandType.MoveTo)
            {
                if (!TryAddPoint(command.X0, command.Y0, out var point))
                {
                    return false;
                }

                AddFigure(paths, current);
                current = [point];
            }
            else if (command.Type == PathCommandType.LineTo)
            {
                if (!TryAddPoint(command.X0, command.Y0, out var point))
                {
                    return false;
                }

                if (current != null && (current.Count == 0 || current[^1] != point))
                {
                    current.Add(point);
                }
            }
            else if (command.Type == PathCommandType.Close)
            {
                AddFigure(paths, current);
                current = null;
            }
            else
            {
                return false;
            }
        }

        AddFigure(paths, current);
        return true;
    }

    private static bool TryAddPoint(double xCoordinate, double yCoordinate, out PointD point)
    {
        point = default;
        if (!double.IsFinite(xCoordinate) || !double.IsFinite(yCoordinate) ||
            Math.Abs(xCoordinate) > MAX_COORDINATE || Math.Abs(yCoordinate) > MAX_COORDINATE)
        {
            return false;
        }

        point = new PointD(xCoordinate, yCoordinate);
        return true;
    }

    private static void AddFigure(PathsD paths, PathD? figure)
    {
        if (figure == null)
        {
            return;
        }

        if (figure.Count > 1 && figure[0] == figure[^1])
        {
            figure.RemoveAt(figure.Count - 1);
        }

        if (figure.Count >= 3)
        {
            paths.Add(figure);
        }
    }

    private static PathGeometry ToGeometry(PathsD paths)
    {
        var geometry = new PathGeometry { FillRule = MewFillRule.NonZero };
        foreach (var path in paths)
        {
            if (path.Count < 3 || !AllFinite(path))
            {
                continue;
            }

            geometry.MoveTo(path[0].x, path[0].y);
            for (int pointIndex = 1; pointIndex < path.Count; pointIndex++)
            {
                geometry.LineTo(path[pointIndex].x, path[pointIndex].y);
            }
            geometry.Close();
        }

        return geometry;
    }

    private static bool AllFinite(PathD path)
    {
        foreach (var point in path)
        {
            if (!double.IsFinite(point.x) || !double.IsFinite(point.y))
            {
                return false;
            }
        }

        return true;
    }

    private static double GetAbsoluteArea(PathsD paths)
        => Math.Abs(Clipper.Area(paths));

    private static ClipperFillRule ToClipperFillRule(MewFillRule fillRule)
        => fillRule == MewFillRule.EvenOdd
            ? ClipperFillRule.EvenOdd
            : ClipperFillRule.NonZero;

    private static PathGeometry EmptyGeometry()
        => new() { FillRule = MewFillRule.NonZero };
}
