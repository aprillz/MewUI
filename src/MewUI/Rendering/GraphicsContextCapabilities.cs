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
