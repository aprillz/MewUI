using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using System.Linq;

namespace Svg;

public partial class SvgLinearGradientServer
{
    protected override Brush? CreateBrush(SvgVisualElement styleOwner, ISvgRenderer renderer, float opacity, bool forStroke)
    {
        bool usesObjectBounds = GradientUnits == SvgCoordinateUnits.ObjectBoundingBox;
        try
        {
            if (usesObjectBounds)
                renderer.SetBoundable(styleOwner);

            var points = new[]
            {
                new Point(
                    NormalizeUnit(X1).ToDeviceValue(renderer, UnitRenderingType.Horizontal, this),
                    NormalizeUnit(Y1).ToDeviceValue(renderer, UnitRenderingType.Vertical, this)),
                new Point(
                    NormalizeUnit(X2).ToDeviceValue(renderer, UnitRenderingType.Horizontal, this),
                    NormalizeUnit(Y2).ToDeviceValue(renderer, UnitRenderingType.Vertical, this))
            };

            var bounds = renderer.GetBoundable().Bounds;
            if (bounds.Width <= 0 || bounds.Height <= 0 ||
                (points[0].X == points[1].X && points[0].Y == points[1].Y))
            {
                return GetCallback != null ? GetCallback().GetBrush(styleOwner, renderer, opacity, forStroke) : null;
            }

            var effectiveTransform = GetEffectiveGradientTransform(bounds, usesObjectBounds);
            points[0] = TransformPoint(points[0], effectiveTransform);
            points[1] = TransformPoint(points[1], effectiveTransform);

            points[0] = new Point(Math.Round(points[0].X, 4), Math.Round(points[0].Y, 4));
            points[1] = new Point(Math.Round(points[1].X, 4), Math.Round(points[1].Y, 4));

            if (usesObjectBounds)
            {
                var midPoint = new Point((points[0].X + points[1].X) / 2.0, (points[0].Y + points[1].Y) / 2.0);
                var dy = points[1].Y - points[0].Y;
                var dx = points[1].X - points[0].X;
                var x1 = points[0].X;
                var y2 = points[1].Y;

                if (dx != 0f && dy != 0f)
                {
                    var startX = ((dy * dx * (midPoint.Y - y2) + Math.Pow(dx, 2) * midPoint.X + Math.Pow(dy, 2) * x1) /
                                         (Math.Pow(dx, 2) + Math.Pow(dy, 2)));
                    var endY = dy * (startX - x1) / dx + y2;
                    points[0] = new Point(startX, midPoint.Y + (midPoint.Y - endY));
                    points[1] = new Point(midPoint.X + (midPoint.X - startX), endY);
                }
            }

            var effectiveStart = points[0];
            var effectiveEnd = points[1];
            if (PointsToMove(styleOwner, points[0], points[1]) > LinePoints.None)
            {
                var expansion = ExpandGradient(styleOwner, points[0], points[1]);
                effectiveStart = expansion.StartPoint;
                effectiveEnd = expansion.EndPoint;
            }

            var stops = CalculateColorStops(styleOwner, renderer, opacity, points[0], effectiveStart, points[1], effectiveEnd);
            return new LinearGradientBrush(
                new Point(effectiveStart.X, effectiveStart.Y),
                new Point(effectiveEnd.X, effectiveEnd.Y),
                stops,
                MewSvgPathUtilities.ToSpreadMethod(SpreadMethod),
                Aprillz.MewUI.Rendering.GradientUnits.UserSpaceOnUse);
        }
        finally
        {
            if (usesObjectBounds)
                renderer.PopBoundable();
        }
    }

    private LinePoints PointsToMove(ISvgBoundable boundable, Point specifiedStart, Point specifiedEnd)
    {
        var bounds = boundable.Bounds;
        if (specifiedStart.X == specifiedEnd.X)
        {
            return (bounds.Top < specifiedStart.Y && specifiedStart.Y < bounds.Bottom ? LinePoints.Start : LinePoints.None) |
                   (bounds.Top < specifiedEnd.Y && specifiedEnd.Y < bounds.Bottom ? LinePoints.End : LinePoints.None);
        }

        if (specifiedStart.Y == specifiedEnd.Y)
        {
            return (bounds.Left < specifiedStart.X && specifiedStart.X < bounds.Right ? LinePoints.Start : LinePoints.None) |
                   (bounds.Left < specifiedEnd.X && specifiedEnd.X < bounds.Right ? LinePoints.End : LinePoints.None);
        }

        return (Contains(bounds, specifiedStart) ? LinePoints.Start : LinePoints.None) |
               (Contains(bounds, specifiedEnd) ? LinePoints.End : LinePoints.None);
    }

