using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;
using Aprillz.MewUI.MewDock.Model.Json;

namespace Aprillz.MewUI.MewDock;

/// <summary>
/// A handle over one tab group (a tabset) managed by a <see cref="DockingManager"/>. Like <see cref="DockPane"/> it
/// carries identity, the common group verbs, and the group's changing state as bindable properties; the layout
/// itself stays inside the manager. Auto-hide borders are not groups - an auto-hidden pane reports a null
/// <see cref="DockPane.Group"/>. Removing the group or calling <see cref="DockingManager.LoadLayout"/> detaches the
/// handle: it keeps its last values and its verbs do nothing.
/// </summary>
public sealed class DockGroup : MewObject
{
    private static readonly MewPropertyKey<bool> IsMaximizedPropertyKey =
        MewProperty<bool>.RegisterReadOnly<DockGroup>(nameof(IsMaximized), false);

    /// <summary>Whether this group is currently maximized.</summary>
    public static readonly MewProperty<bool> IsMaximizedProperty = IsMaximizedPropertyKey.Property;

    private static readonly MewPropertyKey<DockEdge?> EdgePropertyKey =
        MewProperty<DockEdge?>.RegisterReadOnly<DockGroup>(nameof(Edge), null);

    /// <summary>The edge this group is docked to, or null for a document or floating group.</summary>
    public static readonly MewProperty<DockEdge?> EdgeProperty = EdgePropertyKey.Property;

    private static readonly MewPropertyKey<DockPane?> ActivePanePropertyKey =
        MewProperty<DockPane?>.RegisterReadOnly<DockGroup>(nameof(ActivePane), null);

    /// <summary>The selected pane, or null when the group is empty.</summary>
    public static readonly MewProperty<DockPane?> ActivePaneProperty = ActivePanePropertyKey.Property;

    private readonly DockingManager _manager;
    private bool _detached;

    internal DockGroup(DockingManager manager, TabSetNode node)
    {
        _manager = manager;
        Node = node;
    }

    internal TabSetNode Node { get; }

    /// <summary>Gets whether this is a document group (top tabs, maximize) rather than a tool group (caption, edge dock).</summary>
    public bool IsDocument => Node.IsDocument;

    /// <summary>Gets whether this group is currently maximized.</summary>
    public bool IsMaximized => GetValue(IsMaximizedProperty);

    /// <summary>Gets the edge this group is docked to, or null for a document or floating group.</summary>
    public DockEdge? Edge => GetValue(EdgeProperty);

    /// <summary>Adds a pane holding <paramref name="content"/> to this group, of the group's kind (document or tool),
    /// and selects it.</summary>
    /// <exception cref="InvalidOperationException">The handle is detached.</exception>
    public DockPane AddPane(string title, UIElement content, string? component = null)
    {
        if (_detached)
        {
            throw new InvalidOperationException("The group is no longer part of the layout.");
        }
        var json = new JsonTabNode { Name = title, Component = component, IsDocument = IsDocument ? null : false };
        return _manager.AddExplicitTab(json, Node.GetId(), DockLocation.Center, select: true, content);
    }

    /// <summary>Gets the panes in this group at the time of the call.</summary>
    public IReadOnlyList<DockPane> Panes
    {
        get
        {
            var result = new List<DockPane>();
            if (_detached)
            {
                return result;
            }
            foreach (var child in Node.Children)
            {
                if (child is TabNode tab)
                {
                    result.Add(_manager.GetOrCreatePane(tab));
                }
            }
            return result;
        }
    }

    /// <summary>Gets the selected pane, or null when the group is empty.</summary>
    public DockPane? ActivePane => GetValue(ActivePaneProperty);

    /// <summary>Makes this the focused group.</summary>
    public void Activate() => Perform(DockAction.SetActiveTabset(Node.GetId(), Node.LayoutId));

    /// <summary>Closes the whole group and every pane in it.</summary>
    public void Close() => Perform(DockAction.DeleteTabset(Node.GetId()));

    /// <summary>Pops the whole group out into its own window.</summary>
    public void Float() => Perform(DockAction.PopoutTabset(Node.GetId()));

    /// <summary>Toggles this group between maximized and normal; no-op for a tool group, since only document groups maximize.</summary>
    public void ToggleMaximize()
    {
        if (IsDocument)
        {
            Perform(DockAction.MaximizeToggle(Node.GetId()));
        }
    }

    /// <summary>Unpins a docked tool group back to its auto-hide edge; no-op for a document group.</summary>
    public void Unpin()
    {
        if (!IsDocument)
        {
            Perform(DockAction.UnpinTool(Node.GetId()));
        }
    }

    /// <summary>Copies the model's current state into the bindable properties.</summary>
    internal void SyncFromModel()
    {
        if (_detached)
        {
            return;
        }
        SetValue(IsMaximizedPropertyKey, Node.IsMaximized);
        SetValue(EdgePropertyKey, DockingManager.EdgeOfGroup(Node));
        SetValue(ActivePanePropertyKey, Node.GetSelectedNode() is TabNode tab ? _manager.GetOrCreatePane(tab) : null);
    }

    /// <summary>Stops this handle from acting on the layout; it keeps its last values.</summary>
    internal void Detach() => _detached = true;

    private void Perform(DockAction action)
    {
        if (!_detached)
        {
            _manager.PerformAction(action);
        }
    }
}
