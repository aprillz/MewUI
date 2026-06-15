using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Svg;

internal sealed class PathStatistics
{
    private const double GqBreakTwoPoint = 0.57735026918962573;
    private const double GqBreakThreePoint = 0.7745966692414834;
    private const double GqBreakFourPoint01 = 0.33998104358485631;
    private const double GqBreakFourPoint02 = 0.86113631159405257;
    private const double GqWeightFourPoint01 = 0.65214515486254621;
    private const double GqWeightFourPoint02 = 0.34785484513745385;

    private readonly List<ISegment> _segments = new();
    private readonly double _totalLength;

    public PathStatistics(PathGeometry path)
    {
        Point? figureStart = null;
        Point? previousPoint = null;
        double totalLength = 0;

        foreach (var command in path.Commands)
        {
            ISegment? segment = null;
            switch (command.Type)
            {
                case PathCommandType.MoveTo:
                    previousPoint = new Point(command.X0, command.Y0);
                    figureStart = previousPoint;
                    continue;

                case PathCommandType.LineTo:
                    if (previousPoint is { } lineStart)
                    {
                        var lineEnd = new Point(command.X0, command.Y0);
                        segment = new LineSegment(lineStart, lineEnd);
                        previousPoint = lineEnd;
                    }
                    break;

                case PathCommandType.BezierTo:
                    if (previousPoint is { } bezierStart)
                    {
                        var c1 = new Point(command.X0, command.Y0);
                        var c2 = new Point(command.X1, command.Y1);
                        var bezierEnd = new Point(command.X2, command.Y2);
                        segment = new CubicBezierSegment(bezierStart, c1, c2, bezierEnd);
                        previousPoint = bezierEnd;
                    }
                    break;

                case PathCommandType.Close:
                    if (previousPoint is { } closeStart && figureStart is { } closeEnd)
                    {
                        segment = new LineSegment(closeStart, closeEnd);
                        previousPoint = closeEnd;
                    }
                    break;
            }

            if (segment is not null)
            {
                segment.StartOffset = totalLength;
                _segments.Add(segment);
                totalLength += segment.Length;
            }
        }

        _totalLength = totalLength;
    }

    public double TotalLength => _totalLength;

    public void LocationAngleAtOffset(double offset, out Point point, out double angle)
    {
        _segments[BinarySearchForSegment(offset, 0, _segments.Count - 1)].LocationAngleAtOffset(offset, out point, out angle);
    }

    public bool OffsetOnPath(double offset)
    {
        if (_segments.Count == 0)
        {
            return false;
        }

        var segment = _segments[BinarySearchForSegment(offset, 0, _segments.Count - 1)];
        offset -= segment.StartOffset;
        return offset >= 0 && offset <= segment.Length;
    }

    private int BinarySearchForSegment(double offset, int first, int last)
    {
        if (last == first)
        {
            return first;
        }

        if (last - first == 1)
        {
            return offset >= _segments[last].StartOffset ? last : first;
        }

        var mid = (last + first) / 2;
        return offset < _segments[mid].StartOffset
            ? BinarySearchForSegment(offset, first, mid)
            : BinarySearchForSegment(offset, mid, last);
    }

    private interface ISegment
    {
        double StartOffset { get; set; }

        double Length { get; }

        void LocationAngleAtOffset(double offset, out Point point, out double rotation);
    }

    private sealed class LineSegment : ISegment
    {
        private readonly double _length;
        private readonly double _rotation;
        private readonly Point _start;
        private readonly Point _end;

        public LineSegment(Point start, Point end)
        {
            _start = start;
            _end = end;
            _length = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
            _rotation = Math.Atan2(end.Y - start.Y, end.X - start.X) * 180.0 / Math.PI;
        }

        public double StartOffset { get; set; }

        public double Length => _length;

        public void LocationAngleAtOffset(double offset, out Point point, out double rotation)
        {
            offset -= StartOffset;
            point = new Point(
                _start.X + (offset / _length) * (_end.X - _start.X),
                _start.Y + (offset / _length) * (_end.Y - _start.Y));
            rotation = _rotation;
        }
    }

    private sealed class CubicBezierSegment : ISegment
    {
        private readonly Point _p0;
        private readonly Point _p1;
        private readonly Point _p2;
        private readonly Point _p3;
        private readonly double _length;
        private readonly Func<double, double> _integral;
        private readonly SortedList<double, double> _lengths = new();

        public CubicBezierSegment(Point p0, Point p1, Point p2, Point p3)
        {
            _p0 = p0;
            _p1 = p1;
            _p2 = p2;
            _p3 = p3;
            _integral = t => CubicBezierArcLengthIntegrand(_p0, _p1, _p2, _p3, t);
            _length = GetLength(0, 1, 0.00000001);
            _lengths.Add(0, 0);
            _lengths.Add(_length, 1);
        }

        public double StartOffset { get; set; }

        public double Length => _length;