    private GradientPoints ExpandGradient(ISvgBoundable boundable, Point specifiedStart, Point specifiedEnd)
    {
        var pointsToMove = PointsToMove(boundable, specifiedStart, specifiedEnd);
        if (pointsToMove == LinePoints.None)
            return new GradientPoints(specifiedStart, specifiedEnd);

        var bounds = boundable.Bounds;
        var effectiveStart = specifiedStart;
        var effectiveEnd = specifiedEnd;
        var intersectionPoints = CandidateIntersections(bounds, specifiedStart, specifiedEnd);
        if (intersectionPoints.Count < 2)
            return new GradientPoints(specifiedStart, specifiedEnd);

        if (!(Math.Sign(intersectionPoints[1].X - intersectionPoints[0].X) == Math.Sign(specifiedEnd.X - specifiedStart.X) &&
              Math.Sign(intersectionPoints[1].Y - intersectionPoints[0].Y) == Math.Sign(specifiedEnd.Y - specifiedStart.Y)))
        {
            intersectionPoints.Reverse();
        }

        if ((pointsToMove & LinePoints.Start) != 0) effectiveStart = intersectionPoints[0];
        if ((pointsToMove & LinePoints.End) != 0) effectiveEnd = intersectionPoints[1];

        switch (SpreadMethod)
        {
            case SvgGradientSpreadMethod.Reflect:
            case SvgGradientSpreadMethod.Repeat:
                    var specifiedLength = CalculateDistance(specifiedStart, specifiedEnd);
                    var unitVector = new Point(
                        (specifiedEnd.X - specifiedStart.X) / specifiedLength,
                        (specifiedEnd.Y - specifiedStart.Y) / specifiedLength);
                    var opposite = new Point(-unitVector.X, -unitVector.Y);

                var startExtend = Math.Ceiling(CalculateDistance(effectiveStart, specifiedStart) / specifiedLength) * specifiedLength;
                effectiveStart = MovePointAlongVector(specifiedStart, opposite, startExtend);
                var endExtend = Math.Ceiling(CalculateDistance(effectiveEnd, specifiedEnd) / specifiedLength) * specifiedLength;
                effectiveEnd = MovePointAlongVector(specifiedEnd, unitVector, endExtend);
                break;
        }

        return new GradientPoints(effectiveStart, effectiveEnd);
    }

    private List<Point> CandidateIntersections(Rect bounds, Point p1, Point p2)
    {
        var results = new List<Point>();
        if (Math.Round(Math.Abs(p1.Y - p2.Y), 4) == 0)
        {
            results.Add(new Point(bounds.Left, p1.Y));
            results.Add(new Point(bounds.Right, p1.Y));
        }
        else if (Math.Round(Math.Abs(p1.X - p2.X), 4) == 0)
        {
            results.Add(new Point(p1.X, bounds.Top));
            results.Add(new Point(p1.X, bounds.Bottom));
        }
        else
        {
            Point candidate;
            if ((p1.X == bounds.Left || p1.X == bounds.Right) && (p1.Y == bounds.Top || p1.Y == bounds.Bottom))
            {
                results.Add(p1);
            }
            else
            {
                candidate = new Point(bounds.Left, (p2.Y - p1.Y) / (p2.X - p1.X) * (bounds.Left - p1.X) + p1.Y);
                if (bounds.Top <= candidate.Y && candidate.Y <= bounds.Bottom && !ContainsPoint(results, candidate))
                    results.Add(candidate);
                candidate = new Point(bounds.Right, (p2.Y - p1.Y) / (p2.X - p1.X) * (bounds.Right - p1.X) + p1.Y);
                if (bounds.Top <= candidate.Y && candidate.Y <= bounds.Bottom && !ContainsPoint(results, candidate))
                    results.Add(candidate);
            }

            if ((p2.X == bounds.Left || p2.X == bounds.Right) && (p2.Y == bounds.Top || p2.Y == bounds.Bottom))
            {
                results.Add(p2);
            }
            else
            {
                candidate = new Point((bounds.Top - p1.Y) / (p2.Y - p1.Y) * (p2.X - p1.X) + p1.X, bounds.Top);
                if (bounds.Left <= candidate.X && candidate.X <= bounds.Right && !ContainsPoint(results, candidate))
                    results.Add(candidate);
                candidate = new Point((bounds.Bottom - p1.Y) / (p2.Y - p1.Y) * (p2.X - p1.X) + p1.X, bounds.Bottom);
                if (bounds.Left <= candidate.X && candidate.X <= bounds.Right && !ContainsPoint(results, candidate))
                    results.Add(candidate);
            }
        }

        return results;
    }

