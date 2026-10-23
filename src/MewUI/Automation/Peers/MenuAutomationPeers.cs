using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>
/// The peer of a <see cref="MenuBar"/> or an open <see cref="ContextMenu"/>. The entries are drawn by the control,
/// so each menu item is a part peer; separators are left out.
/// </summary>
internal sealed class MenuAutomationPeer : UIElementAutomationPeer
{
    private readonly Dictionary<MenuItem, MenuEntryAutomationPeer> _entryPeers = new();

    public MenuAutomationPeer(UIElement owner)
        : base(owner)
    {
    }

    internal IMenuAutomationHost MenuHost => (IMenuAutomationHost)Owner;

    protected override AutomationControlType GetControlTypeCore()
        => Owner is MenuBar ? AutomationControlType.MenuBar : AutomationControlType.Menu;

    internal override bool IsLeaf => true;

    protected override IReadOnlyList<AutomationPeer> GetChildrenCore()
    {
        var host = MenuHost;
        var children = new List<AutomationPeer>();
        var live = new HashSet<MenuItem>();
        for (int index = 0; index < host.EntryCount; index++)
        {
            if (host.EntryAt(index) is not MenuItem item)
            {
                continue;
            }

            var kind = MenuEntryAutomationPeer.KindOf(host, item);
            if (!_entryPeers.TryGetValue(item, out var peer) || peer.Kind != kind)
            {
                peer = MenuEntryAutomationPeer.Create(this, item, kind);
                _entryPeers[item] = peer;
            }

            peer.Index = index;
            live.Add(item);
            children.Add(peer);
        }

        foreach (var stale in _entryPeers.Keys.Where(item => !live.Contains(item)).ToList())
        {
            _entryPeers.Remove(stale);
        }

        return children;
    }
}

/// <summary>What an automation client can do with a menu entry.</summary>
internal enum MenuEntryKind
{
    /// <summary>Nothing: a bar entry with no submenu.</summary>
    Plain,
    Invoke,
    Checkable,
    Submenu,
}

/// <summary>The peer of one menu item, named by its text with the access key marker removed.</summary>
internal class MenuEntryAutomationPeer : PartAutomationPeer
{
    private protected MenuEntryAutomationPeer(MenuAutomationPeer menuPeer, MenuItem item, MenuEntryKind kind)
        : base(menuPeer)
    {
        Item = item;
        Kind = kind;
    }

    internal MenuItem Item { get; }

    internal MenuEntryKind Kind { get; }

    /// <summary>The entry's position in its menu, refreshed each time the menu lists its children.</summary>
    internal int Index { get; set; }

    private protected IMenuAutomationHost MenuHost => ((MenuAutomationPeer)HostPeer).MenuHost;

    internal static MenuEntryKind KindOf(IMenuAutomationHost host, MenuItem item)
    {
        if (item.SubMenu != null)
        {
            return MenuEntryKind.Submenu;
        }

        if (!host.InvokesEntries)
        {
            return MenuEntryKind.Plain;
        }

        return item.IsCheckable ? MenuEntryKind.Checkable : MenuEntryKind.Invoke;
    }

    internal static MenuEntryAutomationPeer Create(MenuAutomationPeer menuPeer, MenuItem item, MenuEntryKind kind) => kind switch
    {
        MenuEntryKind.Submenu => new SubmenuEntryAutomationPeer(menuPeer, item),
        MenuEntryKind.Checkable => new CheckableMenuEntryAutomationPeer(menuPeer, item),
        MenuEntryKind.Invoke => new InvokableMenuEntryAutomationPeer(menuPeer, item, kind),
        _ => new MenuEntryAutomationPeer(menuPeer, item, kind),
    };

    protected override AutomationControlType GetControlTypeCore() => AutomationControlType.MenuItem;

    protected override string GetNameCore() => Item.GetParsedText().displayText;

    protected override bool IsEnabledCore() => base.IsEnabledCore() && Item.IsEffectivelyEnabled;

    protected override bool TryGetBoundsInHost(out Rect bounds)
    {
        if ((uint)Index < (uint)MenuHost.EntryCount && ReferenceEquals(MenuHost.EntryAt(Index), Item))
        {
            return MenuHost.TryGetEntryBounds(Index, out bounds);
        }

        bounds = Rect.Empty;
        return false;
    }
}

/// <summary>A menu item that runs its command.</summary>
internal class InvokableMenuEntryAutomationPeer : MenuEntryAutomationPeer, IInvokeProvider
{
    internal InvokableMenuEntryAutomationPeer(MenuAutomationPeer menuPeer, MenuItem item, MenuEntryKind kind)
        : base(menuPeer, item, kind)
    {
    }

    public void Invoke()
    {
        AutomationGuard.ThrowIfDisabled(this);
        MenuHost.InvokeEntry(Index);
    }
}

/// <summary>A checkable menu item. Its command decides the checked state, so toggling runs the command.</summary>
internal sealed class CheckableMenuEntryAutomationPeer : InvokableMenuEntryAutomationPeer, IToggleProvider
{
    internal CheckableMenuEntryAutomationPeer(MenuAutomationPeer menuPeer, MenuItem item)
        : base(menuPeer, item, MenuEntryKind.Checkable)
    {
    }

    public ToggleState ToggleState => Item.IsChecked ? ToggleState.On : ToggleState.Off;

    public void Toggle() => Invoke();
}

/// <summary>A menu item that opens a submenu. The open submenu is a menu peer of its own under the window.</summary>
internal sealed class SubmenuEntryAutomationPeer : MenuEntryAutomationPeer, IExpandCollapseProvider
{
    internal SubmenuEntryAutomationPeer(MenuAutomationPeer menuPeer, MenuItem item)
        : base(menuPeer, item, MenuEntryKind.Submenu)
    {
    }

    public ExpandCollapseState ExpandCollapseState
        => MenuHost.OpenEntryIndex == Index ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    public void Expand()
    {
        AutomationGuard.ThrowIfDisabled(this);
        MenuHost.OpenEntry(Index);
    }

    public void Collapse()
    {
        if (MenuHost.OpenEntryIndex == Index)
        {
            MenuHost.CloseOpenEntry();
        }
    }
}
