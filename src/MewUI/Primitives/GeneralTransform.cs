using System.Numerics;

namespace Aprillz.MewUI;

/// <summary>
/// Represents a general point/rect transform between visual coordinate spaces.
/// This is a minimal WPF-compatible shape; it follows any transform applied between the two spaces.
/// </summary>
public abstract class GeneralTransform
{
    /// <summary>
    /// Transforms a point from the source coordinate space to the target coordinate space.
    /// </summary>
    /// <param name="point">The point to transform.</param>
    /// <exception cref="InvalidOperationException">The transform cannot map points, such as the inverse of a zero scale.</exception>
    public abstract Point Transform(Point point);

    /// <summary>
    /// Transforms a rectangle and returns the axis-aligned bounding box of the result.
    /// </summary>
    /// <param name="rect">The rectangle to transform.</param>
    public virtual Rect TransformBounds(Rect rect)
    {
        var topLeft = Transform(rect.TopLeft);
        var topRight = Transform(rect.TopRight);
        var bottomLeft = Transform(rect.BottomLeft);
        var bottomRight = Transform(rect.BottomRight);
        return BoundingBox(topLeft, topRight, bottomLeft, bottomRight);
    }

    /// <summary>
    /// Attempts to transform a point.
    /// </summary>
    /// <param name="inPoint">The input point.</param>
    /// <param name="result">The transformed point.</param>
    /// <returns><see langword="true"/> if the transformation succeeded.</returns>
    public virtual bool TryTransform(Point inPoint, out Point result)
    {
        result = Transform(inPoint);
        return true;
    }

    /// <summary>
    /// Gets the inverse transform; the inverse of a transform that has none maps no point, which <see cref="TryTransform"/> reports as false.
    /// </summary>
    public abstract GeneralTransform Inverse { get; }

    internal static Rect BoundingBox(Point first, Point second, Point third, Point fourth)
    {
        double minX = Math.Min(Math.Min(first.X, second.X), Math.Min(third.X, fourth.X));
        double minY = Math.Min(Math.Min(first.Y, second.Y), Math.Min(third.Y, fourth.Y));
        double maxX = Math.Max(Math.Max(first.X, second.X), Math.Max(third.X, fourth.X));
        double maxY = Math.Max(Math.Max(first.Y, second.Y), Math.Max(third.Y, fourth.Y));
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }
}

internal sealed class IdentityGeneralTransform : GeneralTransform
{
    public static readonly IdentityGeneralTransform Instance = new();

    private IdentityGeneralTransform() { }

    public override Point Transform(Point point) => point;

    public override Rect TransformBounds(Rect rect) => rect;

    public override GeneralTransform Inverse => this;
}

internal sealed class TranslateGeneralTransform : GeneralTransform
{
    private readonly double _dx;
    private readonly double _dy;

    public TranslateGeneralTransform(double dx, double dy)
    {
        _dx = dx;
        _dy = dy;
    }

    public override Point Transform(Point point) =>
        new(point.X + _dx, point.Y + _dy);

    public override Rect TransformBounds(Rect rect) =>
        rect.Offset(_dx, _dy);

    public override GeneralTransform Inverse =>
        new TranslateGeneralTransform(-_dx, -_dy);
}

/// <summary>
/// An affine transform in double precision, using the row-vector convention of <see cref="Matrix3x2"/>.
/// One that came from inverting a singular matrix maps no point: <see cref="TryTransform"/> returns false.
/// </summary>
internal sealed class MatrixGeneralTransform : GeneralTransform
{
    private static readonly MatrixGeneralTransform _singular = new(0, 0, 0, 0, 0, 0, isValid: false);

    private readonly double _m11;
    private readonly double _m12;
    private readonly double _m21;
    private readonly double _m22;
    private readonly double _offsetX;
    private readonly double _offsetY;
    private readonly bool _isValid;

    private MatrixGeneralTransform(double m11, double m12, double m21, double m22, double offsetX, double offsetY, bool isValid)
    {
        _m11 = m11;
        _m12 = m12;
        _m21 = m21;
        _m22 = m22;
        _offsetX = offsetX;
        _offsetY = offsetY;
        _isValid = isValid;
    }

    internal static MatrixGeneralTransform FromMatrix(Matrix3x2 matrix)
        => new(matrix.M11, matrix.M12, matrix.M21, matrix.M22, matrix.M31, matrix.M32, isValid: true);

    internal static MatrixGeneralTransform Translation(double offsetX, double offsetY)
        => new(1, 0, 0, 1, offsetX, offsetY, isValid: true);

    /// <summary>False when this is the inverse of a singular transform.</summary>
    internal bool IsValid => _isValid;

    /// <summary>Returns the transform that applies this one and then <paramref name="next"/>.</summary>
    internal MatrixGeneralTransform Then(MatrixGeneralTransform next)
    {
        if (!_isValid || !next._isValid)
        {
            return _singular;
        }

        return new MatrixGeneralTransform(
            _m11 * next._m11 + _m12 * next._m21,
            _m11 * next._m12 + _m12 * next._m22,
            _m21 * next._m11 + _m22 * next._m21,
            _m21 * next._m12 + _m22 * next._m22,
            _offsetX * next._m11 + _offsetY * next._m21 + next._offsetX,
            _offsetX * next._m12 + _offsetY * next._m22 + next._offsetY,
            isValid: true);
    }

    internal MatrixGeneralTransform Invert()
    {
        double determinant = _m11 * _m22 - _m12 * _m21;
        if (!_isValid || determinant == 0 || !double.IsFinite(determinant))
        {
            return _singular;
        }

        return new MatrixGeneralTransform(
            _m22 / determinant,
            -_m12 / determinant,
            -_m21 / determinant,
            _m11 / determinant,
            (_m21 * _offsetY - _m22 * _offsetX) / determinant,
            (_m12 * _offsetX - _m11 * _offsetY) / determinant,
            isValid: true);
    }

    public override Point Transform(Point point)
    {
        if (!TryTransform(point, out var result))
        {
            throw new InvalidOperationException("The transform has no inverse, so it cannot map points.");
        }

        return result;
    }

    public override bool TryTransform(Point inPoint, out Point result)
    {
        if (!_isValid)
        {
            result = default;
            return false;
        }

        result = new Point(
            inPoint.X * _m11 + inPoint.Y * _m21 + _offsetX,
            inPoint.X * _m12 + inPoint.Y * _m22 + _offsetY);
        return true;
    }

    public override GeneralTransform Inverse => Invert();
}
