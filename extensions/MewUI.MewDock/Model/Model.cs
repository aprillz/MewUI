using Aprillz.MewUI.MewDock.Model.Json;

namespace Aprillz.MewUI.MewDock.Model;

/// <summary>Default min/max node sizes (port of the module consts DefaultMin/DefaultMax in Model.ts).</summary>
internal static class ModelDefaults
{
    public const double Min = 1;
    public const double Max = 99999;
}

/// <summary>
/// Root of the layout tree and action dispatcher (port of FlexLayout model/Model.ts). Popout/sub-layout actions
/// are deferred to Phase 5; the rest of the reducer, the node tree, idMap and global attribute defaults are here.
/// </summary>
internal partial class Model
{
    public const string MainLayoutId = "__main_layout_id__";

    private readonly Dictionary<string, Layout> _layouts = new();
    private readonly Layout _mainLayout;
    private readonly BorderSet _borders;
    private readonly Dictionary<string, Node> _idMap = new();
    private readonly List<Action<DockAction>> _changeListeners = new();

    public Model()
    {
        _mainLayout = new Layout(MainLayoutId, LayoutType.Window, Rect.Empty);
        _layouts[MainLayoutId] = _mainLayout;
        _borders = new BorderSet(this);
    }

    internal Layout MainLayout => _mainLayout;

    internal Dictionary<string, Layout> Layouts => _layouts;

    public BorderSet BorderSet => _borders;

    // Global attribute defaults that nodes fall back to when their own value is unset.
    public bool IsRootOrientationVertical { get; internal set; }

    public bool EnableEdgeDock { get; set; } = true;

    /// <summary>Shows explicit edge markers for document edge-dock (and narrows the bare edge band to match).
    /// A runtime view option - not part of the serialized layout.</summary>
    public bool EnableEdgeDockIndicators { get; set; } = true;

    public bool TabSetEnableClose { get; set; } = true;

    public bool TabSetEnableDeleteWhenEmpty { get; set; } = true;

    public bool TabSetEnableDrop { get; set; } = true;

    public bool TabSetEnableDrag { get; set; } = true;

    public bool TabSetEnableDivide { get; set; } = true;

    public bool TabSetEnableMaximize { get; set; } = true;

    public bool TabSetAutoSelectTab { get; set; } = true;

    public double TabSetMinWidth { get; set; }

    public double TabSetMinHeight { get; set; }

    public double TabSetMaxWidth { get; set; } = ModelDefaults.Max;

    public double TabSetMaxHeight { get; set; } = ModelDefaults.Max;

    public bool TabEnableClose { get; set; } = true;

    public bool TabEnableDrag { get; set; } = true;

    public bool TabEnableRename { get; set; } = true;

    // Default true so tabs may be docked into popped-out (window-type) layouts; set per-node enablePopout=false
    // to pin a tab to the main window. (FlexLayout defaults this false; a dock that features popouts wants it on.)
    public bool TabEnablePopout { get; set; } = true;

    public bool TabEnablePopoutIcon { get; set; } = true;

    public bool TabEnableRenderOnDemand { get; set; } = true;

    public CloseType TabCloseType { get; set; } = CloseType.Visible;

    public double TabBorderWidth { get; set; } = -1;

    public double TabBorderHeight { get; set; } = -1;

    public double TabMinWidth { get; set; }

    public double TabMinHeight { get; set; }

    public double TabMaxWidth { get; set; } = ModelDefaults.Max;

    public double TabMaxHeight { get; set; } = ModelDefaults.Max;

    public string? TabIcon { get; set; }

    public bool BorderEnableDrop { get; set; } = true;

    public bool BorderEnableAutoHide { get; set; }

    public bool BorderAutoSelectTabWhenOpen { get; set; } = true;

    public bool BorderAutoSelectTabWhenClosed { get; set; }

    public bool BorderEnableTabScrollbar { get; set; }

    public double BorderSize { get; set; } = 200;

