using System.Diagnostics;

namespace Aprillz.MewUI;

/// <summary>
/// Represents the thickness of a border or margin (left, top, right, bottom).
/// </summary>
[DebuggerDisplay("Thickness({Left}, {Top}, {Right}, {Bottom})")]
public readonly struct Thickness : IEquatable<Thickness>
{
    /// <summary>
    /// Gets a zero thickness (0 on all sides).
    /// </summary>
    public static readonly Thickness Zero = new(0);

    /// <summary>
    /// Gets the left thickness.
    /// </summary>
    public double Left { get; }

    /// <summary>
    /// Gets the top thickness.
    /// </summary>
    public double Top { get; }

    /// <summary>
    /// Gets the right thickness.
    /// </summary>
    public double Right { get; }

    /// <summary>
    /// Gets the bottom thickness.
    /// </summary>
    public double Bottom { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Thickness"/> struct with a uniform value.
    /// </summary>
    /// <param name="uniform">The thickness to apply to all sides.</param>
    public Thickness(double uniform)
        : this(uniform, uniform, uniform, uniform)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Thickness"/> struct with horizontal and vertical values.
    /// </summary>
    /// <param name="horizontal">The thickness applied to left and right.</param>
    /// <param name="vertical">The thickness applied to top and bottom.</param>
    public Thickness(double horizontal, double vertical)
        : this(horizontal, vertical, horizontal, vertical)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Thickness"/> struct with individual side values.
    /// </summary>
    /// <param name="left">The left thickness.</param>
    /// <param name="top">The top thickness.</param>
    /// <param name="right">The right thickness.</param>
    /// <param name="bottom">The bottom thickness.</param>
    public Thickness(double left, double top, double right, double bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    /// <summary>
    /// Gets the combined left and right thickness.
    /// </summary>
    public double HorizontalThickness => Left + Right;

    /// <summary>
    /// Gets the combined top and bottom thickness.
    /// </summary>
    public double VerticalThickness => Top + Bottom;

    /// <summary>
    /// Gets a value indicating whether all sides have the same thickness.
    /// </summary>
    public bool IsUniform => Left == Top && Top == Right && Right == Bottom;

    /// <summary>
    /// Adds two thicknesses component-wise.
    /// </summary>
    public static Thickness operator +(Thickness a, Thickness b) =>
        new(a.Left + b.Left, a.Top + b.Top, a.Right + b.Right, a.Bottom + b.Bottom);

    /// <summary>
    /// Subtracts two thicknesses component-wise.
    /// </summary>
    public static Thickness operator -(Thickness a, Thickness b) =>
        new(a.Left - b.Left, a.Top - b.Top, a.Right - b.Right, a.Bottom - b.Bottom);

    /// <summary>
    /// Scales a thickness by a scalar factor.
    /// </summary>
    public static Thickness operator *(Thickness thickness, double scalar) =>
        new(thickness.Left * scalar, thickness.Top * scalar,
            thickness.Right * scalar, thickness.Bottom * scalar);

    /// <summary>
    /// Determines whether two thicknesses are equal.
    /// </summary>
    public static bool operator ==(Thickness left, Thickness right) => left.Equals(right);

    /// <summary>
    /// Determines whether two thicknesses are not equal.
    /// </summary>
    public static bool operator !=(Thickness left, Thickness right) => !left.Equals(right);

    /// <summary>
    /// Determines whether this instance is equal to another thickness.
    /// </summary>
    public bool Equals(Thickness other) =>
        Left.Equals(other.Left) && Top.Equals(other.Top) &&
        Right.Equals(other.Right) && Bottom.Equals(other.Bottom);

    public override bool Equals(object? obj) =>
        obj is Thickness other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Left, Top, Right, Bottom);
}
