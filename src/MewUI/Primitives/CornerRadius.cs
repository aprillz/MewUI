using System.Diagnostics;

namespace Aprillz.MewUI;

/// <summary>
/// Represents the corner radii of a rounded rectangle (top-left, top-right, bottom-right, bottom-left).
/// </summary>
[DebuggerDisplay("CornerRadius({TopLeft}, {TopRight}, {BottomRight}, {BottomLeft})")]
public readonly struct CornerRadius : IEquatable<CornerRadius>
{
    /// <summary>
    /// Gets a zero corner radius (0 on all corners).
    /// </summary>
    public static readonly CornerRadius Zero = new(0);

    /// <summary>
    /// Gets the top-left corner radius.
    /// </summary>
    public double TopLeft { get; }

    /// <summary>
    /// Gets the top-right corner radius.
    /// </summary>
    public double TopRight { get; }

    /// <summary>
    /// Gets the bottom-right corner radius.
    /// </summary>
    public double BottomRight { get; }

    /// <summary>
    /// Gets the bottom-left corner radius.
    /// </summary>
    public double BottomLeft { get; }

    /// <summary>
    /// Initializes a new instance with a uniform radius for all corners.
    /// </summary>
    public CornerRadius(double uniform)
        : this(uniform, uniform, uniform, uniform)
    {
    }

    /// <summary>
    /// Initializes a new instance with individual corner radii (clockwise from top-left).
    /// </summary>
    public CornerRadius(double topLeft, double topRight, double bottomRight, double bottomLeft)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomRight = bottomRight;
        BottomLeft = bottomLeft;
    }

    /// <summary>
    /// Gets a value indicating whether all corners have the same radius.
    /// </summary>
    public bool IsUniform => TopLeft == TopRight && TopRight == BottomRight && BottomRight == BottomLeft;

    /// <summary>
    /// Gets a value indicating whether all corner radii are zero.
    /// </summary>
    public bool IsZero => TopLeft == 0 && TopRight == 0 && BottomRight == 0 && BottomLeft == 0;

    /// <summary>
    /// Converts a uniform double value to a <see cref="CornerRadius"/>.
    /// </summary>
    public static implicit operator CornerRadius(double uniform) => new(uniform);

    /// <summary>
    /// Scales a corner radius by a scalar factor.
    /// </summary>
    public static CornerRadius operator *(CornerRadius radius, double scalar) =>
        new(radius.TopLeft * scalar, radius.TopRight * scalar,
            radius.BottomRight * scalar, radius.BottomLeft * scalar);

    /// <summary>
    /// Scales a corner radius by a scalar factor.
    /// </summary>
    public static CornerRadius operator *(double scalar, CornerRadius radius) =>
        radius * scalar;

    public static bool operator ==(CornerRadius left, CornerRadius right) => left.Equals(right);

    public static bool operator !=(CornerRadius left, CornerRadius right) => !left.Equals(right);

    public bool Equals(CornerRadius other) =>
        TopLeft.Equals(other.TopLeft) && TopRight.Equals(other.TopRight) &&
        BottomRight.Equals(other.BottomRight) && BottomLeft.Equals(other.BottomLeft);

    public override bool Equals(object? obj) =>
        obj is CornerRadius other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(TopLeft, TopRight, BottomRight, BottomLeft);
}
