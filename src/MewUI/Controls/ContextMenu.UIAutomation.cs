using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

public sealed partial class ContextMenu : IMenuAutomationHost
{
    int IMenuAutomationHost.EntryCount => Items.Count;

    MenuEntry IMenuAutomationHost.EntryAt(int index) => Items[index];

    int IMenuAutomationHost.OpenEntryIndex => _openSubMenuIndex;

    int IMenuAutomationHost.HotEntryIndex => _hotIndex;

    bool IMenuAutomationHost.InvokesEntries => true;

    bool IMenuAutomationHost.TryGetEntryBounds(int index, out Rect bounds) => TryGetEntryRowBounds(index, out bounds);

    void IMenuAutomationHost.InvokeEntry(int index)
    {
        if (Items[index] is MenuItem item && item.SubMenu == null)
        {
            InvokeItem(item);
            if (FindVisualRoot() is Window window)
            {
                CloseHierarchy(window);
            }
        }
    }

    void IMenuAutomationHost.OpenEntry(int index)
    {
        if (Items[index] is MenuItem item && item.SubMenu != null && TryGetEntryRowBounds(index, out var rowBounds))
        {
            OpenSubMenu(index, item.SubMenu, rowBounds);
        }
    }

    void IMenuAutomationHost.CloseOpenEntry() => CloseSubMenu();

    /// <summary>Tells readers the highlight is about to move to the entry under the pointer.</summary>
    private void QueueHotEntryAnnouncement(MouseEventArgs e)
    {
        if (!AutomationEvents.HasSinks)
        {
            return;
        }

        int index = HitTestEntryIndex(TransformFromRoot(e.Position));
        if (index >= 0 && index != _hotIndex && GetAutomationPeer() is MenuAutomationPeer peer)
        {
            peer.QueueHotEntryAnnouncement(index);
        }
    }

    /// <summary>Tells readers about the entry a key moves the highlight to, once the key has been handled.</summary>
    private void QueueHotEntryAnnouncementAfterKey()
    {
        if (AutomationEvents.HasSinks && GetAutomationPeer() is MenuAutomationPeer peer)
        {
            // Which entry the key leaves highlighted is known only after it is handled, so nothing is said at once.
            peer.QueueHotEntryAnnouncement(-1);
        }
    }

    protected override AutomationPeer? OnCreateAutomationPeer() => new MenuAutomationPeer(this);
}
