using System.Numerics;

namespace Svg.Transforms;

public sealed partial class SvgSkew
{
    public override Matrix3x2 Matrix => new(
        1f,
        (float)Math.Tan(AngleY / 180f * Math.PI),
        (float)Math.Tan(AngleX / 180f * Math.PI),
        1f,
        0f,
        0f);
}
