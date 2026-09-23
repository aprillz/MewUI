namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// The area a stroke covers. Past the half width a centred stroke puts outside every edge, a miter join
/// reaches out to its tip and a square cap to the far corners of its square.
/// </summary>
internal static class StrokeInk
{
    // Below this squared length a control point stands on its end point and gives no direction.
    private const double DEGENERATE_LENGTH_SQUARED = 1e-18;

    // Segments this close to one direction meet without a corner to miter.
    private const double STRAIGHT_JOIN_COSINE = 1 - 1e-9;

    /// <summary>The area the stroke of <paramref name="path"/> covers, drawn with the given width and style.</summary>
    internal static Rect OfPath(PathGeometry path, double thickness, StrokeStyle style)
    {
        if (path.IsEmpty)
        {
            return default;
        }

        double halfWidth = Math.Max(0, thickness) * 0.5;
        var accumulator = new Extent(path.GetBounds().Inflate(halfWidth, halfWidth));
        if (halfWidth == 0)
        {
            return accumulator.Result;
        }

        bool squareCaps = style.LineCap == StrokeLineCap.Square;
        if (squareCaps && style.IsDashed)
        {
            // Every dash ends in a cap somewhere along the path, facing whichever way the path runs there.
            double capReach = halfWidth * Math.Sqrt(2);
            accumulator.Add(path.GetBounds().Inflate(capReach, capReach));
            squareCaps = false;
        }

        var walker = new Walker(halfWidth, style.LineJoin == StrokeLineJoin.Miter, Math.Max(1, style.MiterLimit), squareCaps);
        foreach (var command in path.Commands)
        {
            switch (command.Type)
            {
                case PathCommandType.MoveTo:
                    walker.EndOpenFigure(ref accumulator);
                    walker.Start(command.X0, command.Y0);
                    break;
                case PathCommandType.LineTo:
                    walker.LineTo(command.X0, command.Y0, ref accumulator);
                    break;
                case PathCommandType.BezierTo:
                    walker.BezierTo(command, ref accumulator);
                    break;
                case PathCommandType.Close:
                    walker.Close(ref accumulator);
                    break;
            }
        }

        walker.EndOpenFigure(ref accumulator);
        return accumulator.Result;
    }

    /// <summary>The area a straight line covers, drawn with the given width and style.</summary>
    internal static Rect OfLine(Point start, Point end, double thickness, StrokeStyle style)
    {
        double halfWidth = Math.Max(0, thickness) * 0.5;
        var accumulator = new Extent(new Rect(
            Math.Min(start.X, end.X) - halfWidth,
            Math.Min(start.Y, end.Y) - halfWidth,
            Math.Abs(end.X - start.X) + halfWidth * 2,
            Math.Abs(end.Y - start.Y) + halfWidth * 2));

        // A dash along a straight line caps inside the square the end caps reach, so only they count.
        if (style.LineCap == StrokeLineCap.Square && halfWidth > 0)
        {
            double directionX = end.X - start.X;
            double directionY = end.Y - start.Y;
            double length = Math.Sqrt(directionX * directionX + directionY * directionY);
            if (length > 0)
            {
                directionX /= length;
                directionY /= length;
                AddSquareCap(end.X, end.Y, directionX, directionY, halfWidth, ref accumulator);
                AddSquareCap(start.X, start.Y, -directionX, -directionY, halfWidth, ref accumulator);
            }
        }

        return accumulator.Result;
    }

    private static void AddSquareCap(double x, double y, double outwardX, double outwardY, double halfWidth, ref Extent accumulator)
    {
        // The far corners of the cap stand half a width out along the end and half a width to either side.
        double alongX = outwardX * halfWidth;
        double alongY = outwardY * halfWidth;
        double sideX = -outwardY * halfWidth;
        double sideY = outwardX * halfWidth;
        accumulator.Add(x + alongX + sideX, y + alongY + sideY);
        accumulator.Add(x + alongX - sideX, y + alongY - sideY);
    }

    private static bool TryDirection(double fromX, double fromY, double toX, double toY, out double directionX, out double directionY)
    {
        directionX = toX - fromX;
        directionY = toY - fromY;
        double lengthSquared = directionX * directionX + directionY * directionY;
        if (lengthSquared <= DEGENERATE_LENGTH_SQUARED)
        {
            return false;
        }

        double length = Math.Sqrt(lengthSquared);
        directionX /= length;
        directionY /= length;
        return true;
    }

    private struct Extent(Rect initial)
    {
        private double _left = initial.X;
        private double _top = initial.Y;
        private double _right = initial.Right;
        private double _bottom = initial.Bottom;

        internal readonly Rect Result => new(_left, _top, _right - _left, _bottom - _top);

