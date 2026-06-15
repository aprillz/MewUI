using System.Drawing;

using Aprillz.MewUI.Rendering;

namespace Svg.Pathing;

public sealed partial class SvgQuadraticCurveSegment
{
    private static PointF CalculateControlPoint(PointF start, PointF firstControlPoint)
    {
        var x1 = (firstControlPoint.X * 3 - start.X) / 2;
        var y1 = (firstControlPoint.Y * 3 - start.Y) / 2;
        return new PointF(x1, y1);
    }

    public override PointF AddToPath(PathGeometry graphicsPath, PointF start, SvgPathSegmentList parent)
    {
        var controlPoint = ControlPoint;
        if (float.IsNaN(controlPoint.X) || float.IsNaN(controlPoint.Y))
        {
            var commands = graphicsPath.Commands;
            if (commands.Length > 0 && commands[^1].Type == PathCommandType.BezierTo && commands.Length >= 2)
            {
                var prevMoveOrEnd = commands.Length >= 2 ? commands[^2] : default;
                PointF prevStart = prevMoveOrEnd.Type switch
                {
                    PathCommandType.MoveTo or PathCommandType.LineTo => new PointF((float)prevMoveOrEnd.X0, (float)prevMoveOrEnd.Y0),
                    PathCommandType.BezierTo => new PointF((float)prevMoveOrEnd.X2, (float)prevMoveOrEnd.Y2),
                    _ => start
                };

                var prevBezier = commands[^1];
                var prevFirstControlPoint = new PointF((float)prevBezier.X0, (float)prevBezier.Y0);
                var prevControlPoint = CalculateControlPoint(prevStart, prevFirstControlPoint);
                controlPoint = Reflect(prevControlPoint, start);
            }
            else
            {
                controlPoint = start;
            }
        }
        else
        {
            controlPoint = ToAbsolute(controlPoint, IsRelative, start);
        }

        var end = ToAbsolute(End, IsRelative, start);
        graphicsPath.QuadTo(controlPoint.X, controlPoint.Y, end.X, end.Y);
        return end;
    }
}
