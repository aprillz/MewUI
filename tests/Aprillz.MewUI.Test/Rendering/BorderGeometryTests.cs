using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Rendering;

/// <summary>
/// The border region is what a Border paints when its background cannot overwrite the middle, so it
/// has to carry its own hole and its corners have to be arcs rather than shortcuts across them.
/// </summary>
[TestClass]
public sealed class BorderGeometryTests
{
    private const double TOLERANCE = 0.01;

    [TestMethod]
    public void TheBorderRegionEnclosesTheInnerContourSoTheMiddleStaysUnpainted()
    {
        var metrics = Metrics(new Thickness(4), new CornerRadius(0));
        var path = BorderGeometry.CreateBorderRegion(in metrics);

        Assert.AreEqual(FillRule.NonZero, path.FillRule,
            "the inner contour cancels the outer one only under the non-zero rule");
        Assert.AreEqual(2, CountFigures(path),
            "an outer contour on its own would paint the middle as well");
    }

    [TestMethod]
    public void ASideWithNoThicknessLeavesTheInnerContourAgainstThatEdge()
    {
        // Issue 228: a side set to 0 used to fill the whole box with the border colour.
        var metrics = Metrics(new Thickness(2, 0, 2, 0), new CornerRadius(0));
        var path = BorderGeometry.CreateBorderRegion(in metrics);

        Assert.AreEqual(2, CountFigures(path));
        Assert.AreEqual(new Rect(2, 0, 96, 100), metrics.InnerBounds,
            "the region left unpainted follows the thickness of each side");
    }

    [TestMethod]
    [DataRow(20.0)]
    [DataRow(8.0)]
    public void TheInnerContourRoundsItsCornersInsteadOfCuttingAcrossThem(double radius)
    {
        var metrics = Metrics(new Thickness(0), new CornerRadius(radius));
        var path = BorderGeometry.CreateBorderRegion(in metrics);

        // With no thickness the inner contour matches the outer one, so every corner of the reversed
        // contour can be checked against the circle it is meant to trace. A control point placed
        // against the direction of travel pulls the arc inward and fills the round with a diagonal.
        foreach ((Point start, Point end, Point centre) in ReversedCorners(radius))
        {
            var midpoint = MidpointOfArcEndingAt(path, start, end);
            double distance = Distance(midpoint, centre);
            Assert.AreEqual(radius, distance, TOLERANCE,
                $"the arc from {start} to {end} does not follow the corner");
        }
    }

    private static BorderRenderMetrics Metrics(Thickness thickness, CornerRadius radius)
        => new(new Rect(0, 0, 100, 100), dpiScale: 1, thickness, radius);

    /// <summary>The four corners of the reversed contour, as start, end and the centre they turn about.</summary>
    private static IEnumerable<(Point Start, Point End, Point Centre)> ReversedCorners(double radius)
    {
        double far = 100 - radius;
        yield return (new Point(radius, 0), new Point(0, radius), new Point(radius, radius));
        yield return (new Point(0, far), new Point(radius, 100), new Point(radius, far));
        yield return (new Point(far, 100), new Point(100, far), new Point(far, far));
        yield return (new Point(100, radius), new Point(far, 0), new Point(far, radius));
    }

    private static Point MidpointOfArcEndingAt(PathGeometry path, Point start, Point end)
    {
        var commands = path.Commands;
        var current = new Point(0, 0);
        for (int index = 0; index < commands.Length; index++)
        {
            var command = commands[index];
            switch (command.Type)
            {
                case PathCommandType.MoveTo:
                case PathCommandType.LineTo:
                    current = new Point(command.X0, command.Y0);
                    break;

                case PathCommandType.BezierTo:
                    var last = new Point(command.X2, command.Y2);
                    if (Distance(current, start) < TOLERANCE && Distance(last, end) < TOLERANCE)
                    {
                        return CubicMidpoint(
                            current,
                            new Point(command.X0, command.Y0),
                            new Point(command.X1, command.Y1),
                            last);
                    }

                    current = last;
                    break;
            }
        }

        Assert.Fail($"no arc runs from {start} to {end}");
        return default;
    }

    private static Point CubicMidpoint(Point start, Point control1, Point control2, Point end)
        => new(
            (start.X + (3 * control1.X) + (3 * control2.X) + end.X) / 8,
            (start.Y + (3 * control1.Y) + (3 * control2.Y) + end.Y) / 8);

    private static double Distance(Point left, Point right)
        => Math.Sqrt(((left.X - right.X) * (left.X - right.X)) + ((left.Y - right.Y) * (left.Y - right.Y)));

    private static int CountFigures(PathGeometry path)
    {
        int figures = 0;
        foreach (var command in path.Commands)
        {
            if (command.Type == PathCommandType.MoveTo)
            {
                figures++;
            }
        }

        return figures;
    }
}
