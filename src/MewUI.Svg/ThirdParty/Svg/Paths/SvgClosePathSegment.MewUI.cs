using System.Drawing;

using Aprillz.MewUI.Rendering;

namespace Svg.Pathing;

public sealed partial class SvgClosePathSegment
{
    public override PointF AddToPath(PathGeometry graphicsPath, PointF start, SvgPathSegmentList parent)
    {
        graphicsPath.Close();

        var commands = graphicsPath.Commands;
        if (commands.Length == 0)
        {
            return start;
        }

        for (int i = commands.Length - 1; i >= 0; --i)
        {
            var command = commands[i];
            if (command.Type == PathCommandType.MoveTo)
            {
                return new PointF((float)command.X0, (float)command.Y0);
            }
        }

        return start;
    }
}
