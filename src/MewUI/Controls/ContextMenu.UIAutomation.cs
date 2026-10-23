using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

public sealed partial class ContextMenu : IMenuAutomationHost
{
    int IMenuAutomationHost.EntryCount => Items.Count;

    MenuEntry IMenuAutomationHost.EntryAt(int index) => Items[index];

    int IMenuAutomationHost.OpenEntryIndex => _openSubMenuIndex;

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
}
