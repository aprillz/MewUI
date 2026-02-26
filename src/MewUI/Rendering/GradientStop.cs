namespace Aprillz.MewUI.Rendering;

/// <summary>
/// A single color stop in a gradient, consisting of an offset and a color.
/// </summary>
public readonly struct GradientStop(double offset, Color color)
{
    /// <summary>Gets the position of the stop in the gradient, in the range [0, 1].</summary>
    public double Offset { get; } = offset;

    /// <summary>Gets the color at this stop.</summary>
    public Color Color { get; } = color;
}

/// <summary>
/// Specifies how a gradient is extended beyond its defined bounds.
/// </summary>
public enum SpreadMethod
{
    /// <summary>The last stop color is used outside the gradient bounds.</summary>
    Pad = 0,

    /// <summary>The gradient is reflected and repeated.</summary>
    Reflect = 1,

    /// <summary>The gradient is repeated.</summary>
    Repeat = 2,
}

/// <summary>
/// Specifies the coordinate system for gradient geometry.
/// </summary>
public enum GradientUnits
{
    /// <summary>Coordinates are in the user coordinate system of the element.</summary>
    UserSpaceOnUse = 0,

    /// <summary>Coordinates are relative to the bounding box of the element.</summary>
    ObjectBoundingBox = 1,
}

/// <summary>
/// Helper utilities for gradient brushes.
/// </summary>
public static class GradientBrushHelper
{
    /// <summary>
    /// Samples the gradient brush at <paramref name="t"/> (0–1) and returns an interpolated color.
    /// Used by backends that do not natively support gradients as a single representative color.
    /// </summary>
    public static Color Sample(IReadOnlyList<GradientStop> stops, double t)
    {
        if (stops == null || stops.Count == 0) return Color.Transparent;
        if (stops.Count == 1) return stops[0].Color;

        // Clamp t.
        t = Math.Clamp(t, 0.0, 1.0);

        // Find the bounding stops.
        for (int i = 0; i < stops.Count - 1; i++)
        {
            var s0 = stops[i];
            var s1 = stops[i + 1];
            if (t >= s0.Offset && t <= s1.Offset)
            {
                double span = s1.Offset - s0.Offset;
                double f = span < 1e-10 ? 0 : (t - s0.Offset) / span;
                return Lerp(s0.Color, s1.Color, f);
            }
        }

        // t is beyond the last stop.
        return t <= stops[0].Offset ? stops[0].Color : stops[^1].Color;
    }

    /// <summary>
    /// Returns a single representative color for the gradient by sampling at the midpoint (t=0.5).
    /// Used as a fallback on backends that do not support gradients natively.
    /// </summary>
    public static Color GetRepresentativeColor(this IGradientBrush brush)
        => Sample(brush.Stops, 0.5);

    private static Color Lerp(Color a, Color b, double t)
    {
        double u = 1.0 - t;
        return Color.FromArgb(
            (byte)(int)(a.A * u + b.A * t),
            (byte)(int)(a.R * u + b.R * t),
            (byte)(int)(a.G * u + b.G * t),
            (byte)(int)(a.B * u + b.B * t));
    }
}