        public void LocationAngleAtOffset(double offset, out Point point, out double rotation)
        {
            offset -= StartOffset;
            var t = BinarySearchForParam(offset, 0, _lengths.Count - 1);
            point = CubicBezierCurve(_p0, _p1, _p2, _p3, t);
            var deriv = CubicBezierDerivative(_p0, _p1, _p2, _p3, t);
            rotation = Math.Atan2(deriv.Y, deriv.X) * 180.0 / Math.PI;
        }

        private double GetLength(double left, double right, double epsilon)
        {
            var fullInt = GaussianQuadrature(_integral, left, right, 4);
            return Subdivide(left, right, fullInt, 0, epsilon);
        }

        private double Subdivide(double left, double right, double fullInt, double totalLength, double epsilon)
        {
            var mid = (left + right) / 2;
            var leftValue = GaussianQuadrature(_integral, left, mid, 4);
            var rightValue = GaussianQuadrature(_integral, mid, right, 4);
            if (Math.Abs(fullInt - (leftValue + rightValue)) > epsilon)
            {
                var leftSub = Subdivide(left, mid, leftValue, totalLength, epsilon / 2.0);
                totalLength += leftSub;
                AddElementToTable(mid, totalLength);
                return Subdivide(mid, right, rightValue, totalLength, epsilon / 2.0) + leftSub;
            }

            return leftValue + rightValue;
        }

        private void AddElementToTable(double position, double totalLength)
        {
            _lengths.Add(totalLength, position);
        }

        private double BinarySearchForParam(double length, int first, int last)
        {
            if (last == first)
            {
                return _lengths.Values[last];
            }

            if (last - first == 1)
            {
                return _lengths.Values[first] + (_lengths.Values[last] - _lengths.Values[first]) *
                    (length - _lengths.Keys[first]) / (_lengths.Keys[last] - _lengths.Keys[first]);
            }

            var mid = (last + first) / 2;
            return length < _lengths.Keys[mid]
                ? BinarySearchForParam(length, first, mid)
                : BinarySearchForParam(length, mid, last);
        }

        private static double GaussianQuadrature(Func<double, double> func, double a, double b, int points)
        {
            return points switch
            {
                1 => (b - a) * func((a + b) / 2.0),
                2 => (b - a) / 2.0 * (func((b - a) / 2.0 * -GqBreakTwoPoint + (a + b) / 2.0) +
                                      func((b - a) / 2.0 * GqBreakTwoPoint + (a + b) / 2.0)),
                3 => (b - a) / 2.0 * (5.0 / 9 * func((b - a) / 2.0 * -GqBreakThreePoint + (a + b) / 2.0) +
                                      8.0 / 9 * func((a + b) / 2.0) +
                                      5.0 / 9 * func((b - a) / 2.0 * GqBreakThreePoint + (a + b) / 2.0)),
                4 => (b - a) / 2.0 * (GqWeightFourPoint01 * func((b - a) / 2.0 * -GqBreakFourPoint01 + (a + b) / 2.0) +
                                      GqWeightFourPoint01 * func((b - a) / 2.0 * GqBreakFourPoint01 + (a + b) / 2.0) +
                                      GqWeightFourPoint02 * func((b - a) / 2.0 * -GqBreakFourPoint02 + (a + b) / 2.0) +
                                      GqWeightFourPoint02 * func((b - a) / 2.0 * GqBreakFourPoint02 + (a + b) / 2.0)),
                _ => throw new NotSupportedException(),
            };
        }

        private static Point CubicBezierCurve(Point p0, Point p1, Point p2, Point p3, double t)
        {
            return new Point(
                Math.Pow(1 - t, 3) * p0.X + 3 * Math.Pow(1 - t, 2) * t * p1.X +
                3 * (1 - t) * Math.Pow(t, 2) * p2.X + Math.Pow(t, 3) * p3.X,
                Math.Pow(1 - t, 3) * p0.Y + 3 * Math.Pow(1 - t, 2) * t * p1.Y +
                3 * (1 - t) * Math.Pow(t, 2) * p2.Y + Math.Pow(t, 3) * p3.Y);
        }

        private static Point CubicBezierDerivative(Point p0, Point p1, Point p2, Point p3, double t)
        {
            return new Point(
                3 * Math.Pow(1 - t, 2) * (p1.X - p0.X) + 6 * (1 - t) * t * (p2.X - p1.X) + 3 * Math.Pow(t, 2) * (p3.X - p2.X),
                3 * Math.Pow(1 - t, 2) * (p1.Y - p0.Y) + 6 * (1 - t) * t * (p2.Y - p1.Y) + 3 * Math.Pow(t, 2) * (p3.Y - p2.Y));
        }

        private static double CubicBezierArcLengthIntegrand(Point p0, Point p1, Point p2, Point p3, double t)
        {
            return Math.Sqrt(
                Math.Pow(3 * Math.Pow(1 - t, 2) * (p1.X - p0.X) + 6 * (1 - t) * t * (p2.X - p1.X) + 3 * Math.Pow(t, 2) * (p3.X - p2.X), 2) +
                Math.Pow(3 * Math.Pow(1 - t, 2) * (p1.Y - p0.Y) + 6 * (1 - t) * t * (p2.Y - p1.Y) + 3 * Math.Pow(t, 2) * (p3.Y - p2.Y), 2));
        }
    }
}
