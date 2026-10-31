using System.Numerics;

namespace Aprillz.MewUI.Rendering.Retained;

internal static class RetainedGeometry
{
    /// <summary>Axis-aligned bounds of <paramref name="rect"/> after <paramref name="matrix"/>.</summary>
    internal static Rect TransformRect(Rect rect, Matrix3x2 matrix)
    {
        if (matrix.IsIdentity)
        {
            return rect;
        }

        var topLeft = Vector2.Transform(new Vector2((float)rect.X, (float)rect.Y), matrix);
        var topRight = Vector2.Transform(new Vector2((float)rect.Right, (float)rect.Y), matrix);
        var bottomLeft = Vector2.Transform(new Vector2((float)rect.X, (float)rect.Bottom), matrix);
        var bottomRight = Vector2.Transform(new Vector2((float)rect.Right, (float)rect.Bottom), matrix);

        double minX = Math.Min(Math.Min(topLeft.X, topRight.X), Math.Min(bottomLeft.X, bottomRight.X));
        double minY = Math.Min(Math.Min(topLeft.Y, topRight.Y), Math.Min(bottomLeft.Y, bottomRight.Y));
        double maxX = Math.Max(Math.Max(topLeft.X, topRight.X), Math.Max(bottomLeft.X, bottomRight.X));
        double maxY = Math.Max(Math.Max(topLeft.Y, topRight.Y), Math.Max(bottomLeft.Y, bottomRight.Y));
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    /// <summary><paramref name="point"/> after <paramref name="matrix"/>, kept in double precision under a translation.</summary>
    internal static Point TransformPoint(Point point, Matrix3x2 matrix)
    {
        if (matrix.IsIdentity)
        {
            return point;
        }

        if (IsTranslation(matrix))
        {
            return new Point(point.X + matrix.M31, point.Y + matrix.M32);
        }

        var transformed = Vector2.Transform(new Vector2((float)point.X, (float)point.Y), matrix);
        return new Point(transformed.X, transformed.Y);
    }

    /// <summary>
    /// The transform that does to what stands <paramref name="shift"/> further along what <paramref name="matrix"/>
    /// did to it where it stood: a scale about an element's centre keeps following the element after a move.
    /// </summary>
    internal static Matrix3x2 Shifted(Matrix3x2 matrix, Vector shift)
    {
        if (shift.X == 0 && shift.Y == 0)
        {
            return matrix;
        }

        var back = Matrix3x2.CreateTranslation((float)-shift.X, (float)-shift.Y);
        var forth = Matrix3x2.CreateTranslation((float)shift.X, (float)shift.Y);
        return back * matrix * forth;
    }

    private static bool IsTranslation(Matrix3x2 matrix)
        => matrix.M11 == 1 && matrix.M12 == 0 && matrix.M21 == 0 && matrix.M22 == 1;
}

/// <summary>Unions the rectangles a pass draws, telling "nothing drawn" from an empty rectangle.</summary>
internal struct BoundsAccumulator
{
    private Rect _rect;
    private bool _hasValue;

    internal readonly Rect Result => _hasValue ? _rect : default;

    internal void Add(Rect value)
    {
        if (value.Width <= 0 || value.Height <= 0)
        {
            return;
        }

        _rect = _hasValue ? _rect.Union(value) : value;
        _hasValue = true;
    }
}
