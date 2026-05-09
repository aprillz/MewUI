using System.Numerics;

namespace Svg.Transforms;

public abstract partial class SvgTransform
{
    public abstract Matrix3x2 Matrix { get; }

    public override bool Equals(object obj) => obj is SvgTransform other && Matrix.Equals(other.Matrix);

    public override int GetHashCode() => Matrix.GetHashCode();

    public static bool operator ==(SvgTransform lhs, SvgTransform rhs)
    {
        if (ReferenceEquals(lhs, rhs))
        {
            return true;
        }

        if (lhs is null || rhs is null)
        {
            return false;
        }

        return lhs.Equals(rhs);
    }

    public static bool operator !=(SvgTransform lhs, SvgTransform rhs) => !(lhs == rhs);
}
