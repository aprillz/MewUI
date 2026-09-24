using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Controls;
using Aprillz.MewUI.MewDock.Extended;
using Aprillz.MewUI.MewDock.Model;
using Aprillz.MewUI.MewDock.Model.Json;

using DockModel = Aprillz.MewUI.MewDock.Model.Model;

namespace Aprillz.MewUI.MewDock;

/// <summary>
/// A document-area edge a pane docks to.
/// </summary>
public enum DockEdge
{
    Left,
    Top,
    Right,
    Bottom,
}

/// <summary>
/// The docking facade: one control the host puts in a window. Wraps the model + layout view,
/// exposes panes as lightweight <see cref="DockPane"/> handles, and
/// keeps <see cref="PerformAction"/> as the escape hatch to the full reducer.
/// </summary>
public sealed class DockingManager : Panel
{
    private const string EmptyLayoutJson = """{ "layout": { "type": "row", "children": [] } }""";

    private readonly Dictionary<string, DockPane> _panes = new();
    private readonly Dictionary<string, DockGroup> _groups = new();
    private readonly Dictionary<string, UIElement> _explicitContent = new();
    private readonly Dictionary<string, PaneHost> _hosts = new();
    private DockModel? _model;
    private FlexLayoutView? _view;
    private DockPane? _activePane;
    private UIElement? _centerContent;

    /// <summary>Builds a pane's content, once per pane: the dock keeps it for as long as the pane is in the layout and
    /// gives it back (out of the window) when the pane is closed. Panes added with explicit content bypass it.</summary>
    public Func<DockPane, UIElement?>? ContentFactory { get; set; }

    /// <summary>Builds custom tab-header content, once per pane; null falls back to the default header (title + close).
    /// The header is kept while the pane moves between groups, so it should bind to the pane's properties.</summary>
    public Func<DockPane, UIElement?>? HeaderFactory { get; set; }

    internal DockModel? Model => _model;

    public DockPane? ActivePane => _activePane;

    public event EventHandler<DockPane?>? ActivePaneChanged;

    /// <summary>Raised each time a tab's right-click menu opens, after the default items are added. Handlers add app
    /// commands to <see cref="DockTabMenuEventArgs.Menu"/>.</summary>
    public event EventHandler<DockTabMenuEventArgs>? TabMenuOpening;

    /// <summary>Raised each time a group's (tab strip) right-click menu opens, after the default items.</summary>
    public event EventHandler<DockGroupMenuEventArgs>? GroupMenuOpening;

    /// <summary>Raised after any change to the layout (a user gesture or a handle verb), once the change is complete.</summary>
    public event EventHandler? Changed;

    /// <summary>Raised before the user closes a pane from the dock's own controls (a tab's close button or middle click, a
    /// caption's close button, the Close menu items). Setting <see cref="DockPaneClosingEventArgs.Cancel"/> keeps the pane
    /// where it is. <see cref="DockPane.Close"/> called from code closes without asking.</summary>
    public event EventHandler<DockPaneClosingEventArgs>? PaneClosing;

    /// <summary>Optional custom centre element replacing the document host (panes still dock around it).</summary>
    public UIElement? CenterContent
    {
        get => _centerContent;
        set
        {
            _centerContent = value;
            if (_view is not null)
            {
                _view.Content = value;
            }
        }
    }

    public IReadOnlyList<DockPane> DocumentPanes => CollectPanes(documents: true);

    public IReadOnlyList<DockPane> Panes => CollectPanes(documents: false);

    /// <summary>Every tab group (tabset) in the layout, as handles. Auto-hide borders are not groups.</summary>
    public IReadOnlyList<DockGroup> Groups
    {
        get
        {
            var result = new List<DockGroup>();
            _model?.VisitNodes((node, level) =>
            {
                if (node is TabSetNode tabSet)
                {
                    result.Add(GetOrCreateGroup(tabSet));
                }
            });
            return result;
        }
    }

    /// <summary>The focused group, or null when nothing is focused.</summary>
    public DockGroup? ActiveGroup => _model?.FocusedTabSet is TabSetNode tabSet ? GetOrCreateGroup(tabSet) : null;

    public void LoadLayout(string json)
    {
        if (_model is DockModel previous)
        {
            previous.ChangesCompleted -= OnModelChanged;
        }

        var model = ExtendedDockModel.FromJson(json);
        DetachAllHandles();
        _view?.Release();
        _explicitContent.Clear();
        _model = model;
        // Content of a tab the new layout still has (by id) stays with it; the rest is let go.
        PruneHosts();
        _view = ExtendedDock.CreateView(model, ResolveHost, ResolveHeader, ConfigureTabMenuForNode, ConfigureGroupMenuForNode, RequestClose);

        _view.Content = _centerContent;
        _model.ChangesCompleted += OnModelChanged;
        Clear();
        Add(_view);
        SyncActivePane();
        InvalidateMeasure();
    }

