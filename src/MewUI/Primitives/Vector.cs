using System.Diagnostics;

namespace Aprillz.MewUI;

/// <summary>
/// Represents a 2D vector.
/// </summary>
[DebuggerDisplay("Vector({X}, {Y})")]
public readonly struct Vector : IEquatable<Vector>
{
    /// <summary>
    /// Gets the zero vector (0, 0).
    /// </summary>
    public static readonly Vector Zero = new(0, 0);

    /// <summary>
    /// Gets the unit vector (1, 1).
    /// </summary>
    public static readonly Vector One = new(1, 1);

    /// <summary>
    /// Gets the X component.
    /// </summary>
    public double X { get; }

    /// <summary>
    /// Gets the Y component.
    /// </summary>
    public double Y { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Vector"/> struct.
    /// </summary>
    /// <param name="x">The X component.</param>
    /// <param name="y">The Y component.</param>
    public Vector(double x, double y)
    {
        X = x;
        Y = y;
    }

    /// <summary>
    /// Gets the Euclidean length of the vector.
    /// </summary>
    public double Length => Math.Sqrt(X * X + Y * Y);

    /// <summary>
    /// Gets the squared length of the vector.
    /// </summary>
    public double LengthSquared => X * X + Y * Y;

    /// <summary>
    /// Returns a normalized (unit-length) vector.
    /// </summary>
    public Vector Normalize()
    {
        var length = Length;
        return length > 0 ? new Vector(X / length, Y / length) : Zero;
    }

    /// <summary>
    /// Returns the negated vector.
    /// </summary>
    public Vector Negate() => new(-X, -Y);

    /// <summary>
    /// Adds two vectors component-wise.
    /// </summary>
    public static Vector operator +(Vector left, Vector right) =>
        new(left.X + right.X, left.Y + right.Y);

    /// <summary>
    /// Subtracts two vectors component-wise.
    /// </summary>
    public static Vector operator -(Vector left, Vector right) =>
        new(left.X - right.X, left.Y - right.Y);

    /// <summary>
    /// Scales a vector by a scalar factor.
    /// </summary>
    public static Vector operator *(Vector vector, double scalar) =>
        new(vector.X * scalar, vector.Y * scalar);

    /// <summary>
    /// Divides a vector by a scalar factor.
    /// </summary>
    public static Vector operator /(Vector vector, double scalar) =>
        new(vector.X / scalar, vector.Y / scalar);

    /// <summary>
    /// Negates a vector.
    /// </summary>
    public static Vector operator -(Vector vector) =>
        new(-vector.X, -vector.Y);

    /// <summary>
    /// Computes the dot product of two vectors.
    /// </summary>
    public static double Dot(Vector left, Vector right) =>
        left.X * right.X + left.Y * right.Y;

    /// <summary>
    /// Computes the 2D cross product (scalar) of two vectors.
    /// </summary>
    public static double Cross(Vector left, Vector right) =>
        left.X * right.Y - left.Y * right.X;

    /// <summary>
    /// Determines whether two vectors are equal.
    /// </summary>
    public static bool operator ==(Vector left, Vector right) => left.Equals(right);

    /// <summary>
    /// Determines whether two vectors are not equal.
    /// </summary>
    public static bool operator !=(Vector left, Vector right) => !left.Equals(right);

    /// <summary>
    /// Determines whether this instance is equal to another vector.
    /// </summary>
    public bool Equals(Vector other) =>
        X.Equals(other.X) && Y.Equals(other.Y);

    public override bool Equals(object? obj) =>
        obj is Vector other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(X, Y);
}
