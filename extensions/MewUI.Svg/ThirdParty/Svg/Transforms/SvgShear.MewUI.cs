using System.Numerics;

namespace Svg.Transforms;

public sealed partial class SvgShear
{
    public override Matrix3x2 Matrix => new(1f, Y, X, 1f, 0f, 0f);
}
