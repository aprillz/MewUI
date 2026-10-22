using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock.Model;

namespace Aprillz.MewUI.MewDock;

/// <summary>
/// A handle over one pane (a tab) managed by a <see cref="DockingManager"/>. It carries identity, the common verbs,
/// and the pane's changing state as bindable properties; the layout itself stays inside the manager.
/// The handle stays the same instance while the pane exists (moves, pin/unpin and popout keep it). Closing the pane
/// or calling <see cref="DockingManager.LoadLayout"/> detaches it: it keeps its last values and its setters and
/// verbs do nothing.
/// </summary>
public sealed class DockPane : MewObject
{
    /// <summary>The pane title shown by the default header; binds two-way by default, and a write renames the tab.</summary>
    public static readonly MewProperty<string?> TitleProperty =
        MewProperty<string?>.Register<DockPane>(nameof(Title), null, MewPropertyOptions.BindsTwoWayByDefault,
            static (pane, _, title) => pane.OnTitleChanged(title));

    private static readonly MewPropertyKey<bool> IsActivePropertyKey =
        MewProperty<bool>.RegisterReadOnly<DockPane>(nameof(IsActive), false);

    /// <summary>Whether this is the active pane (the selected pane of the focused group).</summary>
    public static readonly MewProperty<bool> IsActiveProperty = IsActivePropertyKey.Property;

    private static readonly MewPropertyKey<DockGroup?> GroupPropertyKey =
        MewProperty<DockGroup?>.RegisterReadOnly<DockPane>(nameof(Group), null);

    /// <summary>The group this pane lives in, or null when it is auto-hidden in a border.</summary>
    public static readonly MewProperty<DockGroup?> GroupProperty = GroupPropertyKey.Property;

    private static readonly MewPropertyKey<DockEdge?> EdgePropertyKey =
        MewProperty<DockEdge?>.RegisterReadOnly<DockPane>(nameof(Edge), null);

    /// <summary>The edge this pane is docked or auto-hidden on, or null for a document or floating pane.</summary>
    public static readonly MewProperty<DockEdge?> EdgeProperty = EdgePropertyKey.Property;

    private readonly DockingManager _manager;
    private bool _syncing;
    private bool _detached;

    internal DockPane(DockingManager manager, TabNode node)
    {
        _manager = manager;
        Node = node;
    }

    internal TabNode Node { get; }

    internal string Id => Node.GetId();

    /// <summary>Gets or sets the pane title shown by the default header; a new non-null title renames the tab, and null
    /// is ignored.</summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>Gets the serialized component key <see cref="DockingManager.ContentFactory"/> uses to restore content, or null.</summary>
    public string? Component => Node.Component;

    /// <summary>Gets whether this is a document pane (top tabs, maximize) rather than a tool pane (edge dock, auto-hide).</summary>
    public bool IsDocument => Node.IsDocument;

    /// <summary>Gets the group (tabset) this pane lives in, or null when it is auto-hidden in a border.</summary>
    public DockGroup? Group => GetValue(GroupProperty);

    /// <summary>Gets the edge this pane is docked or auto-hidden on, or null for a document or floating pane.</summary>
    public DockEdge? Edge => GetValue(EdgeProperty);

    /// <summary>Gets whether this is the active pane (the selected pane of the focused group).</summary>
    public bool IsActive => GetValue(IsActiveProperty);

    /// <summary>Gets the content given to <see cref="DockingManager.AddDocumentPane"/>, <see cref="DockingManager.AddToolPane"/>
    /// or <see cref="DockGroup.AddPane"/>, or null for a pane whose content <see cref="DockingManager.ContentFactory"/> built.</summary>
    public UIElement? Content => _detached ? null : _manager.GetExplicitContent(Id);

    /// <summary>Makes this the active pane and gives its content the keyboard focus; a revealed auto-hide tool stays
    /// revealed.</summary>
    public void Activate()
    {
        if (_detached)
        {
            return;
        }
        // Selecting a border tab toggles it; one already revealed is only focused.
        if (!(Node.Parent is BorderNode border && ReferenceEquals(border.GetSelectedNode(), Node)))
        {
            Perform(DockAction.SelectTab(Id));
        }
        _manager.FocusContent(Node);
    }

    /// <summary>Closes the pane without raising <see cref="DockingManager.PaneClosing"/>.</summary>
    public void Close() => Perform(DockAction.DeleteTab(Id));

    /// <summary>Pops the pane out into its own window.</summary>
    public void Float() => Perform(DockAction.PopoutTab(Id));

    /// <summary>Pops the pane's whole group (its tabset) out into its own window.</summary>
    public void FloatGroup()
    {
        if (Node.Parent is TabSetNode tabSet)
        {
            Perform(DockAction.PopoutTabset(tabSet.GetId()));
        }
    }

    /// <summary>Splits this pane off its current group toward <paramref name="edge"/> of that group; no-op when the
    /// pane is alone in its group.</summary>
    public void SplitOff(DockEdge edge)
    {
        if (Node.Parent is TabSetNode tabSet && tabSet.Children.Count > 1)
        {
            Perform(DockAction.MoveNode(Id, tabSet.GetId(), DockingManager.ToDockLocation(edge), -1));
        }
    }

    /// <summary>Moves this pane into <paramref name="group"/> as a tab.</summary>
    public void MoveInto(DockGroup group) =>
        Perform(DockAction.MoveNode(Id, group.Node.GetId(), DockLocation.Center, -1));

    /// <summary>Docks this pane against <paramref name="edge"/> of <paramref name="group"/> (a split).</summary>
    public void DockInto(DockGroup group, DockEdge edge) =>
        Perform(DockAction.MoveNode(Id, group.Node.GetId(), DockingManager.ToDockLocation(edge), -1));

    /// <summary>Pins an auto-hide pane into a docked group on its edge; no-op when the pane is not auto-hidden.</summary>
    public void Pin()
    {
        if (Node.Parent is BorderNode)
        {
            Perform(DockAction.PinTool(Id));
        }
    }

    /// <summary>Unpins the pane's docked group back to the auto-hide edge; no-op when the pane is not docked.</summary>
    public void Unpin()
    {
        if (Node.Parent is TabSetNode tabSet)
        {
            Perform(DockAction.UnpinTool(tabSet.GetId()));
        }
    }

    /// <summary>Copies the model's current state into the bindable properties without dispatching actions.</summary>
    internal void SyncFromModel()
    {
        if (_detached)
        {
            return;
        }
        _syncing = true;
        try
        {
            CommitTargetValue(TitleProperty, Node.Name);
            SetValue(IsActivePropertyKey, _manager.IsActiveNode(Node));
            SetValue(GroupPropertyKey, Node.Parent is TabSetNode tabSet ? _manager.GetOrCreateGroup(tabSet) : null);
            SetValue(EdgePropertyKey, DockingManager.EdgeOf(Node));
        }
        finally
        {
            _syncing = false;
        }
    }

    /// <summary>Stops this handle from acting on the layout; it keeps its last values.</summary>
    internal void Detach() => _detached = true;

    private void OnTitleChanged(string? title)
    {
        // Attaching a binding briefly clears the value to null; only a real new name renames the tab.
        if (title is not null && title != Node.Name)
        {
            Perform(DockAction.RenameTab(Id, title));
        }
    }

    private void Perform(DockAction action)
    {
        // Property writes that come from the model (or from a detached handle) must not act on the layout.
        if (!_syncing && !_detached)
        {
            _manager.PerformAction(action);
        }
    }
}
