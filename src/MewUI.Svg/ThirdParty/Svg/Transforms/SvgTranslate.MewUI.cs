using System.Numerics;

namespace Svg.Transforms;

public sealed partial class SvgTranslate
{
    public override Matrix3x2 Matrix => Matrix3x2.CreateTranslation(X, Y);
}
