namespace Aprillz.MewUI.Primitives;

/// <summary>
/// Represents the thickness of a border or margin (left, top, right, bottom).
/// </summary>
public readonly struct Thickness : IEquatable<Thickness>
{
    public static readonly Thickness Zero = new(0);

    public double Left { get; }

    public double Top { get; }

    public double Right { get; }

    public double Bottom { get; }

    public Thickness(double uniform)
        : this(uniform, uniform, uniform, uniform)
    {
    }

    public Thickness(double horizontal, double vertical)
        : this(horizontal, vertical, horizontal, vertical)
    {
    }

    public Thickness(double left, double top, double right, double bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public double HorizontalThickness => Left + Right;

    public double VerticalThickness => Top + Bottom;

    public bool IsUniform => Left == Top && Top == Right && Right == Bottom;

    public static Thickness operator +(Thickness a, Thickness b) =>
        new(a.Left + b.Left, a.Top + b.Top, a.Right + b.Right, a.Bottom + b.Bottom);

    public static Thickness operator -(Thickness a, Thickness b) =>
        new(a.Left - b.Left, a.Top - b.Top, a.Right - b.Right, a.Bottom - b.Bottom);

    public static Thickness operator *(Thickness thickness, double scalar) =>
        new(thickness.Left * scalar, thickness.Top * scalar,
            thickness.Right * scalar, thickness.Bottom * scalar);

    public static bool operator ==(Thickness left, Thickness right) => left.Equals(right);

    public static bool operator !=(Thickness left, Thickness right) => !left.Equals(right);

    public bool Equals(Thickness other) =>
        Left.Equals(other.Left) && Top.Equals(other.Top) &&
        Right.Equals(other.Right) && Bottom.Equals(other.Bottom);

    public override bool Equals(object? obj) =>
        obj is Thickness other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(Left, Top, Right, Bottom);

    public override string ToString() =>
        IsUniform
            ? $"Thickness({Left})"
            : $"Thickness({Left}, {Top}, {Right}, {Bottom})";
}