using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Geometry;

internal static class StrokeWidener
{
    private const double DEGENERATE_LENGTH_SQUARED = 1e-24;
    private const int MAX_ARC_SEGMENTS = 1024;

    internal static PathGeometry Widen(PathGeometry geometry, Pen pen, double tolerance, ToleranceType toleranceType)
    {
        var sink = new PathStrokeSink();
        Widen(geometry, pen, tolerance, toleranceType, sink);
        return sink.Geometry;
    }

    internal static Rect GetBounds(PathGeometry geometry, Pen pen, double tolerance, ToleranceType toleranceType)
    {
        var sink = new BoundsStrokeSink();
        Widen(geometry, pen, tolerance, toleranceType, sink);
        return sink.GetBounds();
    }

    internal static bool Contains(PathGeometry geometry, Pen pen, Point point, double tolerance,
        ToleranceType toleranceType)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
        {
            return false;
        }

        double absoluteTolerance = GeometryFlattener.GetAbsoluteTolerance(geometry, tolerance, toleranceType);
        var sink = new HitTestStrokeSink(point, absoluteTolerance);
        Widen(geometry, pen, tolerance, toleranceType, sink);
        return sink.Contains;
    }

    private static void Widen(PathGeometry geometry, Pen pen, double tolerance, ToleranceType toleranceType,
        IStrokeSink sink)
    {
        if (!double.IsFinite(pen.Thickness) || pen.Thickness <= 0 ||
            !double.IsFinite(tolerance) || !IsValidStyle(pen.StrokeStyle))
        {
            return;
        }

        FlattenedPath flattened = GeometryFlattener.Flatten(geometry, tolerance, toleranceType);
        if (!flattened.IsValid)
        {
            return;
        }

        double absoluteTolerance = GeometryFlattener.GetAbsoluteTolerance(geometry, tolerance, toleranceType);
        double halfWidth = pen.Thickness * 0.5;
        foreach (FlattenedFigure figure in flattened.Figures)
        {
            foreach (StrokeRun run in CreateRuns(figure, pen))
            {
                WidenRun(sink, run, halfWidth, absoluteTolerance, pen.StrokeStyle);
            }
        }
    }

    private static List<StrokeRun> CreateRuns(FlattenedFigure figure, Pen pen)
    {
        List<Point> points = CleanPoints(figure.Points, figure.IsClosed);
        if (points.Count < 2)
        {
            return new List<StrokeRun>();
        }

        IReadOnlyList<double>? dashArray = pen.StrokeStyle.DashArray;
        if (dashArray == null || dashArray.Count == 0)
        {
            return new List<StrokeRun> { new(points, figure.IsClosed) };
        }

        var pattern = new double[dashArray.Count % 2 == 0 ? dashArray.Count : dashArray.Count * 2];
        double patternLength = 0;
        for (int dashIndex = 0; dashIndex < pattern.Length; dashIndex++)
        {
            double length = dashArray[dashIndex % dashArray.Count] * pen.Thickness;
            pattern[dashIndex] = length;
            patternLength += length;
        }

        if (!double.IsFinite(patternLength) || patternLength <= 0)
        {
            return new List<StrokeRun>();
        }

        double offset = pen.StrokeStyle.DashOffset * pen.Thickness;
        offset %= patternLength;
        if (offset < 0)
        {
            offset += patternLength;
        }

        int patternIndex = 0;
        bool drawing = true;
        double remaining = pattern[0];
        AdvanceZeroEntries();
        while (offset > 0)
        {
            double consumed = Math.Min(offset, remaining);
            offset -= consumed;
            remaining -= consumed;
            if (remaining <= 0)
            {
                NextPatternEntry();
            }
        }

        bool startsDrawing = drawing;
        var runs = new List<StrokeRun>();
        List<Point>? currentRun = drawing ? new List<Point> { points[0] } : null;
        int segmentCount = figure.IsClosed ? points.Count : points.Count - 1;

        for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            Point segmentStart = points[segmentIndex];
            Point segmentEnd = points[(segmentIndex + 1) % points.Count];
            double deltaX = segmentEnd.X - segmentStart.X;
            double deltaY = segmentEnd.Y - segmentStart.Y;
            double segmentLength = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
            double consumed = 0;

            while (consumed < segmentLength)
            {
                double take = Math.Min(remaining, segmentLength - consumed);
                double endFraction = (consumed + take) / segmentLength;
                Point pieceEnd = new(segmentStart.X + deltaX * endFraction, segmentStart.Y + deltaY * endFraction);
                if (drawing)
                {
                    currentRun ??= new List<Point> { new Point(
                        segmentStart.X + deltaX * (consumed / segmentLength),
                        segmentStart.Y + deltaY * (consumed / segmentLength)) };
                    AddDistinct(currentRun, pieceEnd);
                }

                consumed += take;
                remaining -= take;
                if (remaining <= 1e-12)
                {
                    if (drawing && currentRun != null && currentRun.Count > 1)
                    {
                        runs.Add(new StrokeRun(currentRun, false));
                        currentRun = null;
                    }
                    NextPatternEntry();
                }
            }
        }

        bool endsDrawing = drawing && currentRun != null && currentRun.Count > 1;
        if (endsDrawing)
        {
            runs.Add(new StrokeRun(currentRun!, false));
        }

        if (figure.IsClosed && startsDrawing && endsDrawing && runs.Count > 0)
        {
            if (runs.Count == 1)
            {
                StrokeRun only = runs[0];
                if (only.Points[0] == only.Points[^1])
                {
                    only.Points.RemoveAt(only.Points.Count - 1);
                    runs[0] = new StrokeRun(only.Points, true);
                }
            }
            else
            {
                List<Point> first = runs[0].Points;
                List<Point> last = runs[^1].Points;
                for (int pointIndex = 1; pointIndex < first.Count; pointIndex++)
                {
                    AddDistinct(last, first[pointIndex]);
                }
                runs[^1] = new StrokeRun(last, false);
                runs.RemoveAt(0);
            }
        }

        return runs;

        void NextPatternEntry()
        {
            patternIndex = (patternIndex + 1) % pattern.Length;
            drawing = !drawing;
            remaining = pattern[patternIndex];
            AdvanceZeroEntries();
        }

        void AdvanceZeroEntries()
        {
            int checkedCount = 0;
            while (remaining <= 0 && checkedCount < pattern.Length)
            {
                patternIndex = (patternIndex + 1) % pattern.Length;
                drawing = !drawing;
                remaining = pattern[patternIndex];
                checkedCount++;
            }
        }
    }

    private static void WidenRun(IStrokeSink sink, StrokeRun run, double halfWidth, double tolerance,
        StrokeStyle style)
    {
        List<Point> points = run.Points;
        int segmentCount = run.IsClosed ? points.Count : points.Count - 1;
        if (segmentCount <= 0)
        {
            return;
        }

        var directions = new Vector[segmentCount];
        var normals = new Vector[segmentCount];
        for (int segmentIndex = 0; segmentIndex < segmentCount; segmentIndex++)
        {
            Point start = points[segmentIndex];
            Point end = points[(segmentIndex + 1) % points.Count];
            Vector direction = Normalize(end.X - start.X, end.Y - start.Y);
            directions[segmentIndex] = direction;
            normals[segmentIndex] = new Vector(-direction.Y, direction.X);
            AddSegment(sink, start, end, normals[segmentIndex], halfWidth);
        }

        if (run.IsClosed)
        {
            for (int pointIndex = 0; pointIndex < points.Count; pointIndex++)
            {
                int previousSegment = (pointIndex + segmentCount - 1) % segmentCount;
                int nextSegment = pointIndex % segmentCount;
                AddJoin(sink, points[pointIndex], directions[previousSegment], normals[previousSegment],
                    directions[nextSegment], normals[nextSegment], halfWidth, tolerance, style);
            }
        }
        else
        {
            for (int pointIndex = 1; pointIndex < points.Count - 1; pointIndex++)
            {
                AddJoin(sink, points[pointIndex], directions[pointIndex - 1], normals[pointIndex - 1],
                    directions[pointIndex], normals[pointIndex], halfWidth, tolerance, style);
            }

            AddCap(sink, points[0], -directions[0], normals[0], halfWidth, tolerance, style.LineCap);
            AddCap(sink, points[^1], directions[^1], normals[^1], halfWidth, tolerance, style.LineCap);
        }
    }

    private static void AddSegment(IStrokeSink sink, Point start, Point end, Vector normal, double halfWidth)
    {
        Vector offset = normal * halfWidth;
        AddPolygon(sink,
            start + offset,
            end + offset,
            end - offset,
            start - offset);
    }

    private static void AddJoin(IStrokeSink sink, Point vertex, Vector incoming, Vector incomingNormal,
        Vector outgoing, Vector outgoingNormal, double halfWidth, double tolerance, StrokeStyle style)
    {
        double cross = Vector.Cross(incoming, outgoing);
        double dot = Vector.Dot(incoming, outgoing);
        if (Math.Abs(cross) <= 1e-12 && dot > 0)
        {
            return;
        }

        if (style.LineJoin == StrokeLineJoin.Round)
        {
            AddCircle(sink, vertex, halfWidth, tolerance);
            return;
        }

        double side = cross > 0 ? -1 : 1;
        Point incomingOuter = vertex + incomingNormal * (halfWidth * side);
        Point outgoingOuter = vertex + outgoingNormal * (halfWidth * side);
        if (style.LineJoin == StrokeLineJoin.Bevel || Math.Abs(cross) <= 1e-12)
        {
            AddPolygon(sink, incomingOuter, vertex, outgoingOuter);
            return;
        }

        Vector firstOffset = incomingNormal * side;
        Vector secondOffset = outgoingNormal * side;
        Vector bisector = Normalize(firstOffset.X + secondOffset.X, firstOffset.Y + secondOffset.Y);
        double denominator = Vector.Dot(bisector, secondOffset);
        double miterRatio = denominator > 1e-12 ? 1 / denominator : double.PositiveInfinity;
        double miterLimit = Math.Max(1, style.MiterLimit);
        if (!double.IsFinite(miterRatio) || miterRatio > miterLimit)
        {
            AddPolygon(sink, incomingOuter, vertex, outgoingOuter);
        }
        else
        {
            Point tip = vertex + bisector * (halfWidth * miterRatio);
            AddPolygon(sink, incomingOuter, tip, outgoingOuter);
        }
    }

    private static void AddCap(IStrokeSink sink, Point endpoint, Vector outward, Vector normal,
        double halfWidth, double tolerance, StrokeLineCap cap)
    {
        if (cap == StrokeLineCap.Flat)
        {
            return;
        }

        if (cap == StrokeLineCap.Square)
        {
            Vector side = normal * halfWidth;
            Vector extension = outward * halfWidth;
            AddPolygon(sink, endpoint + side, endpoint + side + extension,
                endpoint - side + extension, endpoint - side);
            return;
        }

        double outwardAngle = Math.Atan2(outward.Y, outward.X);
        AddSector(sink, endpoint, halfWidth, outwardAngle - Math.PI / 2,
            outwardAngle + Math.PI / 2, tolerance);
    }

    private static void AddCircle(IStrokeSink sink, Point center, double radius, double tolerance) =>
        AddSector(sink, center, radius, 0, Math.PI * 2, tolerance);

    private static void AddSector(IStrokeSink sink, Point center, double radius, double startAngle,
        double endAngle, double tolerance)
    {
        int segmentCount = GetArcSegmentCount(radius, Math.Abs(endAngle - startAngle), tolerance);
        double sweep = Math.Abs(endAngle - startAngle);
        if (Math.Abs(sweep - Math.PI) <= 1e-12 && (segmentCount & 1) != 0)
        {
            segmentCount++;
        }
        else if (Math.Abs(sweep - Math.PI * 2) <= 1e-12)
        {
            segmentCount = Math.Min(MAX_ARC_SEGMENTS, (segmentCount + 3) / 4 * 4);
        }
        var polygon = new Point[segmentCount + 2];
        polygon[0] = center;
        for (int segmentIndex = 0; segmentIndex <= segmentCount; segmentIndex++)
        {
            double fraction = (double)segmentIndex / segmentCount;
            double angle = startAngle + (endAngle - startAngle) * fraction;
            polygon[segmentIndex + 1] = new Point(
                center.X + Math.Cos(angle) * radius,
                center.Y + Math.Sin(angle) * radius);
        }
        AddPolygon(sink, polygon);
    }

    private static int GetArcSegmentCount(double radius, double sweep, double tolerance)
    {
        if (radius <= 0 || sweep <= 0)
        {
            return 1;
        }

        double safeTolerance = Math.Max(tolerance, radius * 1e-12);
        double cosine = Math.Clamp(1 - safeTolerance / radius, -1, 1);
        double maximumAngle = 2 * Math.Acos(cosine);
        if (maximumAngle <= 1e-6)
        {
            maximumAngle = 1e-6;
        }
        return Math.Clamp((int)Math.Ceiling(sweep / maximumAngle), 1, MAX_ARC_SEGMENTS);
    }

    private static void AddPolygon(IStrokeSink sink, params Point[] points)
    {
        if (points.Length < 3)
        {
            return;
        }

        double twiceArea = 0;
        for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
        {
            Point current = points[pointIndex];
            Point next = points[(pointIndex + 1) % points.Length];
            twiceArea += current.X * next.Y - next.X * current.Y;
        }

        if (Math.Abs(twiceArea) <= DEGENERATE_LENGTH_SQUARED)
        {
            return;
        }

        sink.AddPolygon(points, twiceArea > 0);
    }

    private static List<Point> CleanPoints(List<Point> source, bool isClosed)
    {
        var result = new List<Point>(source.Count);
        foreach (Point point in source)
        {
            AddDistinct(result, point);
        }

        if (isClosed && result.Count > 1 && result[0] == result[^1])
        {
            result.RemoveAt(result.Count - 1);
        }
        return result;
    }

    private static bool IsValidStyle(StrokeStyle style)
    {
        if (!double.IsFinite(style.MiterLimit) || !double.IsFinite(style.DashOffset))
        {
            return false;
        }

        if (style.DashArray != null)
        {
            foreach (double dash in style.DashArray)
            {
                if (!double.IsFinite(dash) || dash < 0)
                {
                    return false;
                }
            }
        }
        return true;
    }

    private static Vector Normalize(double xCoordinate, double yCoordinate)
    {
        double lengthSquared = xCoordinate * xCoordinate + yCoordinate * yCoordinate;
        if (lengthSquared <= DEGENERATE_LENGTH_SQUARED)
        {
            return Vector.Zero;
        }
        double inverseLength = 1 / Math.Sqrt(lengthSquared);
        return new Vector(xCoordinate * inverseLength, yCoordinate * inverseLength);
    }

    private static void AddDistinct(List<Point> points, Point point)
    {
        if (points.Count == 0 || points[^1] != point)
        {
            points.Add(point);
        }
    }

    private interface IStrokeSink
    {
        void AddPolygon(Point[] points, bool forward);
    }

    private sealed class PathStrokeSink : IStrokeSink
    {
        internal PathGeometry Geometry { get; } = new() { FillRule = FillRule.NonZero };

        public void AddPolygon(Point[] points, bool forward)
        {
            if (forward)
            {
                Geometry.MoveTo(points[0]);
                for (int pointIndex = 1; pointIndex < points.Length; pointIndex++)
                {
                    Geometry.LineTo(points[pointIndex]);
                }
            }
            else
            {
                Geometry.MoveTo(points[^1]);
                for (int pointIndex = points.Length - 2; pointIndex >= 0; pointIndex--)
                {
                    Geometry.LineTo(points[pointIndex]);
                }
            }
            Geometry.Close();
        }
    }

    private sealed class BoundsStrokeSink : IStrokeSink
    {
        private double _left;
        private double _top;
        private double _right;
        private double _bottom;
        private bool _hasPoint;

        public void AddPolygon(Point[] points, bool forward)
        {
            foreach (Point point in points)
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
        }

        internal Rect GetBounds() => _hasPoint
            ? new Rect(_left, _top, _right - _left, _bottom - _top)
            : Rect.Empty;
    }

    private sealed class HitTestStrokeSink : IStrokeSink
    {
        private readonly Point _point;
        private readonly double _toleranceSquared;

        internal HitTestStrokeSink(Point point, double tolerance)
        {
            _point = point;
            _toleranceSquared = tolerance * tolerance;
        }

        internal bool Contains { get; private set; }

        public void AddPolygon(Point[] points, bool forward)
        {
            if (Contains)
            {
                return;
            }

            int winding = 0;
            for (int pointIndex = 0; pointIndex < points.Length; pointIndex++)
            {
                Point start = points[pointIndex];
                Point end = points[(pointIndex + 1) % points.Length];
                if (DistanceSquaredToSegment(_point, start, end) <= _toleranceSquared)
                {
                    Contains = true;
                    return;
                }

                double side = Cross(start, end, _point);
                if (start.Y <= _point.Y)
                {
                    if (end.Y > _point.Y && side > 0)
                    {
                        winding++;
                    }
                }
                else if (end.Y <= _point.Y && side < 0)
                {
                    winding--;
                }
            }

            Contains = winding != 0;
        }

        private static double DistanceSquaredToSegment(Point point, Point start, Point end)
        {
            double deltaX = end.X - start.X;
            double deltaY = end.Y - start.Y;
            double lengthSquared = deltaX * deltaX + deltaY * deltaY;
            if (lengthSquared <= DEGENERATE_LENGTH_SQUARED)
            {
                return SquaredDistance(point, start);
            }

            double parameter = ((point.X - start.X) * deltaX + (point.Y - start.Y) * deltaY) / lengthSquared;
            parameter = Math.Clamp(parameter, 0, 1);
            Point nearest = new(start.X + parameter * deltaX, start.Y + parameter * deltaY);
            return SquaredDistance(point, nearest);
        }

        private static double SquaredDistance(Point first, Point second)
        {
            double deltaX = first.X - second.X;
            double deltaY = first.Y - second.Y;
            return deltaX * deltaX + deltaY * deltaY;
        }

        private static double Cross(Point start, Point end, Point point) =>
            (end.X - start.X) * (point.Y - start.Y) - (point.X - start.X) * (end.Y - start.Y);
    }

    private readonly struct StrokeRun
    {
        internal StrokeRun(List<Point> points, bool isClosed)
        {
            Points = points;
            IsClosed = isClosed;
        }

        internal List<Point> Points { get; }

        internal bool IsClosed { get; }
    }
}
