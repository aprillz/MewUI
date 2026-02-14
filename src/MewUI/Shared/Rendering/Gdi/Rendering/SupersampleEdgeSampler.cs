using Aprillz.MewUI.Rendering.Gdi.Sdf;

namespace Aprillz.MewUI.Rendering.Gdi.Rendering;

/// <summary>
/// Supersampling-based edge sampler for high-quality anti-aliasing.
/// Takes multiple samples per pixel and averages the results.
/// </summary>
internal sealed class SupersampleEdgeSampler : EdgeSamplerBase
{
    private readonly int _factor;
    private readonly int _totalSamples;
    private readonly byte _sourceAlpha;

    /// <summary>
    /// Creates a supersample edge sampler.
    /// </summary>
    /// <param name="factor">Supersample factor (2 = 2x2 = 4 samples, 3 = 3x3 = 9 samples).</param>
    /// <param name="sourceAlpha">Source alpha value for fully covered pixels.</param>
    public SupersampleEdgeSampler(int factor, byte sourceAlpha = 255)
    {
        _factor = Math.Max(1, Math.Min(4, factor));
        _totalSamples = _factor * _factor;
        _sourceAlpha = sourceAlpha;
    }

    public override int SampleCount => _totalSamples;

    public override byte SampleEdge(int px, int py, ISdfCalculator sdf, float aaWidth)
    {
        int covered = 0;

        for (int sy = 0; sy < _factor; sy++)
        {
            float y = py + (sy + 0.5f) / _factor;

            for (int sx = 0; sx < _factor; sx++)
            {
                float x = px + (sx + 0.5f) / _factor;

                // Check if sample point is inside the shape
                float dist = sdf.GetSignedDistance(x, y);
                if (dist <= 0)
                {
                    covered++;
                }
            }
        }

        return CoverageToAlpha(covered, _totalSamples, _sourceAlpha);
    }

    public byte SampleEdgeCentered(int px, int py, ISdfCalculator sdf, float halfSurfaceW, float halfSurfaceH)
    {
        int covered = 0;

        for (int sy = 0; sy < _factor; sy++)
        {
            float y = py + (sy + 0.5f) / _factor - halfSurfaceH;

            for (int sx = 0; sx < _factor; sx++)
            {
                float x = px + (sx + 0.5f) / _factor - halfSurfaceW;

                float dist = sdf.GetSignedDistance(x, y);
                if (dist <= 0)
                {
                    covered++;
                }
            }
        }

        return CoverageToAlpha(covered, _totalSamples, _sourceAlpha);
    }

    /// <summary>
    /// Samples edge for a line segment with thickness.
    /// Uses squared distance for efficiency.
    /// </summary>
    public byte SampleLineEdge(int px, int py, LineSdf lineSdf, float halfThicknessSq)
    {
        int covered = 0;

        for (int sy = 0; sy < _factor; sy++)
        {
            float y = py + (sy + 0.5f) / _factor;

            for (int sx = 0; sx < _factor; sx++)
            {
                float x = px + (sx + 0.5f) / _factor;

                float distSq = lineSdf.DistanceSqToSegment(x, y);
                if (distSq <= halfThicknessSq)
                {
                    covered++;
                }
            }
        }

        return CoverageToAlpha(covered, _totalSamples, _sourceAlpha);
    }

    /// <summary>
    /// Samples edge for a rounded rectangle.
    /// Coordinates are in pixel space (0 to width/height).
    /// </summary>
    public byte SampleRoundedRectEdge(int px, int py, RoundedRectSdf rectSdf, float halfW, float halfH)
    {
        int covered = 0;

        for (int sy = 0; sy < _factor; sy++)
        {
            float y = py + (sy + 0.5f) / _factor - halfH;

            for (int sx = 0; sx < _factor; sx++)
            {
                float x = px + (sx + 0.5f) / _factor - halfW;

                if (rectSdf.IsInside(x, y))
                {
                    covered++;
                }
            }
        }

        return CoverageToAlpha(covered, _totalSamples, _sourceAlpha);
    }

    public byte SampleRoundedRectStrokeEdgeSsaa(
        int px,
        int py,
        RoundedRectSdf outerSdf,
        RoundedRectSdf? innerSdf,
        float halfSurfaceW,
        float halfSurfaceH)
    {
        int covered = 0;

        for (int sy = 0; sy < _factor; sy++)
        {
            float y = py + (sy + 0.5f) / _factor - halfSurfaceH;

            for (int sx = 0; sx < _factor; sx++)
            {
                float x = px + (sx + 0.5f) / _factor - halfSurfaceW;

                if (!outerSdf.IsInside(x, y))
                {
                    continue;
                }

                if (innerSdf != null && innerSdf.IsInside(x, y))
                {
                    continue;
                }

                covered++;
            }
        }

        return CoverageToAlpha(covered, _totalSamples, _sourceAlpha);
    }

