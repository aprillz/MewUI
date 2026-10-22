namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Per-frame rendering statistics captured after each render pass.
/// </summary>
/// <param name="DrawCalls">Draw calls issued.</param>
/// <param name="CullCount">Draw calls skipped by culling.</param>
/// <param name="PrimitiveStats">Per-primitive counts.</param>
public readonly record struct RenderStats(int DrawCalls, int CullCount, RenderPrimitiveStats PrimitiveStats)
{
    /// <summary>Creates statistics without per-primitive counts.</summary>
    public RenderStats(int drawCalls, int cullCount)
        : this(drawCalls, cullCount, default)
    {
    }

    /// <summary>Draw calls that were actually rendered.</summary>
    public int RenderedCalls => DrawCalls - CullCount;

    /// <summary>Culling ratio (0.0–1.0). 0 means nothing culled, 1 means everything culled.</summary>
    public double CullRatio => DrawCalls > 0 ? (double)CullCount / DrawCalls : 0;
}