    public string SaveLayout() => _model?.ToJsonString() ?? EmptyLayoutJson;

    public DockPane AddDocumentPane(string title, UIElement content, string? component = null)
    {
        var model = RequireModel();
        var json = new JsonTabNode { Name = title, Component = component };
        TabNode node;
        if (FindDocumentTabSetId() is string tabSetId)
        {
            node = AddTabWithContent(model, json, tabSetId, DockLocation.Center, select: true, content);
        }
        else
        {
            // No document tabset yet (empty / custom-center layout): edge-dock a fresh one onto the root row.
            node = AddTabWithContent(model, json, model.GetRootRow().GetId(), DockLocation.Right, select: true, content);
        }
        return GetOrCreatePane(node);
    }

    public DockPane AddToolPane(string title, UIElement content, DockEdge edge = DockEdge.Left, string? component = null)
    {
        var model = RequireModel();
        var border = GetOrCreateBorder(model, ToDockLocation(edge));
        var json = new JsonTabNode { Name = title, IsDocument = false, Component = component };
        // Adding and pinning are one change to the host: it hears about them once, with the pane already docked.
        using (model.DeferNotifications())
        {
            var node = AddTabWithContent(model, json, border.GetId(), DockLocation.Center, select: false, content);
            // A new pane starts pinned as a docked group; Unpin() sends it back to auto-hide.
            model.DoAction(DockAction.PinTool(node.GetId()));
            return GetOrCreatePane(node);
        }
    }

    // Add a tab built from json to an existing target node with explicit (non-factory) content. Used by DockGroup.Add.
    internal DockPane AddExplicitTab(JsonTabNode json, string toNodeId, DockLocation location, bool select, UIElement content)
        => GetOrCreatePane(AddTabWithContent(RequireModel(), json, toNodeId, location, select, content));

    private TabNode AddTabWithContent(DockModel model, JsonTabNode json, string toNodeId, DockLocation location, bool select, UIElement content)
    {
        // The add rebuilds the view before it returns, so the content must be findable under the tab's id first.
        json.Id ??= model.NextUniqueId();
        _explicitContent[json.Id] = content;
        return (TabNode)model.DoAction(DockAction.AddTab(json, toNodeId, location, -1, select))!;
    }

    // The single dispatch path every handle verb funnels through; internal because actions are id-based and not
    // part of the public surface (hosts use DockingManager / DockPane / DockGroup verbs).
    internal object? PerformAction(DockAction action) => _model?.DoAction(action);

