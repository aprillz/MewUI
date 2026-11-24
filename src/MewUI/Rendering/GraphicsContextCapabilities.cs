namespace Aprillz.MewUI.Rendering;

/// <summary>
/// A context that can reset a rectangle of its target to fully transparent, alpha included.
/// Repainting part of a transparent window needs this: filling the box with a colour leaves the
/// alpha that was already there, so the old pixels show through. A context that does not implement
/// it keeps its window on the whole-frame immediate path.
/// </summary>
internal interface ITransparentDirtyRectContext
{
    /// <summary>
    /// Writes zero to every channel of <paramref name="rect"/>, which is in the target's own
    /// coordinates: the caller invokes this with no transform in effect. Callers pass pixel-snapped
    /// rectangles and the edges are not antialiased.
    /// </summary>
    void ClearRectangleToTransparent(Rect rect);
}

/// <summary>
/// A context that can copy what its target holds partway through a frame. A backdrop needs this: it
/// filters the pixels drawn under it so far.
/// </summary>
internal interface ITargetSnapshotContext
{
    /// <summary>
    /// Gives the target's size in pixels and whether it carries alpha, or returns false when the target
    /// cannot be read at this point of the frame, such as while drawing is held back from it.
    /// </summary>
    bool TryGetSnapshotTarget(out int pixelWidth, out int pixelHeight, out bool hasAlpha);

    /// <summary>
    /// Copies the given pixels of the target, with everything drawn so far, to
    /// <paramref name="destination"/> at the given pixel of it, and returns true; returns false, leaving
    /// the destination as it was, when it cannot. Both rectangles are in pixels with the top row first,
    /// and the destination is a surface made by the same factory.
    /// </summary>
    bool TryCopyTargetPixels(int left, int top, int width, int height, IRenderSurface destination, int destinationLeft, int destinationTop);
}

/// <summary>
/// A target snapshot context that holds drawing under some clips back from its target until the clip
/// closes. A read of the target is bracketed by <see cref="BeginTargetRead"/> and
/// <see cref="EndTargetRead"/>: in between, the target holds everything drawn so far, and nothing is
/// drawn to it until the read ends.
/// </summary>
internal interface ITargetReadScopeContext
{
    /// <summary>Lets the drawing held back under open clips through to the target, keeping the clips for after the read.</summary>
    void BeginTargetRead();

    /// <summary>Puts back the clips <see cref="BeginTargetRead"/> let through, as they were.</summary>
    void EndTargetRead();
}

/// <summary>
/// A context that can overwrite a rectangle of its target with one colour, alpha included.
/// Repainting part of an opaque window needs this so the dirty box starts from the window
/// background instead of the pixels the previous frame left behind.
/// </summary>
internal interface IOpaqueDirtyRectContext
{
    /// <summary>
    /// Writes <paramref name="color"/> to every channel of <paramref name="rect"/>, which is in the
    /// target's own coordinates: the caller invokes this with no transform in effect. Callers pass
    /// pixel-snapped rectangles and the edges are not antialiased.
    /// </summary>
    void ClearRectangle(Rect rect, Color color);
}

/// <summary>
/// A context whose frame ends by copying a buffer of its own to the window. When the window still
/// shows the previous frame, the caller can say which areas this frame changed so that only those
/// are copied; no areas means nothing is. The areas are in the target's own coordinates and hold for
/// the current frame only.
/// </summary>
internal interface IPartialPresentContext
{
    void LimitPresentTo(IReadOnlyList<Rect> areas);
}

/// <summary>
/// A context that can copy part of a surface onto its target unchanged. Presenting a kept frame is
/// such a copy, and doing it as one skips the per-pixel paint work of drawing the surface as an image.
/// </summary>
internal interface ISurfaceCopyContext
{
    /// <summary>
    /// Replaces <paramref name="area"/> of the target with the same area of <paramref name="source"/>
    /// and returns true, or draws nothing and returns false when this context cannot copy from that
    /// source. The area is in the target's own coordinates, the caller invokes this with no transform
    /// in effect, and source and target share one pixel grid.
    /// </summary>
    bool TryCopySurface(IRenderSurface source, Rect area);
}

/// <summary>
/// A context that can confine an opaque backdrop scope to the box its owner fills. A scope that does
/// not know its box has to answer for the whole target, and a backend that realizes the scope as a
/// layer then blends everything translucent on the target once more when the scope closes. Told the
/// box, it touches only that. The scope is closed with <see cref="IGraphicsContext.EndOpaqueBackdrop"/>.
/// </summary>
internal interface IBoundedOpaqueBackdropContext
{
    /// <summary>Opens the scope over <paramref name="box"/>, given in the context's current coordinates.</summary>
    void BeginOpaqueBackdrop(Rect box);
}

/// <summary>Opens an opaque backdrop scope, confined to the box when the context can do that.</summary>
internal static class OpaqueBackdropScope
{
    internal static void Begin(IGraphicsContext context, Rect box)
    {
        if (context is IBoundedOpaqueBackdropContext bounded)
        {
            bounded.BeginOpaqueBackdrop(box);
        }
        else
        {
            context.BeginOpaqueBackdrop();
        }
    }
}
