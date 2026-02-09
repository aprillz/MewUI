using System.Diagnostics;

namespace Aprillz.MewUI;

/// <summary>
/// Represents a size with width and height.
/// </summary>
[DebuggerDisplay("Size({Width}, {Height})")]
public readonly struct Size : IEquatable<Size>
{
    /// <summary>
    /// Gets an empty size (0, 0).
    /// </summary>
    public static readonly Size Empty = new(0, 0);

    /// <summary>
    /// Gets an infinite size (PositiveInfinity, PositiveInfinity).
    /// </summary>
    public static readonly Size Infinity = new(double.PositiveInfinity, double.PositiveInfinity);

    /// <summary>
    /// Gets the width component.
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Gets the height component.
    /// </summary>
    public double Height { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Size"/> struct.
    /// </summary>
    /// <param name="width">The width (negative values clamp to 0).</param>
    /// <param name="height">The height (negative values clamp to 0).</param>
    public Size(double width, double height)
    {
        Width = width < 0 ? 0 : width;
        Height = height < 0 ? 0 : height;
    }

    /// <summary>
    /// Gets a value indicating whether this size is empty (0, 0).
    /// </summary>
    public bool IsEmpty => Width == 0 && Height == 0;

    /// <summary>
    /// Returns a copy of this size with a new width.
    /// </summary>
    /// <param name="width">The new width.</param>
    public Size WithWidth(double width) => new(width, Height);

    /// <summary>
    /// Returns a copy of this size with a new height.
    /// </summary>
    /// <param name="height">The new height.</param>
    public Size WithHeight(double height) => new(Width, height);

    /// <summary>
    /// Constrains this size by taking the minimum of each component.
    /// </summary>
    /// <param name="constraint">The maximum allowed size for each component.</param>
    public Size Constrain(Size constraint) => new(
        Math.Min(Width, constraint.Width),
        Math.Min(Height, constraint.Height)
    );

    /// <summary>
    /// Returns a new size with the given thickness removed from both sides.
    /// </summary>
    /// <param name="thickness">The thickness to remove.</param>
    public Size Deflate(Thickness thickness) => new(
        Math.Max(0, Width - thickness.Left - thickness.Right),
        Math.Max(0, Height - thickness.Top - thickness.Bottom)
    );

    /// <summary>
    /// Returns a new size with the given thickness added on both sides.
    /// </summary>
    /// <param name="thickness">The thickness to add.</param>
    public Size Inflate(Thickness thickness) => new(
        Width + thickness.Left + thickness.Right,
        Height + thickness.Top + thickness.Bottom
    );

    /// <summary>
    /// Adds two sizes component-wise.
    /// </summary>
    public static Size operator +(Size left, Size right) =>
        new(left.Width + right.Width, left.Height + right.Height);

    /// <summary>
    /// Subtracts two sizes component-wise.
    /// </summary>
    public static Size operator -(Size left, Size right) =>
        new(left.Width - right.Width, left.Height - right.Height);

    /// <summary>
    /// Scales a size by a scalar factor.
    /// </summary>
    public static Size operator *(Size size, double scalar) =>
        new(size.Width * scalar, size.Height * scalar);

    /// <summary>
    /// Divides a size by a scalar factor.
    /// </summary>
    public static Size operator /(Size size, double scalar) =>
        new(size.Width / scalar, size.Height / scalar);

    /// <summary>
    /// Determines whether two sizes are equal.
    /// </summary>
    public static bool operator ==(Size left, Size right) => left.Equals(right);

    /// <summary>
    /// Determines whether two sizes are not equal.
    /// </summary>
    public static bool operator !=(Size left, Size right) => !left.Equals(right);

    /// <summary>
    /// Determines whether this instance is equal to another size.
    /// </summary>
    public bool Equals(Size other) =>
        Width.Equals(other.Width) && Height.Equals(other.Height);

    public override bool Equals(object? obj) =>
        obj is Size other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Width, Height);
}
