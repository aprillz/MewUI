using System.Numerics;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// A brush that paints with a radial gradient centered at a focal point.
/// </summary>
public sealed class RadialGradientBrush : GradientBrush, IEquatable<RadialGradientBrush>
{
    /// <summary>Gets the center of the gradient ellipse.</summary>
    public Point Center { get; }

    /// <summary>
    /// Gets the focal point (gradient origin) from which the gradient radiates.
    /// In SVG this is the <c>fx</c>/<c>fy</c> attribute; defaults to <see cref="Center"/>.
    /// </summary>
    public Point GradientOrigin { get; }

    /// <summary>Gets the X radius of the gradient ellipse.</summary>
    public double RadiusX { get; }

    /// <summary>Gets the Y radius of the gradient ellipse.</summary>
    public double RadiusY { get; }

    /// <summary>Creates a radial gradient brush.</summary>
    /// <param name="center">Center of the gradient ellipse.</param>
    /// <param name="gradientOrigin">Focal point from which the gradient radiates (SVG: fx/fy).</param>
    /// <param name="radiusX">X radius of the gradient ellipse.</param>
    /// <param name="radiusY">Y radius of the gradient ellipse.</param>
    /// <param name="stops">Color stops defining the gradient. Copied on construction.</param>
    /// <param name="spreadMethod">How to extend the gradient beyond the ellipse boundary.</param>
    /// <param name="units">Coordinate space for geometry parameters.</param>
    /// <param name="gradientTransform">Optional additional transform applied to the gradient geometry.</param>
    public RadialGradientBrush(
        Point center,
        Point gradientOrigin,
        double radiusX,
        double radiusY,
        IReadOnlyList<GradientStop> stops,
        SpreadMethod spreadMethod = SpreadMethod.Pad,
        GradientUnits units = GradientUnits.UserSpaceOnUse,
        Matrix3x2? gradientTransform = null)
        : base(stops, spreadMethod, units, gradientTransform)
    {
        Center = center;
        GradientOrigin = gradientOrigin;
        RadiusX = radiusX;
        RadiusY = radiusY;
    }

    /// <inheritdoc/>
    public bool Equals(RadialGradientBrush? other) =>
        other is not null &&
        Center.Equals(other.Center) &&
        GradientOrigin.Equals(other.GradientOrigin) &&
        RadiusX.Equals(other.RadiusX) &&
        RadiusY.Equals(other.RadiusY) &&
        CommonEquals(other);

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as RadialGradientBrush);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(CommonHashCode(), Center, GradientOrigin, RadiusX, RadiusY);
}
