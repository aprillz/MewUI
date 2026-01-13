namespace Aprillz.MewUI.Primitives;

/// <summary>
/// Represents a 2D vector.
/// </summary>
public readonly struct Vector : IEquatable<Vector>
{
    public static readonly Vector Zero = new(0, 0);
    public static readonly Vector One = new(1, 1);

    public double X { get; }

    public double Y { get; }

    public Vector(double x, double y)
    {
        X = x;
        Y = y;
    }

    public double Length => Math.Sqrt(X * X + Y * Y);

    public double LengthSquared => X * X + Y * Y;

    public Vector Normalize()
    {
        var length = Length;
        return length > 0 ? new Vector(X / length, Y / length) : Zero;
    }

    public Vector Negate() => new(-X, -Y);

    public static Vector operator +(Vector left, Vector right) =>
        new(left.X + right.X, left.Y + right.Y);

    public static Vector operator -(Vector left, Vector right) =>
        new(left.X - right.X, left.Y - right.Y);

    public static Vector operator *(Vector vector, double scalar) =>
        new(vector.X * scalar, vector.Y * scalar);

    public static Vector operator /(Vector vector, double scalar) =>
        new(vector.X / scalar, vector.Y / scalar);

    public static Vector operator -(Vector vector) =>
        new(-vector.X, -vector.Y);

    public static double Dot(Vector left, Vector right) =>
        left.X * right.X + left.Y * right.Y;

    public static double Cross(Vector left, Vector right) =>
        left.X * right.Y - left.Y * right.X;

    public static bool operator ==(Vector left, Vector right) => left.Equals(right);

    public static bool operator !=(Vector left, Vector right) => !left.Equals(right);

    public bool Equals(Vector other) =>
        X.Equals(other.X) && Y.Equals(other.Y);

    public override bool Equals(object? obj) =>
        obj is Vector other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(X, Y);

    public override string ToString() => $"Vector({X}, {Y})";
}