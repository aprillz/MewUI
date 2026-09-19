using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Rendering.Retained;

/// <summary>
/// One invalidation on its way from the visual that raised it to the surface that draws it. It says
/// what changed where it changed, so nothing on the way has to work that out from how it was reached.
/// </summary>
internal struct RenderDirtyRequest(UIElement origin, RenderDirtyKind kind)
{
    /// <summary>The visual whose change this is.</summary>
    internal UIElement Origin { get; } = origin;

    internal RenderDirtyKind Kind { get; } = kind;

    /// <summary>
    /// The outermost visual on the way up that is recorded as one compatibility subtree. Its recording
    /// holds the origin's drawing, so the change is its content change whatever kind the origin raised.
    /// </summary>
    internal UIElement? CompatibilityOwner { get; set; }
}
