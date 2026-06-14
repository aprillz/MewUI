using Aprillz.MewUI.MewDock.Model;

namespace Aprillz.MewUI.MewDock.Extended;

/// <summary>
/// A pinned dock: a sub-layout reserved as an edge region of the document area (Extended docking). Carries the
/// placement data the base <see cref="Layout"/> does not know: the pinned edge, the size along that edge, and the
/// nesting rank among docks.
/// </summary>
internal sealed class DockLayout : Layout
{
    internal DockLayout(string layoutId, DockLocation edge)
        : base(layoutId, LayoutType.Dock, Rect.Empty)
    {
        Edge = edge;
    }

    /// <summary>The document-area edge this dock is pinned to.</summary>
    internal DockLocation Edge { get; }

    /// <summary>The dock's size along its edge (0 = the default size).</summary>
    internal double Size { get; set; }

    // Nesting rank among docks: lower = reserved first = OUTER (full extent); higher = INNER (between perpendicular
    // docks). The drag drop position picks outer vs inner, so e.g. a bottom dock can be full-width or fit between.
    internal double DockRank { get; set; }
}
