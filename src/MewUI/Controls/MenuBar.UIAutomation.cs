using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

public sealed partial class MenuBar : IMenuAutomationHost
{
    int IMenuAutomationHost.EntryCount => _items.Count;

    MenuEntry IMenuAutomationHost.EntryAt(int index) => _items[index];

    int IMenuAutomationHost.OpenEntryIndex => _openIndex;

    int IMenuAutomationHost.HotEntryIndex => _hotIndex;

    bool IMenuAutomationHost.InvokesEntries => false;

    bool IMenuAutomationHost.TryGetEntryBounds(int index, out Rect bounds)
    {
        if ((uint)index < (uint)_itemBounds.Count)
        {
            bounds = _itemBounds[index];
            return true;
        }

        bounds = Rect.Empty;
        return false;
    }

    void IMenuAutomationHost.InvokeEntry(int index)
    {
    }

    void IMenuAutomationHost.OpenEntry(int index) => OpenMenu(index);

    void IMenuAutomationHost.CloseOpenEntry() => CloseOpenMenu();

    /// <summary>Tells readers the highlight is about to move to the entry under the pointer.</summary>
    private void QueueHotEntryAnnouncement(MouseEventArgs e)
    {
        if (!AutomationEvents.HasSinks)
        {
            return;
        }

        int index = HitTestItemIndex(TransformFromRoot(e.Position));
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
}
