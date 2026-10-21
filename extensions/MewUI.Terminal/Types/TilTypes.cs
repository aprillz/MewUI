// Ported from Windows Terminal src/inc/til/point.h, size.h and rect.h (MIT), keeping the members the terminal uses.

namespace Aprillz.MewUI.Terminal.Types;

/// <summary>A cell position: column <see cref="X"/>, row <see cref="Y"/>. Points order by row, then column.</summary>
internal readonly record struct TilPoint(int X, int Y) : IComparable<TilPoint>
{
    public int CompareTo(TilPoint other) => Y != other.Y ? Y.CompareTo(other.Y) : X.CompareTo(other.X);

    public static bool operator <(TilPoint left, TilPoint right) => left.CompareTo(right) < 0;

    public static bool operator <=(TilPoint left, TilPoint right) => left.CompareTo(right) <= 0;

    public static bool operator >(TilPoint left, TilPoint right) => left.CompareTo(right) > 0;

    public static bool operator >=(TilPoint left, TilPoint right) => left.CompareTo(right) >= 0;

    public static TilPoint operator +(TilPoint left, TilPoint right) => new(checked(left.X + right.X), checked(left.Y + right.Y));

    public static TilPoint operator -(TilPoint left, TilPoint right) => new(checked(left.X - right.X), checked(left.Y - right.Y));

    public static TilPoint Min(TilPoint left, TilPoint right) => left <= right ? left : right;

    public static TilPoint Max(TilPoint left, TilPoint right) => left >= right ? left : right;

    public override string ToString() => $"({X}, {Y})";
}

/// <summary>A size in cells.</summary>
internal readonly record struct TilSize(int Width, int Height)
{
    public int Area => checked(Width * Height);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public override string ToString() => $"[{Width} x {Height}]";
}

/// <summary>A rectangle of cells with exclusive <see cref="Right"/> and <see cref="Bottom"/>.</summary>
internal readonly record struct TilRect(int Left, int Top, int Right, int Bottom)
{
    public TilRect(TilPoint topLeft, TilPoint bottomRight)
        : this(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y)
    {
    }

    public TilRect(TilPoint topLeft, TilSize size)
        : this(topLeft.X, topLeft.Y, checked(topLeft.X + size.Width), checked(topLeft.Y + size.Height))
    {
    }

    public TilRect(TilSize size)
        : this(0, 0, size.Width, size.Height)
    {
    }

    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public TilPoint Origin => new(Left, Top);

    public TilSize Size => new(Width, Height);

    public bool IsEmpty => !(Right > Left && Bottom > Top);

    public bool Contains(TilPoint point) => point.X >= Left && point.X < Right && point.Y >= Top && point.Y < Bottom;

    public bool Contains(TilRect other) => other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;

    public TilRect Intersect(TilRect other)
    {
        int left = Math.Max(Left, other.Left);
        int right = Math.Min(Right, other.Right);
        if (left >= right)
        {
            return default;
        }

        int top = Math.Max(Top, other.Top);
        int bottom = Math.Min(Bottom, other.Bottom);
        if (top >= bottom)
        {
            return default;
        }

        return new TilRect(left, top, right, bottom);
    }

    public TilRect Union(TilRect other)
    {
        if (IsEmpty)
        {
            return other.IsEmpty ? default : other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        return new TilRect(Math.Min(Left, other.Left), Math.Min(Top, other.Top), Math.Max(Right, other.Right), Math.Max(Bottom, other.Bottom));
    }

    public TilRect Offset(TilPoint delta) => new(Left + delta.X, Top + delta.Y, Right + delta.X, Bottom + delta.Y);

    public override string ToString() => $"(L:{Left}, T:{Top}, R:{Right}, B:{Bottom})";
}

/// <summary>A rectangle of cells with inclusive <see cref="Right"/> and <see cref="Bottom"/>, as VT margins are given.</summary>
internal readonly record struct TilInclusiveRect(int Left, int Top, int Right, int Bottom)
{
    public TilRect ToExclusive() => new(Left, Top, Right + 1, Bottom + 1);
}

/// <summary>A range of cells from <see cref="Start"/> to <see cref="End"/>, both inclusive, in reading order.</summary>
internal readonly record struct TilPointSpan(TilPoint Start, TilPoint End);
