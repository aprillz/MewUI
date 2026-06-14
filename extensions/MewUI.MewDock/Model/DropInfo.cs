namespace Aprillz.MewUI.MewDock.Model;

/// <summary>The style of outline to draw for a drop target (replaces FlexLayout's CSS outline class names).</summary>
internal enum DropOutlineKind
{
    /// <summary>A normal drop zone (the original FLEXLAYOUT__OUTLINE_RECT).</summary>
    Standard,

    /// <summary>A frame-edge dock zone (the original FLEXLAYOUT__OUTLINE_RECT_EDGE).</summary>
    Edge,
}

/// <summary>
/// Describes where a dragged node will land (port of FlexLayout model/DropInfo.ts). <see cref="Node"/> is always
/// a drop target (row/tabset/border). <see cref="Outline"/> selects the outline style for the view.
/// </summary>
internal sealed record DropInfo(Node Node, Rect Rect, DockLocation Location, int Index, DropOutlineKind Outline);