    public double BorderMinSize { get; set; }

    public double BorderMaxSize { get; set; } = ModelDefaults.Max;

    /// <summary>Optional custom validator consulted before a drop is allowed.</summary>
    public Func<Node, DropInfo, bool>? OnAllowDrop { get; set; }

    /// <summary>Optional callback to style a newly created tabset (attributes applied in a later step).</summary>
    internal Func<TabNode?, object?>? OnCreateTabSet { get; set; }

    public double SplitterSize { get; set; } = 6;

    internal string NextUniqueId() => "#" + Guid.NewGuid().ToString();

    internal void AddNode(Node node) => _idMap[node.GetId()] = node;

    public Node? GetNodeById(string id) => _idMap.TryGetValue(id, out var node) ? node : null;

    internal RowNode GetRootRow(string layoutId = MainLayoutId) => _layouts[layoutId].RootRow!;

    internal TabSetNode? GetMaximizedTabset(string layoutId = MainLayoutId) => _layouts[layoutId].MaximizedTabSet;

    internal void SetMaximizedTabset(TabSetNode? value, string layoutId = MainLayoutId) =>
        _layouts[layoutId].MaximizedTabSet = value;

    internal TabSetNode? GetActiveTabset(string layoutId = MainLayoutId)
    {
        _layouts.TryGetValue(layoutId, out var layout);
        if (layout?.ActiveTabSet is TabSetNode active && GetNodeById(active.GetId()) is not null)
        {
            return active;
        }
        return null;
    }

    internal void SetActiveTabset(TabSetNode? value, string layoutId = MainLayoutId)
    {
        _layouts[layoutId].ActiveTabSet = value;
        FocusedTabSet = value; // keep the single focus in sync on every path (drop/move/maximize, not just clicks)
    }

    // The single focused tabset across all layouts (tool docks + document); drives the active-frame highlight.
    // Setter is open to the feature layer (ExtendedDockModel adjusts focus in its reducer post-pass).
    internal TabSetNode? FocusedTabSet { get; private protected set; }

    public void AddChangeListener(Action<DockAction> listener) => _changeListeners.Add(listener);

    public void RemoveChangeListener(Action<DockAction> listener) => _changeListeners.Remove(listener);

    /// <summary>Visits the borders and every layout's tree (port of visitNodes).</summary>
    public void VisitNodes(Action<Node, int> fn)
    {
        _borders.ForEachNode(fn);
        foreach (var layout in _layouts.Values)
        {
            layout.RootRow?.ForEachNode(fn, 0);
        }
    }

    internal void UpdateIdMap()
    {
        _idMap.Clear();
        VisitNodes((node, level) => _idMap[node.GetId()] = node);
    }

    // Tear-off drag: the node being dragged (a tab or a tabset) is hidden from the layout so its spot reflows once,
    // while its source view stays alive for the drag. Cleared on drop (commit), pop-out, or ESC/cancel (restore).
    internal Node? DraggingNode { get; private set; }

    internal event Action? DraggingChanged;

    internal void SetDraggingNode(Node? node)
    {
        if (ReferenceEquals(node, DraggingNode))
        {
            return;
        }
        DraggingNode = node;
        DraggingChanged?.Invoke();
    }

    internal void Tidy()
    {
        foreach (var layout in _layouts.Values.ToList())
        {
            layout.RootRow?.Tidy();
        }
    }

    // Reducer extension seam: called once per dispatched action, after the faithful switch has applied it and
    // before the id-map rebuild + change notification, so feature effects (Extended dock actions, focus post-
    // effects) land in the same transaction. The only other seam is FindDropTargetNode below; the data model
    // holds no feature flags - behavior is decided polymorphically by which Model subclass is in use.
    internal virtual void OnActionApplied(DockAction action) { }

