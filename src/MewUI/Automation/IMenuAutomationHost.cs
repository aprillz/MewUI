using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>A control that draws menu entries itself and lets automation operate them as the pointer would.</summary>
internal interface IMenuAutomationHost
{
    int EntryCount { get; }

    MenuEntry EntryAt(int index);

    /// <summary>The entry whose submenu is open, or -1.</summary>
    int OpenEntryIndex { get; }

    /// <summary>True when an entry without a submenu runs its command; false for a bar whose entries only open menus.</summary>
    bool InvokesEntries { get; }

    /// <summary>The entry's row in the host's bounds space.</summary>
    bool TryGetEntryBounds(int index, out Rect bounds);

    void InvokeEntry(int index);

    void OpenEntry(int index);

    void CloseOpenEntry();
}
