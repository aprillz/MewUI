using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;
using Aprillz.MewUI.MewDock.Model.Json;

namespace Aprillz.MewUI.MewDock;

/// <summary>
/// A lightweight handle over one tab group (a tabset) managed by a <see cref="DockingManager"/>. Like
/// <see cref="DockPane"/> it carries identity plus the common group verbs; the layout itself stays inside the
/// manager. Auto-hide borders are not groups - an auto-hidden pane reports a null <see cref="DockPane.Group"/>.
/// </summary>
public sealed class DockGroup
{
    private readonly DockingManager _manager;

    internal DockGroup(DockingManager manager, TabSetNode node)
    {
        _manager = manager;
        Node = node;
    }

    internal TabSetNode Node { get; }

    /// <summary>Document group (top tabs, maximize) vs tool group (caption + edge dock).</summary>
    public bool IsDocument => Node.IsDocument;

    /// <summary>True when this group is currently maximized.</summary>
    public bool IsMaximized => Node.IsMaximized;

    /// <summary>The edge this group is docked to, or null for a document / floating group.</summary>
    public DockEdge? Edge => DockingManager.EdgeOfGroup(Node);

    /// <summary>Add a tab to this group. The new pane's kind (document / tool) follows the group.</summary>
    public DockPane AddPane(string title, UIElement content, string? component = null)
    {
        var json = new JsonTabNode { Name = title, Component = component, IsDocument = IsDocument ? null : false };
        return _manager.AddExplicitTab(json, Node.GetId(), DockLocation.Center, select: true, content);
    }

    /// <summary>The tabs in this group.</summary>
    public IReadOnlyList<DockPane> Panes
    {
        get
        {
            var result = new List<DockPane>();
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

    /// <summary>The selected pane, or null when the group is empty.</summary>
    public DockPane? ActivePane => Node.GetSelectedNode() is TabNode tab ? _manager.GetOrCreatePane(tab) : null;

    /// <summary>Make this the focused group.</summary>
    public void Activate() => _manager.PerformAction(DockAction.SetActiveTabset(Node.GetId(), Node.LayoutId));

    /// <summary>Close the whole group and every tab in it.</summary>
    public void Close() => _manager.PerformAction(DockAction.DeleteTabset(Node.GetId()));

    /// <summary>Pop the whole group out into its own window.</summary>
    public void Float() => _manager.PerformAction(DockAction.PopoutTabset(Node.GetId()));

    /// <summary>Toggle maximized/normal. No-op for a tool group (only document groups maximize).</summary>
    public void ToggleMaximize()
    {
        if (IsDocument)
        {
            _manager.PerformAction(DockAction.MaximizeToggle(Node.GetId()));
        }
    }

    /// <summary>Unpin a docked tool group back to its auto-hide edge. No-op for a document group.</summary>
    public void Unpin()
    {
        if (!IsDocument)
        {
            _manager.PerformAction(DockAction.UnpinTool(Node.GetId()));
        }
    }
}