    /// <summary>Finds the drop target under (x, y) for a drag: the borders (main layout) then the layout tree.
    /// ExtendedDockModel replaces the border search with the dock sub-layout search (borders are auto-hide only).</summary>
    internal virtual DropInfo? FindDropTargetNode(string layoutId, Node dragNode, double x, double y)
    {
        if (layoutId == MainLayoutId)
        {
            var borderDrop = _borders.FindDropTargetNode(dragNode, x, y);
            if (borderDrop is not null)
            {
                return borderDrop;
            }
        }
        return _layouts[layoutId].RootRow?.FindDropTargetNode(layoutId, dragNode, x, y);
    }

    /// <summary>Dispatches an action, mutating the tree, then rebuilds the id map and notifies listeners.</summary>
    public object? DoAction(DockAction action)
    {
        object? returnVal = null;

        switch (action)
        {
            case AddTabAction a:
            {
                var node = BuildTabNode(a.Json, addToModel: true);
                if (GetNodeById(a.ToNodeId) is Node toNode && toNode is TabSetNode or BorderNode or RowNode)
                {
                    toNode.Drop(node, a.Location, a.Index, a.Select);
                }
                returnVal = node;
                break;
            }
            case MoveNodeAction a:
            {
                var fromNode = GetNodeById(a.FromNodeId);
                if (fromNode is not null && GetNodeById(a.ToNodeId) is Node toNode && toNode is TabSetNode or BorderNode or RowNode)
                {
                    toNode.Drop(fromNode, a.Location, a.Index, a.Select);
                }
                break;
            }
            case DeleteTabAction a:
                if (GetNodeById(a.TabNodeId) is TabNode tab)
                {
                    tab.Delete();
                }
                break;
            case DeleteTabsetAction a:
                if (GetNodeById(a.TabsetNodeId) is TabSetNode tabset)
                {
                    foreach (var child in tabset.Children.ToList())
                    {
                        if (((TabNode)child).IsEnableClose)
                        {
                            ((TabNode)child).Delete();
                        }
                    }
                    if (tabset.Children.Count == 0)
                    {
                        tabset.Delete();
                    }
                    Tidy();
                }
                break;
            case SelectTabAction a:
                if (GetNodeById(a.TabNodeId) is TabNode selectTab)
                {
                    var parent = selectTab.Parent!;
                    int pos = parent.IndexOfChild(selectTab);
                    if (parent is BorderNode border)
                    {
                        border.SetSelected(border.Selected == pos ? -1 : pos);
                    }
                    else if (parent is TabSetNode parentTabSet)
                    {
                        if (parentTabSet.Selected != pos)
                        {
                            parentTabSet.SetSelected(pos);
                        }
                        selectTab.GetLayout().ActiveTabSet = parentTabSet;
                        FocusedTabSet = parentTabSet;
                    }
                }
                break;
            case SetActiveTabsetAction a:
            {
                string layoutId = a.LayoutId ?? MainLayoutId;
                var layout = _layouts[layoutId];
                var ts = a.TabsetNodeId is not null && GetNodeById(a.TabsetNodeId) is TabSetNode tabSet ? tabSet : null;
                layout.ActiveTabSet = ts;
                FocusedTabSet = ts; // single focus across every layout (tool docks + document)
                break;
            }
            case AdjustWeightsAction a:
                if (GetNodeById(a.NodeId) is RowNode row)
                {
                    var children = row.Children;
                    for (int i = 0; i < children.Count; i++)
                    {
                        ((SizedNode)children[i]).Weight = a.Weights[i];
                    }
                }
                break;
            case AdjustBorderSplitAction a:
                if (GetNodeById(a.NodeId) is BorderNode borderNode)
                {
                    borderNode.SetSize(a.Size);
                }
                break;
            case MaximizeToggleAction a:
            {
                string layoutId = a.LayoutId ?? MainLayoutId;
                var layout = _layouts[layoutId];
                if (GetNodeById(a.TabsetNodeId) is TabSetNode maxTabSet)
                {
                    if (ReferenceEquals(maxTabSet, layout.MaximizedTabSet))
                    {
                        layout.MaximizedTabSet = null;
                    }
                    else
                    {
                        layout.MaximizedTabSet = maxTabSet;
                        layout.ActiveTabSet = maxTabSet;
                    }
                }
                break;
            }
            case RenameTabAction a:
                if (GetNodeById(a.TabNodeId) is TabNode renameTab)
                {
                    renameTab.SetName(a.Text);
                }
                break;
            case UpdateModelAttributesAction a:
                ApplyGlobalAttributes(a.Attributes);
                break;
            case UpdateNodeAttributesAction:
                // TODO Phase 1: apply per-node attribute overrides once nullable backings are wired.
                break;
            case PopoutTabsetAction a:
            {
                if (GetNodeById(a.NodeId) is TabSetNode popoutTabSet)
                {
                    // Pane-ness travels with the tabs (TabNode.IsDocument), so a popped-out pane group keeps its
                    // chrome with no capture here.
                    var layoutId = NextUniqueId();
                    var rect = a.Position is Point pos ? new Rect(pos.X, pos.Y, 600, 400) : new Rect(80, 80, 600, 400);
                    var layout = new Layout(layoutId, a.Type, rect);
                    var popoutRow = new RowNode(this);
                    layout.SetRootRow(popoutRow);
                    _layouts[layoutId] = layout;
                    popoutRow.Drop(popoutTabSet, DockLocation.Center, 0);
                }
                break;
            }
            case PopoutTabAction a:
            {
                if (GetNodeById(a.NodeId) is TabNode popoutTab)
                {
                    // Pane-ness travels with the tab (TabNode.IsDocument): a pane pops out as a pane group (bottom
                    // tabs), a document as a document group.
                    var layoutId = NextUniqueId();
                    var rect = a.Position is Point pos ? new Rect(pos.X, pos.Y, 480, 320) : new Rect(80, 80, 480, 320);
                    var layout = new Layout(layoutId, a.Type, rect);
                    var popoutRow = new RowNode(this);
                    layout.SetRootRow(popoutRow);
                    _layouts[layoutId] = layout;
                    var tabSet = new TabSetNode(this);
                    popoutRow.AddChild(tabSet);
                    tabSet.Drop(popoutTab, DockLocation.Center, 0, select: true);
                }
                break;
            }
            case ClosePopoutAction a:
                if (_layouts.TryGetValue(a.LayoutId, out var closing) && closing.RootRow is RowNode closingRoot)
                {
                    // Dock the popout's content back into the main layout's root, then drop the sub-layout.
                    var mainRoot = GetRootRow(MainLayoutId);
                    foreach (var child in closingRoot.Children.ToList())
                    {
                        closingRoot.RemoveChild(child);
                        mainRoot.AddChild(child);
                    }
                    mainRoot.NormalizeWeights();
                    _layouts.Remove(a.LayoutId);
                    Tidy();
                }
                break;
            case MovePopoutToFrontAction:
            case CreateSubLayoutAction:
                // Float reorder / explicit empty sub-layout creation are not needed by the popout flow yet.
                break;
        }

        // Feature seam: the subclass reducer (its own actions + post-effects) runs inside the same transaction,
        // before the id-map rebuild and the single change notification below.
        OnActionApplied(action);

        UpdateIdMap();

        foreach (var listener in _changeListeners.ToArray())
        {
            listener(action);
        }

        return returnVal;
    }

    private void ApplyGlobalAttributes(JsonGlobal global)
    {
        if (global.RootOrientationVertical is bool rootVertical)
        {
            IsRootOrientationVertical = rootVertical;
        }
        if (global.SplitterSize is double splitterSize)
        {
            SplitterSize = splitterSize;
        }
        if (global.EnableEdgeDock is bool enableEdgeDock)
        {
            EnableEdgeDock = enableEdgeDock;
        }
        if (global.BorderSize is double borderSize)
        {
            BorderSize = borderSize;
        }
    }
}
