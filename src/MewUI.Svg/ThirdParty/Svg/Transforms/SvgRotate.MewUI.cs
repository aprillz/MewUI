using System.Numerics;

namespace Svg.Transforms;

public sealed partial class SvgRotate
{
    public override Matrix3x2 Matrix =>
        Matrix3x2.CreateTranslation(-CenterX, -CenterY) *
        Matrix3x2.CreateRotation((float)(Angle * Math.PI / 180.0)) *
        Matrix3x2.CreateTranslation(CenterX, CenterY);
}
