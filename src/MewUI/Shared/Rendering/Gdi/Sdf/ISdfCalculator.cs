namespace Aprillz.MewUI.Rendering.Gdi.Sdf;

/// <summary>
/// Interface for Signed Distance Field (SDF) calculations.
/// SDF returns the shortest distance from a point to the shape boundary.
/// Negative values indicate inside the shape, positive values indicate outside.
/// </summary>
internal interface ISdfCalculator
{
    /// <summary>
    /// Calculates the signed distance from the point (x, y) to the shape boundary.
    /// </summary>
    /// <param name="x">X coordinate in shape space.</param>
    /// <param name="y">Y coordinate in shape space.</param>
    /// <returns>
    /// Signed distance: negative if inside, positive if outside, zero on boundary.
    /// </returns>
    float GetSignedDistance(float x, float y);

    /// <summary>
    /// Checks if a point is within the anti-aliasing zone (near the boundary).
    /// </summary>
    /// <param name="x">X coordinate in shape space.</param>
    /// <param name="y">Y coordinate in shape space.</param>
    /// <param name="aaWidth">Width of the anti-aliasing zone.</param>
    /// <returns>True if the point is within the AA zone.</returns>
    bool IsInAaZone(float x, float y, float aaWidth);

    /// <summary>
    /// Calculates alpha value using smoothstep function for anti-aliasing.
    /// </summary>
    /// <param name="x">X coordinate in shape space.</param>
    /// <param name="y">Y coordinate in shape space.</param>
    /// <param name="aaWidth">Width of the anti-aliasing zone.</param>
    /// <returns>Alpha value between 0 (outside) and 255 (inside).</returns>
    byte GetAlpha(float x, float y, float aaWidth);
}

/// <summary>
/// Base class providing common SDF functionality.
/// </summary>
internal abstract class SdfCalculatorBase : ISdfCalculator
{
    public abstract float GetSignedDistance(float x, float y);

    public virtual bool IsInAaZone(float x, float y, float aaWidth)
    {
        float dist = GetSignedDistance(x, y);
        return MathF.Abs(dist) <= aaWidth;
    }

    public virtual byte GetAlpha(float x, float y, float aaWidth)
    {
        float dist = GetSignedDistance(x, y);
        return DistanceToAlpha(dist, aaWidth);
    }

    /// <summary>
    /// Converts a signed distance to an alpha value using smoothstep.
    /// </summary>
    protected static byte DistanceToAlpha(float distance, float aaWidth)
    {
        if (distance <= -aaWidth)
        {
            return 255; // Fully inside
        }

        if (distance >= aaWidth)
        {
            return 0; // Fully outside
        }

        // Smoothstep for anti-aliased transition
        float t = (distance + aaWidth) / (2f * aaWidth);
        t = 1f - t; // Invert so inside is high alpha
        t = t * t * (3f - 2f * t); // Smoothstep

        return (byte)(t * 255f + 0.5f);
    }

    /// <summary>
    /// Smoothstep function: 3t² - 2t³
    /// </summary>
    protected static float Smoothstep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
