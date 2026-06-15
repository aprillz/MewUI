using System.Drawing;

using Aprillz.MewUI.Rendering;

namespace Svg.Pathing;

public sealed partial class SvgMoveToSegment
{
    public override PointF AddToPath(PathGeometry graphicsPath, PointF start, SvgPathSegmentList parent)
    {
        var point = ToAbsolute(End, IsRelative, start);
        graphicsPath.MoveTo(point.X, point.Y);
        return point;
    }
}
