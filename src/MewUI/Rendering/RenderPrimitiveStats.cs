namespace Aprillz.MewUI.Rendering;

public readonly struct RenderPrimitiveStats
{
    public RenderPrimitiveStats(
        int saveCount,
        int restoreCount,
        int clipCount,
        int drawLineCount,
        int drawRectangleCount,
        int fillRectangleCount,
        int drawRoundedRectangleCount,
        int fillRoundedRectangleCount,
        int drawEllipseCount,
        int fillEllipseCount,
        int drawPathCount,
        int fillPathCount,
        int drawTextCount,
        int drawImageCount)
    {
        SaveCount = saveCount;
        RestoreCount = restoreCount;
        ClipCount = clipCount;
        DrawLineCount = drawLineCount;
        DrawRectangleCount = drawRectangleCount;
        FillRectangleCount = fillRectangleCount;
        DrawRoundedRectangleCount = drawRoundedRectangleCount;
        FillRoundedRectangleCount = fillRoundedRectangleCount;
        DrawEllipseCount = drawEllipseCount;
        FillEllipseCount = fillEllipseCount;
        DrawPathCount = drawPathCount;
        FillPathCount = fillPathCount;
        DrawTextCount = drawTextCount;
        DrawImageCount = drawImageCount;
    }

    public int SaveCount { get; }
    public int RestoreCount { get; }
    public int ClipCount { get; }
    public int DrawLineCount { get; }
    public int DrawRectangleCount { get; }
    public int FillRectangleCount { get; }
    public int DrawRoundedRectangleCount { get; }
    public int FillRoundedRectangleCount { get; }
    public int DrawEllipseCount { get; }
    public int FillEllipseCount { get; }
    public int DrawPathCount { get; }
    public int FillPathCount { get; }
    public int DrawTextCount { get; }
    public int DrawImageCount { get; }

    public int ShapeCount =>
        DrawLineCount +
        DrawRectangleCount +
        FillRectangleCount +
        DrawRoundedRectangleCount +
        FillRoundedRectangleCount +
        DrawEllipseCount +
        FillEllipseCount +
        DrawPathCount +
        FillPathCount;
}
