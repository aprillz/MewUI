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
    /// <summary>The pane title. Binds two-way by default; writes rename the tab.</summary>
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

    /// <summary>The edge this pane is docked or auto-hidden on, or null for a document / floating pane.</summary>
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

    /// <summary>The pane title shown by the default header. Setting a new non-null title renames the tab through the
    /// reducer; null is ignored.</summary>
    public string? Title
    {
        get => GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The serialized component key (used by <see cref="DockingManager.ContentFactory"/> to restore content).</summary>
    public string? Component => Node.Component;

    /// <summary>Document pane (top tabs, maximize) vs plain pane (edge dock / auto-hide).</summary>
    public bool IsDocument => Node.IsDocument;

    /// <summary>The group (tabset) this pane lives in, or null when it is auto-hidden in a border.</summary>
    public DockGroup? Group => GetValue(GroupProperty);

    /// <summary>The edge this pane is docked or auto-hidden on, or null for a document / floating pane.</summary>
    public DockEdge? Edge => GetValue(EdgeProperty);

    /// <summary>Whether this is the active pane (the selected pane of the focused group).</summary>
    public bool IsActive => GetValue(IsActiveProperty);

    /// <summary>The content given to AddDocumentPane/AddPane, if any (factory-restored panes return null).</summary>
    public UIElement? Content => _detached ? null : _manager.GetExplicitContent(Id);

    /// <summary>Makes this the active pane and gives its content the keyboard focus. A revealed auto-hide tool stays
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

    /// <summary>Splits this pane off its current group toward <paramref name="edge"/> of that group. No-op when the
    /// pane is alone in its group (nothing to split from).</summary>
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

    /// <summary>Pins an auto-hide pane into a docked group on its edge. No-op when not auto-hidden.</summary>
    public void Pin()
    {
        if (Node.Parent is BorderNode)
        {
            Perform(DockAction.PinTool(Id));
        }
    }

    /// <summary>Unpins the pane's docked group back to the auto-hide edge. No-op when not docked.</summary>
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
