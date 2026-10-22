namespace Aprillz.MewUI.Rendering;

/// <summary>Counts of drawing calls recorded for one frame.</summary>
public readonly struct RenderPrimitiveStats
{
    /// <summary>Creates the counts.</summary>
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

    /// <summary>Gets the number of state saves.</summary>
    public int SaveCount { get; }
    /// <summary>Gets the number of state restores.</summary>
    public int RestoreCount { get; }
    /// <summary>Gets the number of clip changes.</summary>
    public int ClipCount { get; }
    /// <summary>Gets the number of line draws.</summary>
    public int DrawLineCount { get; }
    /// <summary>Gets the number of rectangle outline draws.</summary>
    public int DrawRectangleCount { get; }
    /// <summary>Gets the number of rectangle fills.</summary>
    public int FillRectangleCount { get; }
    /// <summary>Gets the number of rounded rectangle outline draws.</summary>
    public int DrawRoundedRectangleCount { get; }
    /// <summary>Gets the number of rounded rectangle fills.</summary>
    public int FillRoundedRectangleCount { get; }
    /// <summary>Gets the number of ellipse outline draws.</summary>
    public int DrawEllipseCount { get; }
    /// <summary>Gets the number of ellipse fills.</summary>
    public int FillEllipseCount { get; }
    /// <summary>Gets the number of path outline draws.</summary>
    public int DrawPathCount { get; }
    /// <summary>Gets the number of path fills.</summary>
    public int FillPathCount { get; }
    /// <summary>Gets the number of text draws.</summary>
    public int DrawTextCount { get; }
    /// <summary>Gets the number of image draws.</summary>
    public int DrawImageCount { get; }

    /// <summary>Gets the total of the shape draws and fills (lines, rectangles, rounded rectangles, ellipses, paths).</summary>
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
