using System.Text.Json;

using Aprillz.MewUI.MewDock.Model.Json;

namespace Aprillz.MewUI.MewDock.Model;

/// <summary>
/// A typed model action (port of FlexLayout model/Actions.ts). The original's string-type + untyped-data-bag pair
/// is replaced by a record per action, which <see cref="Model.DoAction"/> pattern-matches. The concrete records are
/// internal; callers create actions through the static factory methods here and dispatch via
/// <c>DockingManager.PerformAction</c>. Named <c>DockAction</c> (not <c>Action</c>) to avoid the <see cref="Action"/>
/// clash.
/// </summary>
internal abstract record DockAction
{
    /// <summary>Add a new tab built from <paramref name="json"/> onto a tabset, border, or row at the given
    /// location and index (index -1 = append); <paramref name="select"/> selects it after insertion.</summary>
    public static DockAction AddTab(JsonTabNode json, string toNodeId, DockLocation location, int index, bool? select = null) =>
        new AddTabAction(json, toNodeId, location, index, select);

    /// <summary>Move an existing tab or tabset onto a target node at the given location and index (index -1 = append).</summary>
    public static DockAction MoveNode(string fromNodeId, string toNodeId, DockLocation location, int index, bool? select = null) =>
        new MoveNodeAction(fromNodeId, toNodeId, location, index, select);

    /// <summary>Remove a tab; closes its content and drops its sub-layout if it has one.</summary>
    public static DockAction DeleteTab(string tabNodeId) => new DeleteTabAction(tabNodeId);

    /// <summary>Remove a whole tabset and every tab in it.</summary>
    public static DockAction DeleteTabset(string tabsetNodeId) => new DeleteTabsetAction(tabsetNodeId);

    /// <summary>Rename a tab; an empty name clears it (display falls back to the unnamed-tab string).</summary>
    public static DockAction RenameTab(string tabNodeId, string text) => new RenameTabAction(tabNodeId, text);

    /// <summary>Select (activate) a tab within its tabset or border.</summary>
    public static DockAction SelectTab(string tabNodeId) => new SelectTabAction(tabNodeId);

    /// <summary>Set the focused tabset (the one drawn with the accent); null clears the focus. Null
    /// <paramref name="layoutId"/> targets the main layout.</summary>
    public static DockAction SetActiveTabset(string? tabsetNodeId, string? layoutId = null) =>
        new SetActiveTabsetAction(tabsetNodeId, layoutId);

    /// <summary>Set the relative split weights of a row's children, resizing the splits between them.</summary>
    public static DockAction AdjustWeights(string nodeId, double[] weights) => new AdjustWeightsAction(nodeId, weights);

    /// <summary>Set a border's split size (the extent of its reveal panel).</summary>
    public static DockAction AdjustBorderSplit(string nodeId, double size) => new AdjustBorderSplitAction(nodeId, size);

    /// <summary>Toggle a tabset between maximized (full layout) and normal. Null <paramref name="layoutId"/>
    /// targets the main layout.</summary>
    public static DockAction MaximizeToggle(string tabsetNodeId, string? layoutId = null) =>
        new MaximizeToggleAction(tabsetNodeId, layoutId);

    /// <summary>Replace the global model attributes (the defaults applied to every node).</summary>
    public static DockAction UpdateModelAttributes(JsonGlobal attributes) => new UpdateModelAttributesAction(attributes);

    /// <summary>Merge per-node attributes (name, component, enable flags, config, ...) onto one node.</summary>
    public static DockAction UpdateNodeAttributes(string nodeId, JsonElement attributes) =>
        new UpdateNodeAttributesAction(nodeId, attributes);

    /// <summary>Pop a single tab out into its own window (or sub-layout). <paramref name="position"/> is the
    /// physical screen point used for window placement.</summary>
    public static DockAction PopoutTab(string nodeId, LayoutType type = LayoutType.Window, Point? position = null) =>
        new PopoutTabAction(nodeId, type, position);

