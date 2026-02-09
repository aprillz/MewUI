using System.Diagnostics;

namespace Aprillz.MewUI;

/// <summary>
/// Represents a point with X and Y coordinates.
/// </summary>
[DebuggerDisplay("Point({X}, {Y})")]
public readonly struct Point : IEquatable<Point>
{
    /// <summary>
    /// Gets the origin point (0, 0).
    /// </summary>
    public static readonly Point Zero = new(0, 0);

    /// <summary>
    /// Gets the X-coordinate.
    /// </summary>
    public double X { get; }

    /// <summary>
    /// Gets the Y-coordinate.
    /// </summary>
    public double Y { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Point"/> struct.
    /// </summary>
    /// <param name="x">The X-coordinate.</param>
    /// <param name="y">The Y-coordinate.</param>
    public Point(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>
    /// Returns a copy of this point with a new X-coordinate.
    /// </summary>
    /// <param name="x">The new X-coordinate.</param>
    public Point WithX(double x) => new(x, Y);

    /// <summary>
    /// Returns a copy of this point with a new Y-coordinate.
    /// </summary>
    /// <param name="y">The new Y-coordinate.</param>
    public Point WithY(double y) => new(X, y);

    /// <summary>
    /// Returns a new point offset by the given delta.
    /// </summary>
    /// <param name="dx">The delta X.</param>
    /// <param name="dy">The delta Y.</param>
    public Point Offset(double dx, double dy) => new(X + dx, Y + dy);

    /// <summary>
    /// Returns a new point offset by the given vector.
    /// </summary>
    /// <param name="offset">The offset vector.</param>
    public Point Offset(Vector offset) => new(X + offset.X, Y + offset.Y);

    /// <summary>
    /// Returns the Euclidean distance to another point.
    /// </summary>
    /// <param name="other">The other point.</param>
    public double DistanceTo(Point other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>
    /// Adds a vector to a point.
    /// </summary>
    public static Point operator +(Point point, Vector vector) =>
        new(point.X + vector.X, point.Y + vector.Y);

    /// <summary>
    /// Subtracts a vector from a point.
    /// </summary>
    public static Point operator -(Point point, Vector vector) =>
        new(point.X - vector.X, point.Y - vector.Y);

    /// <summary>
    /// Subtracts two points and returns the resulting vector.
    /// </summary>
    public static Vector operator -(Point left, Point right) =>
        new(left.X - right.X, left.Y - right.Y);

    /// <summary>
    /// Scales a point by a scalar factor.
    /// </summary>
    public static Point operator *(Point point, double scalar) =>
        new(point.X * scalar, point.Y * scalar);

    /// <summary>
    /// Determines whether two points are equal.
    /// </summary>
    public static bool operator ==(Point left, Point right) => left.Equals(right);

    /// <summary>
    /// Determines whether two points are not equal.
    /// </summary>
    public static bool operator !=(Point left, Point right) => !left.Equals(right);

    /// <summary>
    /// Determines whether this instance is equal to another point.
    /// </summary>
    public bool Equals(Point other) =>
        X.Equals(other.X) && Y.Equals(other.Y);

    public override bool Equals(object? obj) =>
        obj is Point other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(X, Y);
}
