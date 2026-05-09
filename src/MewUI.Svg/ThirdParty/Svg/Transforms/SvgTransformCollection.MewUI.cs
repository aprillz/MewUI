using System.Numerics;

namespace Svg.Transforms;

public partial class SvgTransformCollection
{
    public Matrix3x2 GetMatrix()
    {
        var result = Matrix3x2.Identity;
        foreach (var transform in this)
        {
            result = transform.Matrix * result;
        }

        return result;
    }
}
