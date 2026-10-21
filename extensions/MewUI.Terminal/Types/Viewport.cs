// Ported from Windows Terminal src/types/inc/viewport.hpp and src/types/viewport.cpp (MIT).

namespace Aprillz.MewUI.Terminal.Types;

/// <summary>
/// A rectangle of cells kept with inclusive edges, with walking and clamping helpers. The default value is empty
/// (right and bottom of -1).
/// </summary>
internal readonly struct Viewport : IEquatable<Viewport>
{
    private readonly int _left;
    private readonly int _top;
    private readonly int _right;
    private readonly int _bottom;
    private readonly bool _isSet;

    private Viewport(int left, int top, int right, int bottom)
    {
        _left = left;
        _top = top;
        _right = right;
        _bottom = bottom;
        _isSet = true;
    }

    public static Viewport Empty => default;

    public static Viewport FromInclusive(TilInclusiveRect rect) => new(rect.Left, rect.Top, rect.Right, rect.Bottom);

    public static Viewport FromExclusive(TilRect rect) => new(rect.Left, rect.Top, rect.Right - 1, rect.Bottom - 1);

    public static Viewport FromDimensions(TilPoint origin, TilSize dimensions)
        => new(origin.X, origin.Y, origin.X + dimensions.Width - 1, origin.Y + dimensions.Height - 1);

    public static Viewport FromDimensions(TilSize dimensions) => FromDimensions(default, dimensions);

    public int Left => _left;

    public int Top => _top;

    public int RightInclusive => _isSet ? _right : -1;

    public int RightExclusive => RightInclusive + 1;

    public int BottomInclusive => _isSet ? _bottom : -1;

    public int BottomExclusive => BottomInclusive + 1;

    public int Height => BottomExclusive - Top;

    public int Width => RightExclusive - Left;

    public TilPoint Origin => new(Left, Top);

    public TilPoint BottomRightInclusive => new(RightInclusive, BottomInclusive);

    public TilPoint BottomRightExclusive => new(RightExclusive, BottomExclusive);

    public TilPoint BottomInclusiveRightExclusive => new(RightExclusive, BottomInclusive);

    public TilPoint EndExclusive => new(Left, BottomExclusive);

    public TilSize Dimensions => new(Width, Height);

    public bool IsValid => Left >= 0 && Top >= 0 && RightInclusive >= Left && BottomInclusive >= Top;

    public bool IsInBounds(Viewport other)
        => other.Left >= Left && other.Left <= RightInclusive &&
            other.RightInclusive >= Left && other.RightInclusive <= RightInclusive &&
            other.Top >= Top && other.Top <= other.BottomInclusive &&
            other.BottomInclusive >= Top && other.BottomInclusive <= BottomInclusive;

    public bool IsInBounds(TilPoint position, bool allowEndExclusive = false)
    {
        if (allowEndExclusive && position == EndExclusive)
        {
            return true;
        }

        return position.X >= Left && position.X < RightExclusive && position.Y >= Top && position.Y < BottomExclusive;
    }

    /// <summary>Whether the point lies inside, counting the column just past the right edge as inside.</summary>
    public bool IsInExclusiveBounds(TilPoint position)
        => position.X >= Left && position.X <= RightExclusive && position.Y >= Top && position.Y <= BottomInclusive;

    public TilPoint Clamp(TilPoint position)
    {
        if (!IsValid)
        {
            throw new InvalidOperationException("An invalid viewport cannot clamp.");
        }

        return new TilPoint(Math.Clamp(position.X, Left, RightInclusive), Math.Clamp(position.Y, Top, BottomInclusive));
    }

    public Viewport Clamp(Viewport other) => FromExclusive(ToExclusive().Intersect(other.ToExclusive()));

    public bool IncrementInBounds(ref TilPoint position, bool allowEndExclusive = false) => WalkInBounds(ref position, 1, allowEndExclusive);

    public bool DecrementInBounds(ref TilPoint position, bool allowEndExclusive = false) => WalkInBounds(ref position, -1, allowEndExclusive);

    /// <summary>Orders two points in reading order, as their distance in cells.</summary>
    public int CompareInBounds(TilPoint first, TilPoint second) => (first.Y - second.Y) * Width + (first.X - second.X);

    /// <summary>Moves a point <paramref name="delta"/> cells in reading order, stopping at the edges; false when it stopped.</summary>
    public bool WalkInBounds(ref TilPoint position, int delta, bool allowEndExclusive = false)
    {
        long left = Left;
        long top = Top;
        long width = Math.Max(0, RightInclusive - Left + 1);
        long height = Math.Max(0, BottomInclusive - Top + 1);
        long max = width * height - (allowEndExclusive ? 0 : 1);
        long offset = width * (position.Y - top) + (position.X - left) + delta;
        long clamped = Math.Clamp(offset, 0, max);
        position = new TilPoint((int)(clamped % width + left), (int)(clamped / width + top));
        return offset == clamped;
    }

    public bool IncrementInExclusiveBounds(ref TilPoint position) => WalkInExclusiveBounds(ref position, 1);

    public bool DecrementInExclusiveBounds(ref TilPoint position) => WalkInExclusiveBounds(ref position, -1);

    public int CompareInExclusiveBounds(TilPoint first, TilPoint second) => (first.Y - second.Y) * Width + (first.X - second.X);

    /// <summary>As <see cref="WalkInBounds"/>, with one more column per row for the position past the right edge.</summary>
    public bool WalkInExclusiveBounds(ref TilPoint position, int delta)
    {
        long left = Left;
        long top = Top;
        long width = Math.Max(0, RightInclusive - Left + 2);
        long height = Math.Max(0, BottomInclusive - Top + 1);
        long max = width * height;
        long offset = width * (position.Y - top) + (position.X - left) + delta;
        long clamped = Math.Clamp(offset, 0, max);
        position = new TilPoint((int)(clamped % width + left), (int)(clamped / width + top));
        return offset == clamped;
    }

    public TilPoint GetWalkOrigin(int delta) => new(delta >= 0 ? Left : RightInclusive, delta >= 0 ? Top : BottomInclusive);

    /// <summary>The walk delta for copying <paramref name="source"/> onto an overlapping <paramref name="target"/> cell by cell.</summary>
    public static int DetermineWalkDirection(Viewport source, Viewport target) => target.Origin < source.Origin ? 1 : -1;

    public TilRect TrimToViewport(TilRect rect)
        => new(Math.Max(rect.Left, Left), Math.Max(rect.Top, Top), Math.Min(rect.Right, RightExclusive), Math.Min(rect.Bottom, BottomExclusive));

    public TilPoint ConvertToOrigin(TilPoint position) => new(position.X - Left, position.Y - Top);

    public TilPoint ConvertFromOrigin(TilPoint position) => new(position.X + Left, position.Y + Top);

    public TilRect ConvertToOrigin(TilRect rect) => rect.Offset(new TilPoint(-Left, -Top));

    public Viewport ConvertToOrigin(Viewport other) => Offset(other, new TilPoint(-Left, -Top));

    public Viewport ConvertFromOrigin(Viewport other) => Offset(other, new TilPoint(Left, Top));

    public TilRect ToExclusive() => new(Left, Top, RightExclusive, BottomExclusive);

    public TilInclusiveRect ToInclusive() => new(Left, Top, RightInclusive, BottomInclusive);

    public Viewport ToOrigin() => new(0, 0, RightInclusive - Left, BottomInclusive - Top);

    public static Viewport Offset(Viewport original, TilPoint delta)
        => new(original.Left + delta.X, original.Top + delta.Y, original.RightInclusive + delta.X, original.BottomInclusive + delta.Y);

    public static Viewport Union(Viewport left, Viewport right)
    {
        if (!left.IsValid && !right.IsValid)
        {
            return Empty;
        }

        if (!left.IsValid)
        {
            return right;
        }

        if (!right.IsValid)
        {
            return left;
        }

        return new Viewport(
            Math.Min(left.Left, right.Left),
            Math.Min(left.Top, right.Top),
            Math.Max(left.RightInclusive, right.RightInclusive),
            Math.Max(left.BottomInclusive, right.BottomInclusive));
    }

    public static Viewport Intersect(Viewport left, Viewport right)
    {
        var intersection = new Viewport(
            Math.Max(left.Left, right.Left),
            Math.Max(left.Top, right.Top),
            Math.Min(left.RightInclusive, right.RightInclusive),
            Math.Min(left.BottomInclusive, right.BottomInclusive));
        return intersection.IsValid ? intersection : Empty;
    }

    public bool Equals(Viewport other)
        => Left == other.Left && Top == other.Top && RightInclusive == other.RightInclusive && BottomInclusive == other.BottomInclusive;

    public override bool Equals(object? obj) => obj is Viewport other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Left, Top, RightInclusive, BottomInclusive);

    public static bool operator ==(Viewport left, Viewport right) => left.Equals(right);

    public static bool operator !=(Viewport left, Viewport right) => !left.Equals(right);

    public override string ToString() => $"(L:{Left}, T:{Top}, R:{RightInclusive}, B:{BottomInclusive}) [{Width} x {Height}]";
}
