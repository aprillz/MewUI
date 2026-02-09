using System.Diagnostics;

namespace Aprillz.MewUI;

/// <summary>
/// Represents a rectangle defined by position and size.
/// </summary>
[DebuggerDisplay("Rect({X}, {Y}, {Width}, {Height})")]
public readonly struct Rect : IEquatable<Rect>
{
    /// <summary>
    /// Gets an empty rectangle at the origin with zero size.
    /// </summary>
    public static readonly Rect Empty = new(0, 0, 0, 0);

    /// <summary>
    /// Gets the X-coordinate of the rectangle's left edge.
    /// </summary>
    public double X { get; }

    /// <summary>
    /// Gets the Y-coordinate of the rectangle's top edge.
    /// </summary>
    public double Y { get; }

    /// <summary>
    /// Gets the width of the rectangle (negative values clamp to 0).
    /// </summary>
    public double Width { get; }

    /// <summary>
    /// Gets the height of the rectangle (negative values clamp to 0).
    /// </summary>
    public double Height { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="Rect"/> struct.
    /// </summary>
    /// <param name="x">The X-coordinate.</param>
    /// <param name="y">The Y-coordinate.</param>
    /// <param name="width">The width (negative values clamp to 0).</param>
    /// <param name="height">The height (negative values clamp to 0).</param>
    public Rect(double x, double y, double width, double height)
    {
        X = x;
        Y = y;
        Width = width < 0 ? 0 : width;
        Height = height < 0 ? 0 : height;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Rect"/> struct from a location and size.
    /// </summary>
    /// <param name="location">The top-left location.</param>
    /// <param name="size">The size.</param>
    public Rect(Point location, Size size)
        : this(location.X, location.Y, size.Width, size.Height)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Rect"/> struct at the origin with the given size.
    /// </summary>
    /// <param name="size">The size.</param>
    public Rect(Size size)
        : this(0, 0, size.Width, size.Height)
    {
    }

    /// <summary>
    /// Gets the X-coordinate of the rectangle's left edge.
    /// </summary>
    public double Left => X;

    /// <summary>
    /// Gets the Y-coordinate of the rectangle's top edge.
    /// </summary>
    public double Top => Y;

    /// <summary>
    /// Gets the X-coordinate of the rectangle's right edge.
    /// </summary>
    public double Right => X + Width;

    /// <summary>
    /// Gets the Y-coordinate of the rectangle's bottom edge.
    /// </summary>
    public double Bottom => Y + Height;

    /// <summary>
    /// Gets the top-left corner point.
    /// </summary>
    public Point TopLeft => new(X, Y);

    /// <summary>
    /// Gets the top-right corner point.
    /// </summary>
    public Point TopRight => new(Right, Y);

    /// <summary>
    /// Gets the bottom-left corner point.
    /// </summary>
    public Point BottomLeft => new(X, Bottom);

    /// <summary>
    /// Gets the bottom-right corner point.
    /// </summary>
    public Point BottomRight => new(Right, Bottom);

    /// <summary>
    /// Gets the center point of the rectangle.
    /// </summary>
    public Point Center => new(X + Width / 2, Y + Height / 2);

    /// <summary>
    /// Gets the size of the rectangle.
    /// </summary>
    public Size Size => new(Width, Height);

    /// <summary>
    /// Gets the top-left position of the rectangle.
    /// </summary>
    public Point Position => new(X, Y);

    /// <summary>
    /// Gets a value indicating whether the rectangle has zero area.
    /// </summary>
    public bool IsEmpty => Width == 0 || Height == 0;

    /// <summary>
    /// Determines whether the rectangle contains the specified point.
    /// </summary>
    /// <param name="point">The point to test.</param>
    public bool Contains(Point point) =>
        point.X >= X && point.X < Right &&
        point.Y >= Y && point.Y < Bottom;

    /// <summary>
    /// Determines whether the rectangle fully contains another rectangle.
    /// </summary>
    /// <param name="rect">The rectangle to test.</param>
    public bool Contains(Rect rect) =>
        rect.X >= X && rect.Right <= Right &&
        rect.Y >= Y && rect.Bottom <= Bottom;

    /// <summary>
    /// Determines whether the rectangle intersects another rectangle.
    /// </summary>
    /// <param name="rect">The rectangle to test.</param>
    public bool IntersectsWith(Rect rect) =>
        rect.X < Right && rect.Right > X &&
        rect.Y < Bottom && rect.Bottom > Y;

    /// <summary>
    /// Returns the intersection of this rectangle with another rectangle.
    /// </summary>
    /// <param name="rect">The rectangle to intersect with.</param>
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

    /// <summary>
    /// Returns the smallest rectangle that contains both this rectangle and another rectangle.
    /// </summary>
    /// <param name="rect">The rectangle to union with.</param>
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

    /// <summary>
    /// Returns a new rectangle translated by the given delta.
    /// </summary>
    /// <param name="dx">The delta X.</param>
    /// <param name="dy">The delta Y.</param>
    public Rect Offset(double dx, double dy) =>
        new(X + dx, Y + dy, Width, Height);

    /// <summary>
    /// Returns a new rectangle translated by the given vector.
    /// </summary>
    /// <param name="offset">The translation vector.</param>
    public Rect Offset(Vector offset) =>
        new(X + offset.X, Y + offset.Y, Width, Height);

    /// <summary>
    /// Returns a new rectangle inflated by the given amount in each direction.
    /// </summary>
    /// <param name="dx">The horizontal inflation amount.</param>
    /// <param name="dy">The vertical inflation amount.</param>
    public Rect Inflate(double dx, double dy) =>
        new(X - dx, Y - dy, Width + 2 * dx, Height + 2 * dy);

    /// <summary>
    /// Returns a new rectangle inflated by the given thickness.
    /// </summary>
    /// <param name="thickness">The thickness to apply.</param>
    public Rect Inflate(Thickness thickness) =>
        new(X - thickness.Left, Y - thickness.Top,
            Width + thickness.Left + thickness.Right,
            Height + thickness.Top + thickness.Bottom);

    /// <summary>
    /// Returns a new rectangle deflated by the given thickness.
    /// </summary>
    /// <param name="thickness">The thickness to remove.</param>
    public Rect Deflate(Thickness thickness) =>
        new(X + thickness.Left, Y + thickness.Top,
            Width - thickness.Left - thickness.Right,
            Height - thickness.Top - thickness.Bottom);

    /// <summary>
    /// Returns a copy of this rectangle with a new X-coordinate.
    /// </summary>
    /// <param name="x">The new X-coordinate.</param>
    public Rect WithX(double x) => new(x, Y, Width, Height);

    /// <summary>
    /// Returns a copy of this rectangle with a new Y-coordinate.
    /// </summary>
    /// <param name="y">The new Y-coordinate.</param>
    public Rect WithY(double y) => new(X, y, Width, Height);

    /// <summary>
    /// Returns a copy of this rectangle with a new width.
    /// </summary>
    /// <param name="width">The new width.</param>
    public Rect WithWidth(double width) => new(X, Y, width, Height);

    /// <summary>
    /// Returns a copy of this rectangle with a new height.
    /// </summary>
    /// <param name="height">The new height.</param>
    public Rect WithHeight(double height) => new(X, Y, Width, height);

    /// <summary>
    /// Returns a copy of this rectangle with a new position.
    /// </summary>
    /// <param name="position">The new top-left position.</param>
    public Rect WithPosition(Point position) => new(position.X, position.Y, Width, Height);

    /// <summary>
    /// Returns a copy of this rectangle with a new size.
    /// </summary>
    /// <param name="size">The new size.</param>
    public Rect WithSize(Size size) => new(X, Y, size.Width, size.Height);

    /// <summary>
    /// Determines whether two rectangles are equal.
    /// </summary>
    public static bool operator ==(Rect left, Rect right) => left.Equals(right);

    /// <summary>
    /// Determines whether two rectangles are not equal.
    /// </summary>
    public static bool operator !=(Rect left, Rect right) => !left.Equals(right);

    /// <summary>
    /// Determines whether this instance is equal to another rectangle.
    /// </summary>
    public bool Equals(Rect other) =>
        X.Equals(other.X) && Y.Equals(other.Y) &&
        Width.Equals(other.Width) && Height.Equals(other.Height);

    public override bool Equals(object? obj) =>
        obj is Rect other && Equals(other);

    public override int GetHashCode() =>
        HashCode.Combine(X, Y, Width, Height);
}
