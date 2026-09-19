namespace Aprillz.MewUI.Rendering;

/// <summary>
/// A context that can reset a rectangle of its target to fully transparent, alpha included.
/// Repainting part of a transparent window needs this: filling the box with a colour leaves the
/// alpha that was already there, so the old pixels show through. A context that does not implement
/// it keeps its window on the whole-frame immediate path.
/// </summary>
internal interface ITransparentDamageContext
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
/// Repainting part of an opaque window needs this so the damaged box starts from the window
/// background instead of the pixels the previous frame left behind.
/// </summary>
internal interface IOpaqueDamageContext
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
