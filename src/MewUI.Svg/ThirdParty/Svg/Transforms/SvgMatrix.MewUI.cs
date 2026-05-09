using System.Numerics;

namespace Svg.Transforms;

public sealed partial class SvgMatrix
{
    public override Matrix3x2 Matrix => new(
        Points[0],
        Points[1],
        Points[2],
        Points[3],
        Points[4],
        Points[5]);
}
