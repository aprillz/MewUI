using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Geometry;

internal sealed class FlattenedFigure
{
    internal FlattenedFigure(List<Point> points, bool isClosed)
    {
        Points = points;
        IsClosed = isClosed;
    }

    internal List<Point> Points { get; }

    internal bool IsClosed { get; }
}

internal sealed class FlattenedPath
{
    internal FlattenedPath(FillRule fillRule, List<FlattenedFigure> figures, bool isValid)
    {
        FillRule = fillRule;
        Figures = figures;
        IsValid = isValid;
    }

    internal FillRule FillRule { get; }

    internal List<FlattenedFigure> Figures { get; }

    internal bool IsValid { get; }
}

internal static class GeometryFlattener
{
    internal const double DEFAULT_TOLERANCE = 0.25;

    private const double NUMERIC_FUZZ = 1e-12;
    private const int MAX_SUBDIVISION_DEPTH = 32;

    internal static FlattenedPath Flatten(PathGeometry geometry, double tolerance, ToleranceType toleranceType)
    {
        if (!IsValid(geometry) || !double.IsFinite(tolerance))
        {
            return new FlattenedPath(geometry.FillRule, new List<FlattenedFigure>(), false);
        }

        double absoluteTolerance = GetAbsoluteTolerance(geometry, tolerance, toleranceType);
        var figures = new List<FlattenedFigure>();
        List<Point>? points = null;
        Point current = default;
        Point figureStart = default;

        foreach (PathCommand command in geometry.Commands)
        {
            switch (command.Type)
            {
                case PathCommandType.MoveTo:
                    FinishFigure(false);
                    current = figureStart = new Point(command.X0, command.Y0);
                    points = new List<Point> { current };
                    break;
                case PathCommandType.LineTo:
                    if (points != null)
                    {
                        current = new Point(command.X0, command.Y0);
                        AddDistinct(points, current);
                    }
                    break;
                case PathCommandType.BezierTo:
                    if (points != null)
                    {
                        Point control1 = new(command.X0, command.Y0);
                        Point control2 = new(command.X1, command.Y1);
                        Point end = new(command.X2, command.Y2);
                        FlattenBezier(points, current, control1, control2, end, absoluteTolerance, 0);
                        current = end;
                    }
                    break;
                case PathCommandType.Close:
                    if (points != null)
                    {
                        current = figureStart;
                        FinishFigure(true);
                    }
                    break;
            }
        }

        FinishFigure(false);
        return new FlattenedPath(geometry.FillRule, figures, true);

        void FinishFigure(bool isClosed)
        {
            if (points != null)
            {
                figures.Add(new FlattenedFigure(points, isClosed));
                points = null;
            }
        }
    }

    internal static PathGeometry ToPath(FlattenedPath flattened)
    {
        var result = new PathGeometry { FillRule = flattened.FillRule };
        if (!flattened.IsValid)
        {
            return result;
        }

        foreach (FlattenedFigure figure in flattened.Figures)
        {
            if (figure.Points.Count == 0)
            {
                continue;
            }

            result.MoveTo(figure.Points[0]);
            for (int pointIndex = 1; pointIndex < figure.Points.Count; pointIndex++)
            {
                result.LineTo(figure.Points[pointIndex]);
            }

            if (figure.IsClosed)
            {
                result.Close();
            }
        }

        return result;
    }

    internal static Rect GetTightBounds(PathGeometry geometry)
    {
        if (!IsValid(geometry))
        {
            return Rect.Empty;
        }

        var bounds = new BoundsAccumulator();
        bool hasFigure = false;
        Point current = default;
        Point figureStart = default;

        foreach (PathCommand command in geometry.Commands)
        {
            switch (command.Type)
            {
                case PathCommandType.MoveTo:
                    current = figureStart = new Point(command.X0, command.Y0);
                    bounds.Add(current);
                    hasFigure = true;
                    break;
                case PathCommandType.LineTo:
                    if (hasFigure)
                    {
                        current = new Point(command.X0, command.Y0);
                        bounds.Add(current);
                    }
                    break;
                case PathCommandType.BezierTo:
                    if (hasFigure)
                    {
                        Point control1 = new(command.X0, command.Y0);
                        Point control2 = new(command.X1, command.Y1);
                        Point end = new(command.X2, command.Y2);
                        AddBezierBounds(ref bounds, current, control1, control2, end);
                        current = end;
                    }
                    break;
                case PathCommandType.Close:
                    if (hasFigure)
                    {
                        current = figureStart;
                        hasFigure = false;
                    }
                    break;
            }
        }

        return bounds.GetResult();
    }

