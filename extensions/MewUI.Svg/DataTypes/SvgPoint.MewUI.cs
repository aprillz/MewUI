using Aprillz.MewUI;

namespace Svg;

public partial struct SvgPoint
{
    public Point ToDeviceValue(ISvgRenderer renderer, SvgElement owner)
    {
        return new Point(
            X.ToDeviceValue(renderer, UnitRenderingType.Horizontal, owner),
            Y.ToDeviceValue(renderer, UnitRenderingType.Vertical, owner));
    }
}
