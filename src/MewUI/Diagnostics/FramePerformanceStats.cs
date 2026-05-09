using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Diagnostics;

public readonly struct FramePerformanceStats
{
    public FramePerformanceStats(
        long frameIndex,
        long sourceId,
        double frameMs,
        double beginFrameMs,
        double animationMs,
        double layoutMs,
        double measureMs,
        double arrangeMs,
        double renderBodyMs,
        double devToolsMs,
        double endFrameMs,
        double presentMs,
        int drawCalls,
        int cullCount,
        RenderPrimitiveStats primitiveStats,
        long allocatedBytes,
        int gen0Collections,
        int gen1Collections,
        int gen2Collections,
        bool layoutRan,
        bool measureRan,
        bool arrangeRan)
    {
        FrameIndex = frameIndex;
        SourceId = sourceId;
        FrameMs = frameMs;
        BeginFrameMs = beginFrameMs;
        AnimationMs = animationMs;
        LayoutMs = layoutMs;
        MeasureMs = measureMs;
        ArrangeMs = arrangeMs;
        RenderBodyMs = renderBodyMs;
        DevToolsMs = devToolsMs;
        EndFrameMs = endFrameMs;
        PresentMs = presentMs;
        DrawCalls = drawCalls;
        CullCount = cullCount;
        PrimitiveStats = primitiveStats;
        AllocatedBytes = allocatedBytes;
        Gen0Collections = gen0Collections;
        Gen1Collections = gen1Collections;
        Gen2Collections = gen2Collections;
        LayoutRan = layoutRan;
        MeasureRan = measureRan;
        ArrangeRan = arrangeRan;
    }

    public long FrameIndex { get; }
    public long SourceId { get; }
    public double FrameMs { get; }
    public double BeginFrameMs { get; }
    public double AnimationMs { get; }
    public double LayoutMs { get; }
    public double MeasureMs { get; }
    public double ArrangeMs { get; }
    public double RenderBodyMs { get; }
    public double DevToolsMs { get; }
    public double EndFrameMs { get; }
    public double PresentMs { get; }
    public int DrawCalls { get; }
    public int CullCount { get; }
    public RenderPrimitiveStats PrimitiveStats { get; }
    public long AllocatedBytes { get; }
    public int Gen0Collections { get; }
    public int Gen1Collections { get; }
    public int Gen2Collections { get; }
    public bool LayoutRan { get; }
    public bool MeasureRan { get; }
    public bool ArrangeRan { get; }

    public bool IsEmpty => FrameIndex == 0 && FrameMs == 0;
}
