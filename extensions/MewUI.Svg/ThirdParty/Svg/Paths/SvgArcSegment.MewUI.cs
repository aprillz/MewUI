using System.Drawing;

using Aprillz.MewUI.Rendering;

namespace Svg.Pathing;

public sealed partial class SvgArcSegment
{
    public override PointF AddToPath(PathGeometry graphicsPath, PointF start, SvgPathSegmentList parent)
    {
        var end = ToAbsolute(End, IsRelative, start);
        graphicsPath.SvgArcTo(RadiusX, RadiusY, Angle, Size == SvgArcSize.Large, Sweep == SvgArcSweep.Positive, end.X, end.Y);
        return end;
    }
}
