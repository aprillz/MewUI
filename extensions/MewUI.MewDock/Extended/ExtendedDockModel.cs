using System.Text.Json;

using Aprillz.MewUI.MewDock.Model;
using Aprillz.MewUI.MewDock.Model.Json;

using DockModel = Aprillz.MewUI.MewDock.Model.Model;

namespace Aprillz.MewUI.MewDock.Extended;

/// <summary>
/// The Extended (Visual Studio style) docking feature layer expressed as a <see cref="DockModel"/> subclass. It owns
/// everything the faithful model does not: the behavior overrides (borders are auto-hide only, tools dock into groups,
/// focus collapses reveals) and the dock-action reducer (pin / unpin / edge-dock) plus its dock sub-layout helpers.
/// The base model carries no feature flag - which subclass is instantiated decides the behavior.
/// </summary>
internal sealed class ExtendedDockModel : DockModel
{
    // Borders are auto-hide only, NOT drop targets - the faithful border search is replaced by the dock search:
    // pinned tool docks are edge regions of the main view, so their sub-layout trees are searched instead.
    internal override DropInfo? FindDropTargetNode(string layoutId, Node dragNode, double x, double y)
    {
        if (layoutId == MainLayoutId)
        {
            foreach (var (dockId, dockLayout) in Layouts)
            {
                if (dockLayout is DockLayout && dockLayout.RootRow is RowNode dockRoot)
                {
                    var dockDrop = dockRoot.FindDropTargetNode(dockId, dragNode, x, y);
                    if (dockDrop is not null)
                    {
                        return dockDrop;
                    }
                }
            }
        }
        return Layouts[layoutId].RootRow?.FindDropTargetNode(layoutId, dragNode, x, y);
    }

    internal override void OnActionApplied(DockAction action)
    {
        switch (action)
        {
            case SetActiveTabsetAction:
            {
                // Focusing any tabset auto-hides a revealed (expanded) auto-hide border.
                if (FocusedTabSet is not null)
                {
                    foreach (var border in BorderSet.Borders)
                    {
                        if (border.Selected != -1)
                        {
                            border.SetSelected(-1);
                        }
                    }
                }
                break;
            }
            case SelectTabAction a:
            {
                // Revealing a border tool focuses its pane: clear the tabset focus so nothing else is highlighted
                // (and the just-revealed border is not auto-hidden by a later focus change).
                if (GetNodeById(a.TabNodeId) is TabNode revealedTab
                    && revealedTab.Parent is BorderNode revealedBorder && revealedBorder.Selected != -1)
                {
                    FocusedTabSet = null;
                    // Auto-hide flyouts are mutually exclusive: revealing one border collapses any other revealed one.
                    foreach (var border in BorderSet.Borders)
                    {
                        if (!ReferenceEquals(border, revealedBorder) && border.Selected != -1)
                        {
                            border.SetSelected(-1);
                        }
                    }
                }
                break;
            }
            case PinToolAction a:
            {
                // Pin a border tool to its edge: join an existing tool dock there (as another tab), else create one.
                if (GetNodeById(a.NodeId) is TabNode toolTab && toolTab.Parent is BorderNode fromBorder)
                {
                    // Standard nesting: left/right docks are outer (full height), top/bottom are inner (between them).
                    bool outer = fromBorder.Location is DockLocation.Left or DockLocation.Right;
                    var target = FindOrCreateDock(fromBorder.Location, fromBorder.GetSize(), NextDockRank(outer));
                    fromBorder.Remove(toolTab);
                    target.Drop(toolTab, DockLocation.Center, target.Children.Count, select: true);
                }
                break;
            }
            case EdgeDockToolAction a:
            {
                // Dock a dragged tool (single tab or whole group) to a document-area edge: create/join the dock there.
                if (GetNodeById(a.NodeId) is Node dragNode && ModelUtils.IsPane(dragNode))
                {
                    var tabs = new List<TabNode>();
                    if (dragNode is TabNode singleTool)
                    {
                        tabs.Add(singleTool);
                    }
                    else
                    {
                        foreach (var child in dragNode.Children)
                        {
                            if (child is TabNode groupTool)
                            {
                                tabs.Add(groupTool);
                            }
                        }
                    }
                    if (tabs.Count > 0)
                    {
                        // Edge dock always creates a NEW stacked dock (join is the separate "tab" drop band).
                        var target = CreateDock(a.Edge, 220, NextDockRank(a.Outer));
                        foreach (var moveTool in tabs)
                        {
                            target.Drop(moveTool, DockLocation.Center, target.Children.Count, select: true);
                        }
                        Tidy();
                        PruneEmptyDocks();
                    }
                }
                break;
            }
            case UnpinToolAction a:
            {
                // Unpin a pinned tool group: move its tools to the auto-hide border on the dock's edge, collapse it,
                // and drop the dock sub-layout once it holds no tabsets.
                if (GetNodeById(a.NodeId) is TabSetNode toolSet
                    && toolSet.GetLayout() is DockLayout dockLayout
                    && BorderSet.BorderMap.TryGetValue(dockLayout.Edge, out var border))
                {
                    var tools = new List<TabNode>();
                    foreach (var child in toolSet.Children)
                    {
                        if (child is TabNode toolTab)
                        {
                            tools.Add(toolTab);
                        }
                    }
                    foreach (var toolTab in tools)
                    {
                        border.Drop(toolTab, DockLocation.Center, border.Children.Count);
                    }
                    border.SetSelected(-1);
                    Tidy();
                    if (dockLayout.RootRow is null || dockLayout.RootRow.Children.Count == 0)
                    {
                        Layouts.Remove(dockLayout.LayoutId);
                    }
                }
                break;
            }
        }
    }

