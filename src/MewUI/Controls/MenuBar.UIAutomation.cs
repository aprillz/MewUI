using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

public sealed partial class MenuBar : IMenuAutomationHost
{
    int IMenuAutomationHost.EntryCount => _items.Count;

    MenuEntry IMenuAutomationHost.EntryAt(int index) => _items[index];

    int IMenuAutomationHost.OpenEntryIndex => _openIndex;

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
}