    internal static bool FillContains(PathGeometry geometry, Point point, double tolerance, ToleranceType toleranceType)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            return false;
        }

        FlattenedPath flattened = Flatten(geometry, tolerance, toleranceType);
        if (!flattened.IsValid)
        {
            return false;
        }

        double absoluteTolerance = GetAbsoluteTolerance(geometry, tolerance, toleranceType);
        double toleranceSquared = absoluteTolerance * absoluteTolerance;
        int winding = 0;

        foreach (FlattenedFigure figure in flattened.Figures)
        {
            List<Point> points = figure.Points;
            if (points.Count < 2)
            {
                continue;
            }

            int segmentCount = points.Count;
            for (int pointIndex = 0; pointIndex < segmentCount; pointIndex++)
            {
                Point start = points[pointIndex];
                Point end = pointIndex + 1 < segmentCount ? points[pointIndex + 1] : points[0];
                if (DistanceSquaredToSegment(point, start, end) <= toleranceSquared)
                {
                    return true;
                }

                if (start.Y <= point.Y)
                {
                    if (end.Y > point.Y && IsLeft(start, end, point) > 0)
                    {
                        winding++;
                    }
                }
                else if (end.Y <= point.Y && IsLeft(start, end, point) < 0)
                {
                    winding--;
                }
            }
        }

        return geometry.FillRule == FillRule.EvenOdd ? (winding & 1) != 0 : winding != 0;
    }

    internal static double GetAbsoluteTolerance(PathGeometry geometry, double tolerance, ToleranceType toleranceType)
    {
        Rect looseBounds = geometry.GetBounds();
        double extent = Math.Max(looseBounds.Width, looseBounds.Height);
        if (!double.IsFinite(extent))
        {
            return double.NaN;
        }

        return toleranceType == ToleranceType.Relative
            ? Math.Max(tolerance, NUMERIC_FUZZ) * extent
            : Math.Max(tolerance, extent * NUMERIC_FUZZ);
    }

    internal static bool IsValid(PathGeometry geometry)
    {
        foreach (PathCommand command in geometry.Commands)
        {
            bool valid = command.Type switch
            {
                PathCommandType.MoveTo or PathCommandType.LineTo =>
                    double.IsFinite(command.X0) && double.IsFinite(command.Y0),
                PathCommandType.BezierTo =>
                    double.IsFinite(command.X0) && double.IsFinite(command.Y0) &&
                    double.IsFinite(command.X1) && double.IsFinite(command.Y1) &&
                    double.IsFinite(command.X2) && double.IsFinite(command.Y2),
                _ => true,
            };

            if (!valid)
            {
                return false;
            }
        }

        return true;
    }

    private static void FlattenBezier(List<Point> output, Point start, Point control1, Point control2,
        Point end, double tolerance, int depth)
    {
        if (depth >= MAX_SUBDIVISION_DEPTH || IsBezierFlat(start, control1, control2, end, tolerance))
        {
            AddDistinct(output, end);
            return;
        }

        Point midpoint01 = Midpoint(start, control1);
        Point midpoint12 = Midpoint(control1, control2);
        Point midpoint23 = Midpoint(control2, end);
        Point midpoint012 = Midpoint(midpoint01, midpoint12);
        Point midpoint123 = Midpoint(midpoint12, midpoint23);
        Point midpoint = Midpoint(midpoint012, midpoint123);

        FlattenBezier(output, start, midpoint01, midpoint012, midpoint, tolerance, depth + 1);
        FlattenBezier(output, midpoint, midpoint123, midpoint23, end, tolerance, depth + 1);
    }

    private static bool IsBezierFlat(Point start, Point control1, Point control2, Point end, double tolerance)
    {
        double toleranceSquared = tolerance * tolerance;
        return DistanceSquaredToSegment(control1, start, end) <= toleranceSquared &&
            DistanceSquaredToSegment(control2, start, end) <= toleranceSquared;
    }

    private static void AddBezierBounds(ref BoundsAccumulator bounds, Point start, Point control1,
        Point control2, Point end)
    {
        bounds.Add(start);
        bounds.Add(end);
        AddAxisExtrema(start.X, control1.X, control2.X, end.X, start, control1, control2, end, ref bounds);
        AddAxisExtrema(start.Y, control1.Y, control2.Y, end.Y, start, control1, control2, end, ref bounds);
    }

    private static void AddAxisExtrema(double startValue, double control1Value, double control2Value,
        double endValue, Point start, Point control1, Point control2, Point end, ref BoundsAccumulator bounds)
    {
        double coefficientA = -startValue + 3 * control1Value - 3 * control2Value + endValue;
        double coefficientB = 2 * (startValue - 2 * control1Value + control2Value);
        double coefficientC = control1Value - startValue;

        if (Math.Abs(coefficientA) <= NUMERIC_FUZZ)
        {
            if (Math.Abs(coefficientB) > NUMERIC_FUZZ)
            {
                AddBezierPointAt(-coefficientC / coefficientB, start, control1, control2, end, ref bounds);
            }
            return;
        }

        double discriminant = coefficientB * coefficientB - 4 * coefficientA * coefficientC;
        if (discriminant < 0)
        {
            return;
        }

        double root = Math.Sqrt(discriminant);
        AddBezierPointAt((-coefficientB + root) / (2 * coefficientA), start, control1, control2, end, ref bounds);
        AddBezierPointAt((-coefficientB - root) / (2 * coefficientA), start, control1, control2, end, ref bounds);
    }

    private static void AddBezierPointAt(double parameter, Point start, Point control1, Point control2,
        Point end, ref BoundsAccumulator bounds)
    {
        if (parameter > 0 && parameter < 1)
        {
            bounds.Add(EvaluateBezier(start, control1, control2, end, parameter));
        }
    }

    private static Point EvaluateBezier(Point start, Point control1, Point control2, Point end, double parameter)
    {
        Point first = Lerp(start, control1, parameter);
        Point second = Lerp(control1, control2, parameter);
        Point third = Lerp(control2, end, parameter);
        return Lerp(Lerp(first, second, parameter), Lerp(second, third, parameter), parameter);
    }

    private static double DistanceSquaredToSegment(Point point, Point start, Point end)
    {
        double deltaX = end.X - start.X;
        double deltaY = end.Y - start.Y;
        double lengthSquared = deltaX * deltaX + deltaY * deltaY;
        if (lengthSquared <= NUMERIC_FUZZ * NUMERIC_FUZZ)
        {
            return SquaredDistance(point, start);
        }

        double parameter = ((point.X - start.X) * deltaX + (point.Y - start.Y) * deltaY) / lengthSquared;
        parameter = Math.Clamp(parameter, 0, 1);
        double nearestX = start.X + parameter * deltaX;
        double nearestY = start.Y + parameter * deltaY;
        double distanceX = point.X - nearestX;
        double distanceY = point.Y - nearestY;
        return distanceX * distanceX + distanceY * distanceY;
    }

    private static double IsLeft(Point start, Point end, Point point) =>
        (end.X - start.X) * (point.Y - start.Y) - (point.X - start.X) * (end.Y - start.Y);

    private static double SquaredDistance(Point first, Point second)
    {
        double deltaX = first.X - second.X;
        double deltaY = first.Y - second.Y;
        return deltaX * deltaX + deltaY * deltaY;
    }

    private static Point Midpoint(Point first, Point second) =>
        new((first.X + second.X) * 0.5, (first.Y + second.Y) * 0.5);

    private static Point Lerp(Point first, Point second, double parameter) =>
        new(first.X + (second.X - first.X) * parameter, first.Y + (second.Y - first.Y) * parameter);

    private static void AddDistinct(List<Point> points, Point point)
    {
        if (points.Count == 0 || points[^1] != point)
        {
            points.Add(point);
        }
    }

    private struct BoundsAccumulator
    {
        private double _left;
        private double _top;
        private double _right;
        private double _bottom;
        private bool _hasPoint;

        internal void Add(Point point)
        {
            if (!_hasPoint)
            {
                _left = _right = point.X;
                _top = _bottom = point.Y;
                _hasPoint = true;
            }
            else
            {
                _left = Math.Min(_left, point.X);
                _top = Math.Min(_top, point.Y);
                _right = Math.Max(_right, point.X);
                _bottom = Math.Max(_bottom, point.Y);
            }
        }

        internal readonly Rect GetResult() => _hasPoint
            ? new Rect(_left, _top, _right - _left, _bottom - _top)
            : Rect.Empty;
    }
}