    // First tabset in a node's subtree (depth-first); used to join a tool to an existing dock on an edge.
    private static TabSetNode? FindFirstTabSet(Node node)
    {
        if (node is TabSetNode tabSet)
        {
            return tabSet;
        }
        foreach (var child in node.Children)
        {
            if (FindFirstTabSet(child) is TabSetNode found)
            {
                return found;
            }
        }
        return null;
    }

    // The tool tabset of the Dock sub-layout pinned to an edge, joining an existing dock on that edge if there is
    // one (keeps its rank), else creating a fresh dock at the given rank. Used by pin (two border tools share a dock).
    private TabSetNode FindOrCreateDock(DockLocation edge, double size, double rank)
    {
        foreach (var (_, layout) in Layouts)
        {
            if (layout is DockLayout dock && dock.Edge == edge
                && dock.RootRow is RowNode root && FindFirstTabSet(root) is TabSetNode existing)
            {
                return existing;
            }
        }
        return CreateDock(edge, size, rank);
    }

    // Always create a fresh Dock sub-layout (a new stacked dock) at the given edge and nesting rank.
    private TabSetNode CreateDock(DockLocation edge, double size, double rank)
    {
        var layoutId = NextUniqueId();
        var dockLayout = new DockLayout(layoutId, edge)
        {
            Size = size,
            DockRank = rank,
        };
        var dockRow = new RowNode(this);
        dockLayout.SetRootRow(dockRow);
        Layouts[layoutId] = dockLayout;
        var tabSet = new TabSetNode(this);
        dockRow.AddChild(tabSet);
        return tabSet;
    }

    // A nesting rank for a new dock: outer = below every existing dock (reserved first, full extent), inner = above.
    private double NextDockRank(bool outer)
    {
        bool any = false;
        double min = 0, max = 0;
        foreach (var (_, layout) in Layouts)
        {
            if (layout is not DockLayout dock)
            {
                continue;
            }
            if (!any)
            {
                min = max = dock.DockRank;
                any = true;
            }
            else
            {
                min = Math.Min(min, dock.DockRank);
                max = Math.Max(max, dock.DockRank);
            }
        }
        if (!any)
        {
            return 0;
        }
        return outer ? min - 1 : max + 1;
    }

    // Drop Dock sub-layouts that no longer hold any tabset (after a tool moved/unpinned out of them).
    private void PruneEmptyDocks()
    {
        foreach (var (id, layout) in Layouts.ToList())
        {
            if (layout is DockLayout && (layout.RootRow is null || layout.RootRow.Children.Count == 0))
            {
                Layouts.Remove(id);
            }
        }
    }

    private protected override void BuildSubLayout(string id, JsonSubLayout json)
    {
        if (json.Type != "dock")
        {
            base.BuildSubLayout(id, json);
            return;
        }
        var layout = new DockLayout(id, DockLocationExtensions.GetByName(json.Edge ?? "left"))
        {
            Size = json.Size ?? 0,
            DockRank = json.DockRank ?? 0,
        };
        layout.SetRootRow(BuildRowNode(json.Layout, layout));
        Layouts[id] = layout;
    }

    private protected override JsonSubLayout SubLayoutToJson(Layout layout, RowNode rootRow)
    {
        if (layout is not DockLayout dock)
        {
            return base.SubLayoutToJson(layout, rootRow);
        }
        return new JsonSubLayout
        {
            Type = "dock",
            Edge = dock.Edge.GetName(),
            Size = dock.Size,
            DockRank = dock.DockRank,
            Layout = RowToJson(rootRow),
        };
    }

    public static new ExtendedDockModel FromJson(JsonModel json)
    {
        var model = new ExtendedDockModel();
        model.LoadFrom(json);
        model.StampBorderPanes();
        return model;
    }

    // Tabs living in an auto-hide border are panes. Stamp the data on load (legacy/faithful JSON has no per-tab
    // marking) so pane-ness travels with the tab through pin / edge-dock / popout moves.
    private void StampBorderPanes()
    {
        foreach (var border in BorderSet.Borders)
        {
            foreach (var child in border.Children)
            {
                if (child is TabNode tab)
                {
                    tab.IsDocument = false;
                }
            }
        }
    }

    public static new ExtendedDockModel FromJson(string json) =>
        FromJson(JsonSerializer.Deserialize(json, MewDockJsonContext.Default.JsonModel)!);
}
