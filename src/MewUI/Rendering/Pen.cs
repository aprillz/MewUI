namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Immutable description of a stroke: a brush, a thickness, and a stroke style.
/// <para>
/// A pen owns no native resources and is never disposed; instances are safe to
/// share across threads and carry value equality.
/// </para>
/// </summary>
public sealed class Pen : IEquatable<Pen>
{
    /// <summary>Gets the brush used to paint the stroke.</summary>
    public Brush Brush { get; }

    /// <summary>Gets the stroke thickness in device-independent pixels (DIPs).</summary>
    public double Thickness { get; }

    /// <summary>Gets the stroke style (line cap, line join, miter limit, dashes).</summary>
    public StrokeStyle StrokeStyle { get; }

    /// <summary>Creates a pen that strokes with <paramref name="brush"/>.</summary>
    /// <param name="brush">The brush to use for the stroke.</param>
    /// <param name="thickness">Stroke thickness in device-independent pixels.</param>
    /// <param name="strokeStyle">
    /// Stroke attributes, or <see langword="null"/> for <see cref="StrokeStyle.Default"/>
    /// (flat caps, miter join, miter limit 10).
    /// </param>
    public Pen(Brush brush, double thickness = 1.0, StrokeStyle? strokeStyle = null)
    {
        Brush = brush;
        Thickness = thickness;
        StrokeStyle = strokeStyle ?? StrokeStyle.Default;
    }

    /// <summary>Creates a pen that strokes with a solid <paramref name="color"/>.</summary>
    public Pen(Color color, double thickness = 1.0, StrokeStyle? strokeStyle = null)
        : this(new SolidColorBrush(color), thickness, strokeStyle)
    {
    }

    /// <inheritdoc/>
    public bool Equals(Pen? other) =>
        other is not null &&
        Thickness.Equals(other.Thickness) &&
        StrokeStyle.Equals(other.StrokeStyle) &&
        Brush.Equals(other.Brush);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as Pen);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Brush, Thickness, StrokeStyle);
}
