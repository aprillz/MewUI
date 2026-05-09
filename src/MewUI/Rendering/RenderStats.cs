namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Per-frame rendering statistics captured after each render pass.
/// </summary>
public readonly record struct RenderStats(int DrawCalls, int CullCount, RenderPrimitiveStats PrimitiveStats)
{
    public RenderStats(int drawCalls, int cullCount)
        : this(drawCalls, cullCount, default)
    {
    }

    /// <summary>Draw calls that were actually rendered.</summary>
    public int RenderedCalls => DrawCalls - CullCount;

    /// <summary>Culling ratio (0.0–1.0). 0 means nothing culled, 1 means everything culled.</summary>
    public double CullRatio => DrawCalls > 0 ? (double)CullCount / DrawCalls : 0;
}
