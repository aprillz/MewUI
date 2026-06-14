using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;

namespace Aprillz.MewUI.MewDock;

/// <summary>
/// A lightweight handle over one pane (a tab) managed by a <see cref="DockingManager"/>. It carries identity and
/// the common verbs; it is not a live wrapper tree - the layout itself stays inside the manager.
/// </summary>
public sealed class DockPane
{
    private readonly DockingManager _manager;

    internal DockPane(DockingManager manager, TabNode node)
    {
        _manager = manager;
        Node = node;
    }

    internal TabNode Node { get; }

    internal string Id => Node.GetId();

    /// <summary>The pane title shown by the default header. Setting it renames the tab through the reducer.</summary>
    public string? Title
    {
        get => Node.Name;
        set => _manager.PerformAction(DockAction.RenameTab(Id, value ?? string.Empty));
    }

    /// <summary>The serialized component key (used by <see cref="DockingManager.ContentFactory"/> to restore content).</summary>
    public string? Component => Node.Component;

    /// <summary>Document pane (top tabs, maximize) vs plain pane (edge dock / auto-hide).</summary>
    public bool IsDocument => Node.IsDocument;

    /// <summary>The group (tabset) this pane lives in, or null when it is auto-hidden in a border.</summary>
    public DockGroup? Group => Node.Parent is TabSetNode tabSet ? _manager.GetOrCreateGroup(tabSet) : null;

    /// <summary>The edge this pane is docked or auto-hidden on, or null for a document / floating pane.</summary>
    public DockEdge? Edge => DockingManager.EdgeOf(Node);

    public bool IsActive => ReferenceEquals(_manager.ActivePane, this);

    /// <summary>The content given to AddDocumentPane/AddPane, if any (factory-restored panes return null).</summary>
    public UIElement? Content => _manager.GetExplicitContent(Id);

    public void Activate() => _manager.PerformAction(DockAction.SelectTab(Id));

    public void Close() => _manager.PerformAction(DockAction.DeleteTab(Id));

    /// <summary>Pops the pane out into its own window.</summary>
    public void Float() => _manager.PerformAction(DockAction.PopoutTab(Id));

    /// <summary>Pops the pane's whole group (its tabset) out into its own window.</summary>
    public void FloatGroup()
    {
        if (Node.Parent is TabSetNode tabSet)
        {
            _manager.PerformAction(DockAction.PopoutTabset(tabSet.GetId()));
        }
    }

    /// <summary>Splits this pane off its current group toward <paramref name="edge"/> of that group. No-op when the
    /// pane is alone in its group (nothing to split from).</summary>
    public void SplitOff(DockEdge edge)
    {
        if (Node.Parent is TabSetNode tabSet && tabSet.Children.Count > 1)
        {
            _manager.PerformAction(DockAction.MoveNode(Id, tabSet.GetId(), DockingManager.ToDockLocation(edge), -1));
        }
    }

    /// <summary>Moves this pane into <paramref name="group"/> as a tab.</summary>
    public void MoveInto(DockGroup group) =>
        _manager.PerformAction(DockAction.MoveNode(Id, group.Node.GetId(), DockLocation.Center, -1));

    /// <summary>Docks this pane against <paramref name="edge"/> of <paramref name="group"/> (a split).</summary>
    public void DockInto(DockGroup group, DockEdge edge) =>
        _manager.PerformAction(DockAction.MoveNode(Id, group.Node.GetId(), DockingManager.ToDockLocation(edge), -1));

    /// <summary>Pins an auto-hide pane into a docked group on its edge. No-op when not auto-hidden.</summary>
    public void Pin()
    {
        if (Node.Parent is BorderNode)
        {
            _manager.PerformAction(DockAction.PinTool(Id));
        }
    }

    /// <summary>Unpins the pane's docked group back to the auto-hide edge. No-op when not docked.</summary>
    public void Unpin()
    {
        if (Node.Parent is TabSetNode tabSet)
        {
            _manager.PerformAction(DockAction.UnpinTool(tabSet.GetId()));
        }
    }
}
