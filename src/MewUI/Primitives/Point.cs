namespace Aprillz.MewUI;

/// <summary>
/// Represents a point with X and Y coordinates.
/// </summary>
public readonly struct Point : IEquatable<Point>
{
    public static readonly Point Zero = new(0, 0);

    public double X { get; }

    public double Y { get; }

    public Point(double x, double y)
    {
        X = x;
        Y = y;
    }

    public Point WithX(double x) => new(x, Y);

    public Point WithY(double y) => new(X, y);

    public Point Offset(double dx, double dy) => new(X + dx, Y + dy);

    public Point Offset(Vector offset) => new(X + offset.X, Y + offset.Y);

    public double DistanceTo(Point other)
    {
        var dx = X - other.X;
        var dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public static Point operator +(Point point, Vector vector) =>
        new(point.X + vector.X, point.Y + vector.Y);

    public static Point operator -(Point point, Vector vector) =>
        new(point.X - vector.X, point.Y - vector.Y);

    public static Vector operator -(Point left, Point right) =>
        new(left.X - right.X, left.Y - right.Y);

    public static Point operator *(Point point, double scalar) =>
        new(point.X * scalar, point.Y * scalar);

    public static bool operator ==(Point left, Point right) => left.Equals(right);

    public static bool operator !=(Point left, Point right) => !left.Equals(right);

    public bool Equals(Point other) =>
        X.Equals(other.X) && Y.Equals(other.Y);

    public override bool Equals(object? obj) =>
        obj is Point other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(X, Y);

    public override string ToString() => $"Point({X}, {Y})";
}