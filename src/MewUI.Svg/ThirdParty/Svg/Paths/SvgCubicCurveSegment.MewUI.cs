using System.Drawing;

using Aprillz.MewUI.Rendering;

namespace Svg.Pathing;

public sealed partial class SvgCubicCurveSegment
{
    public override PointF AddToPath(PathGeometry graphicsPath, PointF start, SvgPathSegmentList parent)
    {
        var firstControlPoint = FirstControlPoint;
        if (float.IsNaN(firstControlPoint.X) || float.IsNaN(firstControlPoint.Y))
        {
            var commands = graphicsPath.Commands;
            if (commands.Length > 0 && commands[^1].Type == PathCommandType.BezierTo)
            {
                var prev = commands[^1];
                firstControlPoint = Reflect(new PointF((float)prev.X1, (float)prev.Y1), start);
            }
            else
            {
                firstControlPoint = start;
            }
        }
        else
        {
            firstControlPoint = ToAbsolute(firstControlPoint, IsRelative, start);
        }

        var secondControlPoint = ToAbsolute(SecondControlPoint, IsRelative, start);
        var end = ToAbsolute(End, IsRelative, start);
        graphicsPath.BezierTo(firstControlPoint.X, firstControlPoint.Y, secondControlPoint.X, secondControlPoint.Y, end.X, end.Y);
        return end;
    }
}
