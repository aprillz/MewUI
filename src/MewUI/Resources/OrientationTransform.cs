using System.Numerics;

namespace Aprillz.MewUI.Resources;

/// <summary>
/// Maps between an image's raw (as-decoded) pixel space and the oriented space the viewer sees after an
/// <see cref="ImageOrientation"/> is applied. Every consumer that honors orientation (layout, ViewBox,
/// alignment, hit/pixel lookup) goes through this single source of truth so the forward and inverse
/// directions cannot drift apart.
/// </summary>
/// <remarks>
/// Matrices follow the <see cref="Vector2.Transform(Vector2, Matrix3x2)"/> convention and map the raw
/// image box <c>[0,w] x [0,h]</c> onto the oriented box anchored at the origin. The transforms are pure
/// 90-degree rotations and axis flips, so they map axis-aligned rectangles to axis-aligned rectangles
/// exactly.
/// </remarks>
public static class OrientationTransform
{
    /// <summary>Normalizes an unknown/invalid value to <see cref="ImageOrientation.Normal"/>.</summary>
    public static ImageOrientation Normalize(ImageOrientation orientation) =>
        orientation is >= ImageOrientation.Normal and <= ImageOrientation.Rotate270
            ? orientation
            : ImageOrientation.Normal;

    /// <summary>
    /// True when the orientation is a quarter turn (or diagonal reflection), so the oriented size swaps
    /// width and height relative to the raw size.
    /// </summary>
    public static bool SwapsWidthHeight(ImageOrientation orientation) =>
        Normalize(orientation) is ImageOrientation.Transpose
            or ImageOrientation.Rotate90
            or ImageOrientation.Transverse
            or ImageOrientation.Rotate270;

    /// <summary>Size of the image in oriented space (width/height swapped for quarter turns).</summary>
    public static Size GetOrientedSize(ImageOrientation orientation, double rawWidth, double rawHeight) =>
        SwapsWidthHeight(orientation) ? new Size(rawHeight, rawWidth) : new Size(rawWidth, rawHeight);

    /// <summary>
    /// Builds the raw-to-oriented affine transform for an image of the given raw size. Entries are 0/±1
    /// plus a width/height translation, so the transform is exact for integer pixel coordinates.
    /// </summary>
    public static Matrix3x2 CreateRawToOrientedMatrix(ImageOrientation orientation, double rawWidth, double rawHeight)
    {
        float w = (float)rawWidth;
        float h = (float)rawHeight;

        // Constructor order is (m11, m12, m21, m22, m31, m32); a point maps to
        // (x*m11 + y*m21 + m31, x*m12 + y*m22 + m32).
        return Normalize(orientation) switch
        {
            ImageOrientation.Normal           => new Matrix3x2(1, 0, 0, 1, 0, 0),
            ImageOrientation.MirrorHorizontal => new Matrix3x2(-1, 0, 0, 1, w, 0),
            ImageOrientation.Rotate180        => new Matrix3x2(-1, 0, 0, -1, w, h),
            ImageOrientation.MirrorVertical   => new Matrix3x2(1, 0, 0, -1, 0, h),
            ImageOrientation.Transpose        => new Matrix3x2(0, 1, 1, 0, 0, 0),
            ImageOrientation.Rotate90         => new Matrix3x2(0, 1, -1, 0, h, 0),
            ImageOrientation.Transverse       => new Matrix3x2(0, -1, -1, 0, h, w),
            ImageOrientation.Rotate270        => new Matrix3x2(0, -1, 1, 0, 0, w),
            _                                 => Matrix3x2.Identity,
        };
    }

    /// <summary>Inverse of <see cref="CreateRawToOrientedMatrix"/> (oriented coordinates back to raw).</summary>
    public static Matrix3x2 CreateOrientedToRawMatrix(ImageOrientation orientation, double rawWidth, double rawHeight)
    {
        var forward = CreateRawToOrientedMatrix(orientation, rawWidth, rawHeight);
        // Determinant is +/-1 with integer entries, so the inverse is exact.
        return Matrix3x2.Invert(forward, out var inverse) ? inverse : Matrix3x2.Identity;
    }

    /// <summary>Maps a raw pixel coordinate to oriented space.</summary>
    public static Point RawToOriented(ImageOrientation orientation, double rawWidth, double rawHeight, Point raw) =>
        Apply(CreateRawToOrientedMatrix(orientation, rawWidth, rawHeight), raw);

    /// <summary>Maps an oriented coordinate back to raw pixel space.</summary>
    public static Point OrientedToRaw(ImageOrientation orientation, double rawWidth, double rawHeight, Point oriented) =>
        Apply(CreateOrientedToRawMatrix(orientation, rawWidth, rawHeight), oriented);

    /// <summary>Maps a raw-space rectangle to oriented space (stays axis-aligned).</summary>
    public static Rect RawToOriented(ImageOrientation orientation, double rawWidth, double rawHeight, Rect raw) =>
        TransformRect(CreateRawToOrientedMatrix(orientation, rawWidth, rawHeight), raw);

    /// <summary>Maps an oriented-space rectangle back to raw space (stays axis-aligned).</summary>
    public static Rect OrientedToRaw(ImageOrientation orientation, double rawWidth, double rawHeight, Rect oriented) =>
        TransformRect(CreateOrientedToRawMatrix(orientation, rawWidth, rawHeight), oriented);

    // Applies the affine transform in double precision. The matrix entries are exact (0/+/-1 and the
    // width/height translation), so promoting them to double avoids float coordinate rounding for large
    // images while keeping the Vector2.Transform convention.
    private static Point Apply(Matrix3x2 m, Point p) => new(
        p.X * m.M11 + p.Y * m.M21 + m.M31,
        p.X * m.M12 + p.Y * m.M22 + m.M32);

    private static Rect TransformRect(Matrix3x2 m, Rect r)
    {
        var a = Apply(m, new Point(r.X, r.Y));
        var b = Apply(m, new Point(r.X + r.Width, r.Y));
        var c = Apply(m, new Point(r.X, r.Y + r.Height));
        var d = Apply(m, new Point(r.X + r.Width, r.Y + r.Height));

        double minX = Math.Min(Math.Min(a.X, b.X), Math.Min(c.X, d.X));
        double minY = Math.Min(Math.Min(a.Y, b.Y), Math.Min(c.Y, d.Y));
        double maxX = Math.Max(Math.Max(a.X, b.X), Math.Max(c.X, d.X));
        double maxY = Math.Max(Math.Max(a.Y, b.Y), Math.Max(c.Y, d.Y));
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }
}
