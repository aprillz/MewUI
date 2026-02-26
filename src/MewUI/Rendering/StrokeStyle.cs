namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Specifies the cap drawn at the start and end of each open sub-path.
/// </summary>
public enum StrokeLineCap
{
    /// <summary>A flat cap that ends exactly at the path endpoint. This is the default.</summary>
    Flat = 0,

    /// <summary>A semicircular cap that extends past the endpoint by half the stroke width.</summary>
    Round = 1,

    /// <summary>A square cap that extends past the endpoint by half the stroke width.</summary>
    Square = 2,
}

/// <summary>
/// Specifies how the joint between two connected lines is drawn.
/// </summary>
public enum StrokeLineJoin
{
    /// <summary>
    /// A sharp miter join. If the miter would exceed <see cref="StrokeStyle.MiterLimit"/> times the
    /// stroke width, the join is trimmed to a bevel.  This is the default.
    /// </summary>
    Miter = 0,

    /// <summary>A circular arc join centered on the vertex.</summary>
    Round = 1,

    /// <summary>A flat bevel join that cuts off the corner.</summary>
    Bevel = 2,
}

/// <summary>
/// Describes stroke attributes: line cap, line join, miter limit, and optional dash pattern.
/// </summary>
public readonly struct StrokeStyle : IEquatable<StrokeStyle>
{
    /// <summary>Gets the cap style applied to both the start and end of each open sub-path.</summary>
    public StrokeLineCap LineCap { get; init; }

    /// <summary>Gets the join style applied at each vertex.</summary>
    public StrokeLineJoin LineJoin { get; init; }

    /// <summary>
    /// Gets the miter limit.  When the miter join angle is very sharp the miter can extend far;
    /// if the ratio of miter length to stroke width exceeds this value the join is clipped to a bevel.
    /// Meaningful only when <see cref="LineJoin"/> is <see cref="StrokeLineJoin.Miter"/>.
    /// Defaults to <c>10.0</c>, matching SVG and Direct2D defaults.
    /// </summary>
    public double MiterLimit { get; init; }

    /// <summary>
    /// Gets the dash pattern as alternating (dash length, gap length) values in stroke-width units.
    /// <see langword="null"/> or empty means a solid stroke.
    /// </summary>
    public IReadOnlyList<double>? DashArray { get; init; }

    /// <summary>
    /// Gets the offset into the dash pattern at which drawing begins, in stroke-width units.
    /// </summary>
    public double DashOffset { get; init; }

    /// <summary>Returns <see langword="true"/> when this style produces a dashed stroke.</summary>
    public bool IsDashed => DashArray is { Count: > 0 };

    /// <summary>The default stroke style: flat caps, miter join, miter limit 10, solid.</summary>
    public static readonly StrokeStyle Default = new() { MiterLimit = 10.0 };

    /// <inheritdoc/>
    public bool Equals(StrokeStyle other) =>
        LineCap == other.LineCap &&
        LineJoin == other.LineJoin &&
        Math.Abs(MiterLimit - other.MiterLimit) < 1e-9 &&
        Math.Abs(DashOffset - other.DashOffset) < 1e-9 &&
        DashArrayEquals(DashArray, other.DashArray);

    private static bool DashArrayEquals(IReadOnlyList<double>? a, IReadOnlyList<double>? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null || a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
            if (Math.Abs(a[i] - b[i]) >= 1e-9) return false;
        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is StrokeStyle s && Equals(s);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int h = HashCode.Combine(LineCap, LineJoin, MiterLimit, DashOffset);
        if (DashArray != null)
            foreach (var d in DashArray)
                h = HashCode.Combine(h, d);
        return h;
    }

    /// <inheritdoc/>
    public static bool operator ==(StrokeStyle left, StrokeStyle right) => left.Equals(right);

    /// <inheritdoc/>
    public static bool operator !=(StrokeStyle left, StrokeStyle right) => !left.Equals(right);
}
