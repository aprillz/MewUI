using Aprillz.MewUI.Rendering.Gdi.Sdf;

namespace Aprillz.MewUI.Rendering.Gdi.Rendering;

/// <summary>
/// Interface for edge sampling strategies used in anti-aliasing.
/// </summary>
internal interface IEdgeSampler
{
    /// <summary>
    /// Computes the alpha value for a pixel at complex edges.
    /// </summary>
    /// <param name="px">Pixel X coordinate.</param>
    /// <param name="py">Pixel Y coordinate.</param>
    /// <param name="sdf">The SDF calculator for the shape.</param>
    /// <param name="aaWidth">Anti-aliasing width.</param>
    /// <returns>Alpha value between 0 and 255.</returns>
    byte SampleEdge(int px, int py, ISdfCalculator sdf, float aaWidth);

    /// <summary>
    /// Gets the number of samples used per pixel.
    /// </summary>
    int SampleCount { get; }
}

/// <summary>
/// Base class for edge samplers with common functionality.
/// </summary>
internal abstract class EdgeSamplerBase : IEdgeSampler
{
    public abstract byte SampleEdge(int px, int py, ISdfCalculator sdf, float aaWidth);
    public abstract int SampleCount { get; }

    /// <summary>
    /// Converts coverage ratio to alpha value.
    /// </summary>
    protected static byte CoverageToAlpha(int covered, int totalSamples, byte srcAlpha)
    {
        if (covered <= 0)
        {
            return 0;
        }

        int alpha = (srcAlpha * covered + (totalSamples / 2)) / totalSamples;
        return (byte)Math.Min(255, alpha);
    }
}