    private IReadOnlyList<GradientStop> CalculateColorStops(
        SvgVisualElement owner,
        ISvgRenderer renderer,
        float opacity,
        Point specifiedStart,
        Point effectiveStart,
        Point specifiedEnd,
        Point effectiveEnd)
    {
        float startExtend;
        float endExtend;
        var colorStops = GetColorStops(renderer, opacity, false)
            .Select(x => new GradientStop(x.Offset, x.Color))
            .ToList();

        var startDelta = CalculateDistance(specifiedStart, effectiveStart);
        var endDelta = CalculateDistance(specifiedEnd, effectiveEnd);
        if (!(startDelta > 0) && !(endDelta > 0))
            return colorStops;

        var specifiedLength = CalculateDistance(specifiedStart, specifiedEnd);
        var unitVector = new Point(
            (specifiedEnd.X - specifiedStart.X) / specifiedLength,
            (specifiedEnd.Y - specifiedStart.Y) / specifiedLength);
        var effectiveLength = CalculateDistance(effectiveStart, effectiveEnd);

        switch (SpreadMethod)
        {
            case SvgGradientSpreadMethod.Reflect:
                startExtend = (float)Math.Ceiling(CalculateDistance(effectiveStart, specifiedStart) / specifiedLength);
                endExtend = (float)Math.Ceiling(CalculateDistance(effectiveEnd, specifiedEnd) / specifiedLength);
                {
                    var colors = colorStops.Select(x => x.Color).ToList();
                    var positions = colorStops.Select(x => x.Offset + startExtend).ToList();

                    for (var i = 0; i < startExtend; i++)
                    {
                        if (i % 2 == 0)
                        {
                            for (var j = 1; j < colorStops.Count; j++)
                            {
                                positions.Insert(0, (startExtend - 1 - i) + 1 - colorStops[j].Offset);
                                colors.Insert(0, colorStops[j].Color);
                            }
                        }
                        else
                        {
                            for (var j = 0; j < colorStops.Count - 1; j++)
                            {
                                positions.Insert(j, (startExtend - 1 - i) + colorStops[j].Offset);
                                colors.Insert(j, colorStops[j].Color);
                            }
                        }
                    }

                    for (var i = 0; i < endExtend; i++)
                    {
                        if (i % 2 == 0)
                        {
                            int insertPos = positions.Count;
                            for (var j = 0; j < colorStops.Count - 1; j++)
                            {
                                positions.Insert(insertPos, (startExtend + 1 + i) + 1 - colorStops[j].Offset);
                                colors.Insert(insertPos, colorStops[j].Color);
                            }
                        }
                        else
                        {
                            for (var j = 1; j < colorStops.Count; j++)
                            {
                                positions.Add((startExtend + 1 + i) + colorStops[j].Offset);
                                colors.Add(colorStops[j].Color);
                            }
                        }
                    }

                    return ToGradientStops(positions.Select(p => p / (startExtend + 1 + endExtend)).ToList(), colors);
                }

            case SvgGradientSpreadMethod.Repeat:
                startExtend = (float)Math.Ceiling(CalculateDistance(effectiveStart, specifiedStart) / specifiedLength);
                endExtend = (float)Math.Ceiling(CalculateDistance(effectiveEnd, specifiedEnd) / specifiedLength);
                {
                    var colors = new List<Color>();
                    var positions = new List<double>();
                    for (int i = 0; i < startExtend + endExtend + 1; i++)
                    {
                        foreach (var stop in colorStops)
                        {
                            positions.Add((i + stop.Offset * 0.9999) / (startExtend + endExtend + 1));
                            colors.Add(stop.Color);
                        }
                    }

                    positions[^1] = 1.0;
                    return ToGradientStops(positions, colors);
                }

            default:
                for (var i = 0; i < colorStops.Count; i++)
                {
                    var originalPoint = MovePointAlongVector(specifiedStart, unitVector, specifiedLength * colorStops[i].Offset);
                    var distanceFromEffectiveStart = CalculateDistance(effectiveStart, originalPoint);
                    colorStops[i] = new GradientStop(
                        Math.Round(Math.Clamp(distanceFromEffectiveStart / effectiveLength, 0.0, 1.0), 5),
                        colorStops[i].Color);
                }

                if (startDelta > 0)
                    colorStops.Insert(0, new GradientStop(0.0, colorStops[0].Color));
                if (endDelta > 0)
                    colorStops.Add(new GradientStop(1.0, colorStops[^1].Color));
                return colorStops;
        }
    }

    private static IReadOnlyList<GradientStop> ToGradientStops(IReadOnlyList<double> positions, IReadOnlyList<Color> colors)
    {
        var result = new List<GradientStop>(positions.Count);
        for (int i = 0; i < positions.Count; i++)
            result.Add(new GradientStop(Math.Clamp(positions[i], 0.0, 1.0), colors[i]));
        return result;
    }

    private static Point MovePointAlongVector(Point start, Point unitVector, double distance)
        => new(start.X + unitVector.X * distance, start.Y + unitVector.Y * distance);

    private static bool ContainsPoint(List<Point> points, Point candidate)
    {
        foreach (var point in points)
        {
            if (point == candidate)
                return true;
            if (Math.Round(point.X) == Math.Round(candidate.X) && Math.Round(point.Y) == Math.Round(candidate.Y))
                return true;
        }

        return false;
    }

    private static bool Contains(Rect rect, Point point)
        => rect.Left <= point.X && point.X <= rect.Right && rect.Top <= point.Y && point.Y <= rect.Bottom;

    private static double CalculateDistance(Point p1, Point p2)
    {
        var dx = p2.X - p1.X;
        var dy = p2.Y - p1.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    [Flags]
    private enum LinePoints
    {
        None = 0,
        Start = 1,
        End = 2
    }

    private readonly struct GradientPoints
    {
        public GradientPoints(Point startPoint, Point endPoint)
        {
            StartPoint = startPoint;
            EndPoint = endPoint;
        }

        public Point StartPoint { get; }
        public Point EndPoint { get; }
    }
}
