namespace Aprillz.MewUI;

/// <summary>
/// Represents a rectangle defined by position and size.
/// </summary>
public readonly struct Rect : IEquatable<Rect>
{
    public static readonly Rect Empty = new(0, 0, 0, 0);

    public double X { get; }

    public double Y { get; }

    public double Width { get; }

    public double Height { get; }

    public Rect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width < 0 ? 0 : width;
        Height = height < 0 ? 0 : height;
    }

    public Rect(Point location, Size size)
        : this(location.X, location.Y, size.Width, size.Height)
    {
    }

    public Rect(Size size)
        : this(0, 0, size.Width, size.Height)
    {
    }

    public double Left => X;

    public double Top => Y;

    public double Right => X + Width;

    public double Bottom => Y + Height;

    public Point TopLeft => new(X, Y);

    public Point TopRight => new(Right, Y);

    public Point BottomLeft => new(X, Bottom);

    public Point BottomRight => new(Right, Bottom);

    public Point Center => new(X + Width / 2, Y + Height / 2);

    public Size Size => new(Width, Height);

    public Point Position => new(X, Y);

    public bool IsEmpty => Width == 0 || Height == 0;

    public bool Contains(Point point) =>
        point.X >= X && point.X < Right &&
        point.Y >= Y && point.Y < Bottom;

    public bool Contains(Rect rect) =>
        rect.X >= X && rect.Right <= Right &&
        rect.Y >= Y && rect.Bottom <= Bottom;

    public bool IntersectsWith(Rect rect) =>
        rect.X < Right && rect.Right > X &&
        rect.Y < Bottom && rect.Bottom > Y;

    public Rect Intersect(Rect rect)
    {
        var x = Math.Max(X, rect.X);
        var y = Math.Max(Y, rect.Y);
        var right = Math.Min(Right, rect.Right);
        var bottom = Math.Min(Bottom, rect.Bottom);

        if (right > x && bottom > y)
        {
            return new Rect(x, y, right - x, bottom - y);
        }

        return Empty;
    }

    public Rect Union(Rect rect)
    {
        if (IsEmpty)
        {
            return rect;
        }

        if (rect.IsEmpty)
        {
            return this;
        }

        var x = Math.Min(X, rect.X);
        var y = Math.Min(Y, rect.Y);
        var right = Math.Max(Right, rect.Right);
        var bottom = Math.Max(Bottom, rect.Bottom);

        return new Rect(x, y, right - x, bottom - y);
    }

    public Rect Offset(double dx, double dy) =>
        new(X + dx, Y + dy, Width, Height);

    public Rect Offset(Vector offset) =>
        new(X + offset.X, Y + offset.Y, Width, Height);

    public Rect Inflate(double dx, double dy) =>
        new(X - dx, Y - dy, Width + 2 * dx, Height + 2 * dy);

    public Rect Inflate(Thickness thickness) =>
        new(X - thickness.Left, Y - thickness.Top,
            Width + thickness.Left + thickness.Right,
            Height + thickness.Top + thickness.Bottom);

    public Rect Deflate(Thickness thickness) =>
        new(X + thickness.Left, Y + thickness.Top,
            Width - thickness.Left - thickness.Right,
            Height - thickness.Top - thickness.Bottom);

    public Rect WithX(double x) => new(x, Y, Width, Height);

    public Rect WithY(double y) => new(X, y, Width, Height);

    public Rect WithWidth(double width) => new(X, Y, width, Height);

    public Rect WithHeight(double height) => new(X, Y, Width, height);

    public Rect WithPosition(Point position) => new(position.X, position.Y, Width, Height);

    public Rect WithSize(Size size) => new(X, Y, size.Width, size.Height);

    public static bool operator ==(Rect left, Rect right) => left.Equals(right);

    public static bool operator !=(Rect left, Rect right) => !left.Equals(right);

    public bool Equals(Rect other) =>
        X.Equals(other.X) && Y.Equals(other.Y) &&
        Width.Equals(other.Width) && Height.Equals(other.Height);

    public override bool Equals(object? obj) =>
        obj is Rect other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(X, Y, Width, Height);

    public override string ToString() => $"Rect({X}, {Y}, {Width}, {Height})";
}