    protected override Size MeasureContent(Size availableSize)
    {
        _view?.Measure(availableSize);
        // The dock fills whatever it is given; unbounded, it asks for nothing.
        return new Size(
            double.IsPositiveInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsPositiveInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override void ArrangeContent(Rect bounds) => _view?.Arrange(bounds);

    private void OnModelChanged()
    {
        PrunePanes();
        PruneHosts();
        var previousActive = _activePane;
        SyncActivePane();
        // A pane that became active, or a tool that was just revealed, takes the keyboard focus into its content.
        if (_activePane is DockPane active && !ReferenceEquals(active, previousActive))
        {
            FocusContent(active.Node);
        }
        FocusNewlyRevealed();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private readonly HashSet<TabNode> _revealed = new();

    private void FocusNewlyRevealed()
    {
        var revealed = new List<TabNode>();
        if (_model is DockModel model)
        {
            foreach (var border in model.BorderSet.Borders)
            {
                if (border.GetSelectedNode() is TabNode tab)
                {
                    revealed.Add(tab);
                }
            }
        }
        foreach (var tab in revealed)
        {
            if (!_revealed.Contains(tab))
            {
                FocusContent(tab);
            }
        }
        _revealed.Clear();
        _revealed.UnionWith(revealed);
    }

    private void SyncActivePane()
    {
        var active = _model?.FocusedTabSet?.GetSelectedNode() is TabNode tab ? GetOrCreatePane(tab) : null;
        var previous = _activePane;
        _activePane = active;
        // Sync before raising ActivePaneChanged so a handler reads the new state through the handles.
        SyncHandles();
        if (!ReferenceEquals(active, previous))
        {
            ActivePaneChanged?.Invoke(this, active);
        }
    }

    private void SyncHandles()
    {
        // Handles created during the pass sync themselves on creation, so iterate a snapshot.
        foreach (var pane in _panes.Values.ToList())
        {
            pane.SyncFromModel();
        }
        foreach (var group in _groups.Values.ToList())
        {
            group.SyncFromModel();
        }
    }

    private void PrunePanes()
    {
        foreach (var (id, pane) in _panes.ToList())
        {
            if (!ReferenceEquals(_model?.GetNodeById(id), pane.Node))
            {
                pane.Detach();
                _panes.Remove(id);
                _explicitContent.Remove(id);
            }
        }
        foreach (var (id, group) in _groups.ToList())
        {
            if (!ReferenceEquals(_model?.GetNodeById(id), group.Node))
            {
                group.Detach();
                _groups.Remove(id);
            }
        }
    }

    private void DetachAllHandles()
    {
        foreach (var pane in _panes.Values)
        {
            pane.Detach();
        }
        foreach (var group in _groups.Values)
        {
            group.Detach();
        }
        _panes.Clear();
        _groups.Clear();
        _activePane = null;
    }

    internal bool IsActiveNode(TabNode node) => ReferenceEquals(_model?.FocusedTabSet?.GetSelectedNode(), node);

    internal DockPane GetOrCreatePane(TabNode node)
    {
        string id = node.GetId();
        if (_panes.TryGetValue(id, out var pane))
        {
            return pane;
        }
        pane = new DockPane(this, node);
        // Cache before syncing: the sync resolves the group, whose sync resolves its selected pane.
        _panes[id] = pane;
        pane.SyncFromModel();
        return pane;
    }

    internal DockGroup GetOrCreateGroup(TabSetNode node)
    {
        string id = node.GetId();
        if (_groups.TryGetValue(id, out var group))
        {
            return group;
        }
        group = new DockGroup(this, node);
        _groups[id] = group;
        group.SyncFromModel();
        return group;
    }

    internal UIElement? GetExplicitContent(string id) => _explicitContent.TryGetValue(id, out var content) ? content : null;

    /// <summary>A tab's content is decided once: the content it was added with, or what ContentFactory makes for it.</summary>
    private PaneHost? ResolveHost(TabNode tab)
    {
        string id = tab.GetId();
        if (_hosts.TryGetValue(id, out var host))
        {
            host.Tab = tab;
            return host;
        }
        var pane = GetOrCreatePane(tab);
        var content = _explicitContent.TryGetValue(id, out var explicitContent)
            ? explicitContent
            : ContentFactory?.Invoke(pane);
        host = new PaneHost(tab, content);
        host.Pressed += OnHostPressed;
        host.FocusEntered += OnHostFocusEntered;
        _hosts[id] = host;
        return host;
    }

    /// <summary>A click in a pane's content makes its group the active one, as a click on the group's chrome does.</summary>
    private void OnHostPressed(PaneHost host)
    {
        if (_model is DockModel model && host.Tab.Parent is TabSetNode tabSet && !ReferenceEquals(model.FocusedTabSet, tabSet))
        {
            model.DoAction(DockAction.SetActiveTabset(tabSet.GetId(), tabSet.LayoutId));
        }
    }

    /// <summary>The dock's own close controls come here: the host may keep the pane, otherwise it is closed.</summary>
    internal void RequestClose(TabNode tab)
    {
        if (_model is not DockModel model || !tab.IsEnableClose)
        {
            return;
        }
        var args = new DockPaneClosingEventArgs(GetOrCreatePane(tab));
        PaneClosing?.Invoke(this, args);
        if (!args.Cancel && ReferenceEquals(model.GetNodeById(tab.GetId()), tab))
        {
            model.DoAction(DockAction.DeleteTab(tab.GetId()));
        }
    }

    /// <summary>Keyboard focus arriving in a pane's content (a click, Tab, or code) makes its group the active one.</summary>
    private void OnHostFocusEntered(PaneHost host) => OnHostPressed(host);

    /// <summary>Gives the keyboard focus to a tab's content: where it last was inside, or its first focusable element.</summary>
    internal void FocusContent(TabNode tab)
    {
        if (_hosts.TryGetValue(tab.GetId(), out var host))
        {
            host.RestoreFocus();
        }
    }

    /// <summary>A tab that left the layout gives its content back, so the host application can show it again elsewhere.</summary>
    private void PruneHosts()
    {
        foreach (var (id, host) in _hosts.ToList())
        {
            if (_model?.GetNodeById(id) is not TabNode)
            {
                _hosts.Remove(id);
                host.Pressed -= OnHostPressed;
                host.FocusEntered -= OnHostFocusEntered;
                host.ReleaseContent();
            }
        }
    }

    private UIElement? ResolveHeader(TabNode tab) => HeaderFactory?.Invoke(GetOrCreatePane(tab));

    // Default menus are built in the view; these raise the public events (per open) with the matching handle.
    private void ConfigureTabMenuForNode(TabNode tab, ContextMenu menu, CommandScope commands)
        => TabMenuOpening?.Invoke(this, new DockTabMenuEventArgs(GetOrCreatePane(tab), menu, commands));

    private void ConfigureGroupMenuForNode(TabSetNode tabSet, ContextMenu menu, CommandScope commands)
        => GroupMenuOpening?.Invoke(this, new DockGroupMenuEventArgs(GetOrCreateGroup(tabSet), menu, commands));

    private IReadOnlyList<DockPane> CollectPanes(bool documents)
    {
        var result = new List<DockPane>();
        _model?.VisitNodes((node, level) =>
        {
            if (node is TabNode tab && tab.IsDocument == documents)
            {
                result.Add(GetOrCreatePane(tab));
            }
        });
        return result;
    }

    private DockModel RequireModel()
    {
        if (_model is null)
        {
            LoadLayout(EmptyLayoutJson);
        }
        return _model!;
    }

    private string? FindDocumentTabSetId()
    {
        if (_model is null)
        {
            return null;
        }
        if (_model.FocusedTabSet is { IsDocument: true } focused)
        {
            return focused.GetId();
        }
        string? id = null;
        _model.GetRootRow().ForEachNode((node, level) =>
        {
            if (id is null && node is TabSetNode { IsDocument: true } tabSet)
            {
                id = tabSet.GetId();
            }
        }, 0);
        return id;
    }

    internal static DockLocation ToDockLocation(DockEdge edge) => edge switch
    {
        DockEdge.Left => DockLocation.Left,
        DockEdge.Top => DockLocation.Top,
        DockEdge.Right => DockLocation.Right,
        _ => DockLocation.Bottom,
    };

    internal static DockEdge? FromDockLocation(DockLocation location) => location switch
    {
        DockLocation.Left => DockEdge.Left,
        DockLocation.Top => DockEdge.Top,
        DockLocation.Right => DockEdge.Right,
        DockLocation.Bottom => DockEdge.Bottom,
        _ => null,
    };

    // The edge a group is docked to (its Dock sub-layout edge), or null for a document / floating group.
    internal static DockEdge? EdgeOfGroup(TabSetNode tabSet) =>
        tabSet.GetLayout() is DockLayout dock ? FromDockLocation(dock.Edge) : null;

    // The edge a tab sits on: its auto-hide border's edge, or its docked group's edge; null otherwise.
    internal static DockEdge? EdgeOf(Node node) => node.Parent switch
    {
        BorderNode border => FromDockLocation(border.Location),
        TabSetNode tabSet => EdgeOfGroup(tabSet),
        _ => null,
    };

    private static BorderNode GetOrCreateBorder(DockModel model, DockLocation location)
    {
        if (model.BorderSet.BorderMap.TryGetValue(location, out var border))
        {
            return border;
        }
        border = new BorderNode(model, location);
        model.BorderSet.Add(border);
        return border;
    }
}

/// <summary>
/// Args for <see cref="DockingManager.PaneClosing"/>: the pane the user is closing, and whether to keep it.
/// </summary>
public sealed class DockPaneClosingEventArgs : EventArgs
{
    internal DockPaneClosingEventArgs(DockPane pane)
    {
        Pane = pane;
    }

    /// <summary>The pane being closed.</summary>
    public DockPane Pane { get; }

    /// <summary>Set to keep the pane open.</summary>
    public bool Cancel { get; set; }
}

/// <summary>
/// Args for <see cref="DockingManager.TabMenuOpening"/>: the pane and its right-click menu (already populated with
/// the default items) for handlers to augment. Raised each time the menu opens.
/// </summary>
public sealed class DockTabMenuEventArgs : EventArgs
{
    internal DockTabMenuEventArgs(DockPane pane, ContextMenu menu, CommandScope commands)
    {
        Pane = pane;
        Menu = menu;
        Commands = commands;
    }

    public DockPane Pane { get; }

    public ContextMenu Menu { get; }

    /// <summary>Gets the command scope targeted by this menu.</summary>
    public CommandScope Commands { get; }
}

/// <summary>
/// Args for <see cref="DockingManager.GroupMenuOpening"/>: the group and its right-click menu (already populated
/// with the default items) for handlers to augment. Raised each time the menu opens.
/// </summary>
public sealed class DockGroupMenuEventArgs : EventArgs
{
    internal DockGroupMenuEventArgs(DockGroup group, ContextMenu menu, CommandScope commands)
    {
        Group = group;
        Menu = menu;
        Commands = commands;
    }

    public DockGroup Group { get; }

    public ContextMenu Menu { get; }

    /// <summary>Gets the command scope targeted by this menu.</summary>
    public CommandScope Commands { get; }
}
