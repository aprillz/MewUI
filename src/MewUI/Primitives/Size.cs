namespace Aprillz.MewUI;

/// <summary>
/// Represents a size with width and height.
/// </summary>
public readonly struct Size : IEquatable<Size>
{
    public static readonly Size Empty = new(0, 0);
    public static readonly Size Infinity = new(double.PositiveInfinity, double.PositiveInfinity);

    public double Width { get; }

    public double Height { get; }

    public Size(double width, double height)
    {
        Width = width < 0 ? 0 : width;
        Height = height < 0 ? 0 : height;
    }

    public bool IsEmpty => Width == 0 && Height == 0;

    public Size WithWidth(double width) => new(width, Height);

    public Size WithHeight(double height) => new(Width, height);

    public Size Constrain(Size constraint) => new(
        Math.Min(Width, constraint.Width),
        Math.Min(Height, constraint.Height)
    );

    public Size Deflate(Thickness thickness) => new(
        Math.Max(0, Width - thickness.Left - thickness.Right),
        Math.Max(0, Height - thickness.Top - thickness.Bottom)
    );

    public Size Inflate(Thickness thickness) => new(
        Width + thickness.Left + thickness.Right,
        Height + thickness.Top + thickness.Bottom
    );

    public static Size operator +(Size left, Size right) =>
        new(left.Width + right.Width, left.Height + right.Height);

    public static Size operator -(Size left, Size right) =>
        new(left.Width - right.Width, left.Height - right.Height);

    public static Size operator *(Size size, double scalar) =>
        new(size.Width * scalar, size.Height * scalar);

    public static Size operator /(Size size, double scalar) =>
        new(size.Width / scalar, size.Height / scalar);

    public static bool operator ==(Size left, Size right) => left.Equals(right);

    public static bool operator !=(Size left, Size right) => !left.Equals(right);

    public bool Equals(Size other) =>
        Width.Equals(other.Width) && Height.Equals(other.Height);

    public override bool Equals(object? obj) =>
        obj is Size other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Width, Height);

    public override string ToString() => $"Size({Width}, {Height})";
}