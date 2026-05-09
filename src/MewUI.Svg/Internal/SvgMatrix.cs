using System.Numerics;

namespace Aprillz.MewUI.Svg.Internal;

// SVG 2D affine transform matrix:
//  | a  c  e |
//  | b  d  f |
//  | 0  0  1 |
// Point transform: x' = a*x + c*y + e,  y' = b*x + d*y + f
internal readonly struct SvgMatrix
{
    public static readonly SvgMatrix Identity = new(1, 0, 0, 1, 0, 0);

    public readonly double A, B, C, D, E, F;

    public SvgMatrix(double a, double b, double c, double d, double e, double f)
    {
        A = a; B = b; C = c; D = d; E = e; F = f;
    }

    public static SvgMatrix Translate(double tx, double ty)
        => new(1, 0, 0, 1, tx, ty);

    public static SvgMatrix Scale(double sx, double sy)
        => new(sx, 0, 0, sy, 0, 0);

    public static SvgMatrix Rotate(double angleRad)
    {
        double cos = Math.Cos(angleRad);
        double sin = Math.Sin(angleRad);
        return new(cos, sin, -sin, cos, 0, 0);
    }

    public static SvgMatrix RotateAround(double angleRad, double cx, double cy)
    {
        double cos = Math.Cos(angleRad);
        double sin = Math.Sin(angleRad);
        return new(cos, sin, -sin, cos,
            cx - cx * cos + cy * sin,
            cy - cx * sin - cy * cos);
    }

    public static SvgMatrix SkewX(double angleRad)
        => new(1, 0, Math.Tan(angleRad), 1, 0, 0);

    public static SvgMatrix SkewY(double angleRad)
        => new(1, Math.Tan(angleRad), 0, 1, 0, 0);

    /// <summary>Returns this * other (apply 'other' first, then 'this').</summary>
    public SvgMatrix Multiply(SvgMatrix other) => new(
        A * other.A + C * other.B,
        B * other.A + D * other.B,
        A * other.C + C * other.D,
        B * other.C + D * other.D,
        A * other.E + C * other.F + E,
        B * other.E + D * other.F + F);

    /// <summary>Post-multiplies local onto this (parent * local) so local acts in the inner coord system.</summary>
    public SvgMatrix Append(SvgMatrix local) => Multiply(local);

    public (double x, double y) Apply(double x, double y)
        => (A * x + C * y + E, B * x + D * y + F);

    /// <summary>True when the matrix has no rotation or shear (only scale + translate).</summary>
    public bool IsAxisAligned => B == 0 && C == 0;

    public bool IsIdentity => A == 1 && B == 0 && C == 0 && D == 1 && E == 0 && F == 0;

    public Matrix3x2 ToMatrix3x2() => new((float)A, (float)B, (float)C, (float)D, (float)E, (float)F);
}
