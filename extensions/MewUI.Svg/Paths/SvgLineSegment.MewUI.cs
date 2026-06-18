using System.Drawing;

using Aprillz.MewUI.Rendering;

namespace Svg.Pathing;

public sealed partial class SvgLineSegment
{
    public override PointF AddToPath(PathGeometry graphicsPath, PointF start, SvgPathSegmentList parent)
    {
        var end = ToAbsolute(End, IsRelative, start);
        graphicsPath.LineTo(end.X, end.Y);
        return end;
    }
}
