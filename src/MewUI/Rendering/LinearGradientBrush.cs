using System.Numerics;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// A brush that paints with a linear gradient between two points.
/// </summary>
public sealed class LinearGradientBrush : GradientBrush, IEquatable<LinearGradientBrush>
{
    /// <summary>Gets the start point of the gradient.</summary>
    public Point StartPoint { get; }

    /// <summary>Gets the end point of the gradient.</summary>
    public Point EndPoint { get; }

    /// <summary>Creates a linear gradient brush.</summary>
    /// <param name="startPoint">Start point (in <paramref name="units"/> coordinates).</param>
    /// <param name="endPoint">End point (in <paramref name="units"/> coordinates).</param>
    /// <param name="stops">Color stops defining the gradient. Copied on construction.</param>
    /// <param name="spreadMethod">How to extend the gradient beyond the start/end points.</param>
    /// <param name="units">Coordinate space for <paramref name="startPoint"/> and <paramref name="endPoint"/>.</param>
    /// <param name="gradientTransform">Optional additional transform applied to the gradient geometry.</param>
    public LinearGradientBrush(
        Point startPoint,
        Point endPoint,
        IReadOnlyList<GradientStop> stops,
        SpreadMethod spreadMethod = SpreadMethod.Pad,
        GradientUnits units = GradientUnits.UserSpaceOnUse,
        Matrix3x2? gradientTransform = null)
        : base(stops, spreadMethod, units, gradientTransform)
    {
        StartPoint = startPoint;
        EndPoint = endPoint;
    }

    /// <inheritdoc/>
    public bool Equals(LinearGradientBrush? other) =>
        other is not null &&
        StartPoint.Equals(other.StartPoint) &&
        EndPoint.Equals(other.EndPoint) &&
        CommonEquals(other);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as LinearGradientBrush);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(CommonHashCode(), StartPoint, EndPoint);
}
