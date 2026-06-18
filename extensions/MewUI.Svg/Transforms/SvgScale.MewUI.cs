using System.Numerics;

namespace Svg.Transforms;

public sealed partial class SvgScale
{
    public override Matrix3x2 Matrix => Matrix3x2.CreateScale(X, Y);
}