    /// <summary>Pop a whole tabset out into its own window (or sub-layout).</summary>
    public static DockAction PopoutTabset(string nodeId, LayoutType type = LayoutType.Window, Point? position = null) =>
        new PopoutTabsetAction(nodeId, type, position);

    /// <summary>Close a popout window / sub-layout by its layout id; its tabs are removed with it.</summary>
    public static DockAction ClosePopout(string layoutId) => new ClosePopoutAction(layoutId);

    /// <summary>Bring a popout window to the front.</summary>
    public static DockAction MovePopoutToFront(string layoutId) => new MovePopoutToFrontAction(layoutId);

    /// <summary>Create a new sub-layout from a row tree at <paramref name="rect"/> (e.g. a pre-populated popout).</summary>
    public static DockAction CreateSubLayout(JsonRowNode layout, JsonRect rect, LayoutType type) =>
        new CreateSubLayoutAction(layout, rect, type);

    /// <summary>Extended docking: pin an auto-hide border tool into a docked group on that edge.</summary>
    public static DockAction PinTool(string nodeId) => new PinToolAction(nodeId);

    /// <summary>Extended docking: unpin a docked tool group back to the auto-hide border on its edge.</summary>
    public static DockAction UnpinTool(string nodeId) => new UnpinToolAction(nodeId);

    /// <summary>Extended docking: dock a dragged tool to a document-area edge. Outer = full extent (reserved
    /// first); inner = between the perpendicular docks.</summary>
    public static DockAction EdgeDockTool(string nodeId, DockLocation edge, bool outer) =>
        new EdgeDockToolAction(nodeId, edge, outer);
}

internal sealed record AddTabAction(JsonTabNode Json, string ToNodeId, DockLocation Location, int Index, bool? Select = null) : DockAction;

internal sealed record MoveNodeAction(string FromNodeId, string ToNodeId, DockLocation Location, int Index, bool? Select = null) : DockAction;

internal sealed record DeleteTabAction(string TabNodeId) : DockAction;

internal sealed record DeleteTabsetAction(string TabsetNodeId) : DockAction;

internal sealed record RenameTabAction(string TabNodeId, string Text) : DockAction;

internal sealed record SelectTabAction(string TabNodeId) : DockAction;

internal sealed record SetActiveTabsetAction(string? TabsetNodeId, string? LayoutId = null) : DockAction;

internal sealed record AdjustWeightsAction(string NodeId, double[] Weights) : DockAction;

internal sealed record AdjustBorderSplitAction(string NodeId, double Size) : DockAction;

internal sealed record MaximizeToggleAction(string TabsetNodeId, string? LayoutId = null) : DockAction;

internal sealed record UpdateModelAttributesAction(JsonGlobal Attributes) : DockAction;

internal sealed record UpdateNodeAttributesAction(string NodeId, JsonElement Attributes) : DockAction;

internal sealed record PopoutTabAction(string NodeId, LayoutType Type = LayoutType.Window, Point? Position = null) : DockAction;

internal sealed record PopoutTabsetAction(string NodeId, LayoutType Type = LayoutType.Window, Point? Position = null) : DockAction;

internal sealed record ClosePopoutAction(string LayoutId) : DockAction;

internal sealed record MovePopoutToFrontAction(string LayoutId) : DockAction;

internal sealed record CreateSubLayoutAction(JsonRowNode Layout, JsonRect Rect, LayoutType Type) : DockAction;

/// <summary>Pin a tool tab (currently in a border) into a Dock sub-layout on that edge.</summary>
internal sealed record PinToolAction(string NodeId) : DockAction;

/// <summary>Unpin a pinned tool group (tabset) back to the auto-hide border on its edge.</summary>
internal sealed record UnpinToolAction(string NodeId) : DockAction;

/// <summary>Dock a dragged tool (tab or tool group) to a document-area edge. Outer = full extent (reserved first);
/// inner = between the perpendicular docks.</summary>
internal sealed record EdgeDockToolAction(string NodeId, DockLocation Edge, bool Outer) : DockAction;