        internal void Add(double x, double y)
        {
            _left = Math.Min(_left, x);
            _top = Math.Min(_top, y);
            _right = Math.Max(_right, x);
            _bottom = Math.Max(_bottom, y);
        }

        internal void Add(Rect rect)
        {
            Add(rect.X, rect.Y);
            Add(rect.Right, rect.Bottom);
        }
    }

    /// <summary>Follows the figures of a path and adds what its joins and caps reach past the half width.</summary>
    private struct Walker(double halfWidth, bool miterJoins, double miterLimit, bool squareCaps)
    {
        private double _startX;
        private double _startY;
        private double _currentX;
        private double _currentY;

        // Direction the figure leaves its start in, and the one it arrives at the current point in.
        private double _firstX;
        private double _firstY;
        private double _lastX;
        private double _lastY;
        private bool _hasSegment;

        internal void Start(double x, double y)
        {
            _startX = _currentX = x;
            _startY = _currentY = y;
            _hasSegment = false;
        }

        internal void LineTo(double x, double y, ref Extent accumulator)
        {
            if (TryDirection(_currentX, _currentY, x, y, out double directionX, out double directionY))
            {
                Segment(directionX, directionY, directionX, directionY, ref accumulator);
            }

            _currentX = x;
            _currentY = y;
        }

        internal void BezierTo(in PathCommand command, ref Extent accumulator)
        {
            // A control point standing on its end point gives no direction, so the next one along does.
            bool leaves =
                TryDirection(_currentX, _currentY, command.X0, command.Y0, out double leaveX, out double leaveY) ||
                TryDirection(_currentX, _currentY, command.X1, command.Y1, out leaveX, out leaveY) ||
                TryDirection(_currentX, _currentY, command.X2, command.Y2, out leaveX, out leaveY);
            bool arrives =
                TryDirection(command.X1, command.Y1, command.X2, command.Y2, out double arriveX, out double arriveY) ||
                TryDirection(command.X0, command.Y0, command.X2, command.Y2, out arriveX, out arriveY) ||
                TryDirection(_currentX, _currentY, command.X2, command.Y2, out arriveX, out arriveY);
            if (leaves && arrives)
            {
                Segment(leaveX, leaveY, arriveX, arriveY, ref accumulator);
            }

            _currentX = command.X2;
            _currentY = command.Y2;
        }

        internal void Close(ref Extent accumulator)
        {
            LineTo(_startX, _startY, ref accumulator);
            if (_hasSegment)
            {
                Join(_startX, _startY, _lastX, _lastY, _firstX, _firstY, ref accumulator);
            }

            // A closed figure has no ends to cap; drawing on starts a new figure where this one began.
            Start(_startX, _startY);
        }

        internal void EndOpenFigure(ref Extent accumulator)
        {
            if (_hasSegment && squareCaps)
            {
                AddSquareCap(_currentX, _currentY, _lastX, _lastY, halfWidth, ref accumulator);
                AddSquareCap(_startX, _startY, -_firstX, -_firstY, halfWidth, ref accumulator);
            }

            _hasSegment = false;
        }

        private void Segment(double leaveX, double leaveY, double arriveX, double arriveY, ref Extent accumulator)
        {
            if (_hasSegment)
            {
                Join(_currentX, _currentY, _lastX, _lastY, leaveX, leaveY, ref accumulator);
            }
            else
            {
                _firstX = leaveX;
                _firstY = leaveY;
                _hasSegment = true;
            }

            _lastX = arriveX;
            _lastY = arriveY;
        }

        private readonly void Join(double x, double y, double inX, double inY, double outX, double outY, ref Extent accumulator)
        {
            // A round or bevel join stays within half a width of its corner, which the inflated bounds hold.
            if (!miterJoins)
            {
                return;
            }

            double cosine = Math.Clamp(inX * outX + inY * outY, -1, 1);
            if (cosine >= STRAIGHT_JOIN_COSINE)
            {
                return;
            }

            // The tip stands 1 / cos(turn / 2) half widths out along the bisector of the outer side.
            double tipRatioSquared = 2 / (1 + cosine);
            if (double.IsInfinity(tipRatioSquared) || tipRatioSquared > miterLimit * miterLimit)
            {
                // Past the limit a backend bevels the corner or cuts the miter at the limit, both within it.
                double reach = miterLimit * halfWidth;
                accumulator.Add(new Rect(x - reach, y - reach, reach * 2, reach * 2));
                return;
            }

            double bisectorX = inX - outX;
            double bisectorY = inY - outY;
            double bisectorLength = Math.Sqrt(bisectorX * bisectorX + bisectorY * bisectorY);
            double tipDistance = Math.Sqrt(tipRatioSquared) * halfWidth;
            accumulator.Add(x + bisectorX / bisectorLength * tipDistance, y + bisectorY / bisectorLength * tipDistance);
        }
    }
}