    /// <summary>
    /// Samples edge for an ellipse.
    /// Optimized for the specific shape.
    /// </summary>
    public byte SampleEllipseEdge(int px, int py, EllipseSdf ellipseSdf)
    {
        int covered = 0;

        for (int sy = 0; sy < _factor; sy++)
        {
            float y = py + (sy + 0.5f) / _factor;

            for (int sx = 0; sx < _factor; sx++)
            {
                float x = px + (sx + 0.5f) / _factor;

                if (ellipseSdf.IsInside(x, y))
                {
                    covered++;
                }
            }
        }

        return CoverageToAlpha(covered, _totalSamples, _sourceAlpha);
    }

    /// <summary>
    /// Samples edge for a stroke (ring shape between outer and inner bounds).
    /// Used for ellipse strokes where SDF stores center coordinates internally.
    /// </summary>
    public byte SampleStrokeEdge(int px, int py, ISdfCalculator outerSdf, ISdfCalculator? innerSdf)
    {
        int covered = 0;

        for (int sy = 0; sy < _factor; sy++)
        {
            float y = py + (sy + 0.5f) / _factor;

            for (int sx = 0; sx < _factor; sx++)
            {
                float x = px + (sx + 0.5f) / _factor;

                // Must be inside outer and outside inner
                float outerDist = outerSdf.GetSignedDistance(x, y);
                if (outerDist > 0)
                {
                    continue; // Outside outer shape
                }

                if (innerSdf != null)
                {
                    float innerDist = innerSdf.GetSignedDistance(x, y);
                    if (innerDist <= 0)
                    {
                        continue; // Inside inner shape (hollow part)
                    }
                }

                covered++;
            }
        }

        return CoverageToAlpha(covered, _totalSamples, _sourceAlpha);
    }

    public byte SampleStrokeEdgeCentered(
        int px,
        int py,
        ISdfCalculator outerSdf,
        ISdfCalculator? innerSdf,
        float halfSurfaceW,
        float halfSurfaceH)
    {
        int covered = 0;

        for (int sy = 0; sy < _factor; sy++)
        {
            float y = py + (sy + 0.5f) / _factor - halfSurfaceH;

            for (int sx = 0; sx < _factor; sx++)
            {
                float x = px + (sx + 0.5f) / _factor - halfSurfaceW;

                float outerDist = outerSdf.GetSignedDistance(x, y);
                if (outerDist > 0)
                {
                    continue;
                }

                if (innerSdf != null)
                {
                    float innerDist = innerSdf.GetSignedDistance(x, y);
                    if (innerDist <= 0)
                    {
                        continue;
                    }
                }

                covered++;
            }
        }

        return CoverageToAlpha(covered, _totalSamples, _sourceAlpha);
    }

    /// <summary>
    /// Samples edge for a rounded rectangle stroke using SDF-based alpha calculation.
    /// Provides smooth anti-aliasing at shape boundaries.
    /// </summary>
    /// <param name="px">Pixel X coordinate.</param>
    /// <param name="py">Pixel Y coordinate.</param>
    /// <param name="outerSdf">Outer rounded rectangle SDF.</param>
    /// <param name="innerSdf">Inner rounded rectangle SDF (can be null for filled stroke).</param>
    /// <param name="halfSurfaceW">Half of the surface width for coordinate transformation.</param>
    /// <param name="halfSurfaceH">Half of the surface height for coordinate transformation.</param>
    public byte SampleRoundedRectStrokeEdge(
        int px, int py,
        RoundedRectSdf outerSdf,
        RoundedRectSdf? innerSdf,
        float halfSurfaceW,
        float halfSurfaceH)
    {
        int covered = 0;

        for (int sy = 0; sy < _factor; sy++)
        {
            float y = py + (sy + 0.5f) / _factor - halfSurfaceH;

            for (int sx = 0; sx < _factor; sx++)
            {
                float x = px + (sx + 0.5f) / _factor - halfSurfaceW;

                // Must be inside outer and outside inner
                float outerDist = outerSdf.GetSignedDistance(x, y);
                if (outerDist > 0)
                {
                    continue; // Outside outer shape
                }

                if (innerSdf != null)
                {
                    float innerDist = innerSdf.GetSignedDistance(x, y);
                    if (innerDist <= 0)
                    {
                        continue; // Inside inner shape (hollow part)
                    }
                }

                covered++;
            }
        }

        return CoverageToAlpha(covered, _totalSamples, _sourceAlpha);
    }
}
