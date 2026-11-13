using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>Turns element changes into automation notifications for the peers in use.</summary>
internal static class ElementAutomationEvents
{
    /// <summary>
    /// Passes a property change to the element's peer in use. Keyboard focus is announced even when no client has
    /// asked for the focused element yet: its peer is created, or the nearest ancestor peer stands in when it has none.
    /// </summary>
    internal static void OnPropertyChanged(UIElement element, MewProperty property)
    {
        var peer = element.AutomationPeerIfCreated;
        if (peer == null && property == UIElement.IsFocusedProperty && element.IsFocused && AutomationEvents.HasSinks)
        {
            peer = element.GetAutomationPeer();
            if (peer == null)
            {
                for (var ancestor = element.Parent; ancestor != null; ancestor = ancestor.Parent)
                {
                    if (ancestor is UIElement ancestorElement && ancestorElement.GetAutomationPeer() is AutomationPeer ancestorPeer)
                    {
                        ancestorPeer.RaiseFocusChangedFromTree();
                        return;
                    }
                }

                return;
            }
        }

        peer?.NotifyOwnerPropertyChanged(property);
    }

    /// <summary>
    /// Announces a move of <paramref name="child"/> to its old and new parents' peers, and the opening or closing of a
    /// menu or tooltip when the child is the content of a popup.
    /// </summary>
    internal static void OnParentChanged(Element child, Element? previousParent, Element? newParent)
    {
        UIElement.NotifyAutomationStructureChanged(previousParent);
        UIElement.NotifyAutomationStructureChanged(newParent);

        if (child is not UIElement element || !AutomationEvents.HasSinks)
        {
            return;
        }

        if (previousParent is PopupChrome && element.AutomationPeerIfCreated is AutomationPeer closedPeer
            && PopupEventOf(closedPeer, opened: false) is AutomationEventKind closedKind)
        {
            closedPeer.RaiseEventFromTree(closedKind);
        }

        if (newParent is PopupChrome)
        {
            // Raised once the popup is placed and shown, so a reader that asks where it is gets its real bounds.
            var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(DispatcherPriority.Background, () => AnnouncePopupOpened(element));
            }
            else
            {
                AnnouncePopupOpened(element);
            }
        }
    }

    private static void AnnouncePopupOpened(UIElement element)
    {
        if (element.Parent is PopupChrome && element.GetAutomationPeer() is AutomationPeer peer
            && PopupEventOf(peer, opened: true) is AutomationEventKind kind)
        {
            peer.RaiseEventFromTree(kind);
        }
    }

    private static AutomationEventKind? PopupEventOf(AutomationPeer peer, bool opened) => peer.GetControlType() switch
    {
        AutomationControlType.Menu => opened ? AutomationEventKind.MenuOpened : AutomationEventKind.MenuClosed,
        AutomationControlType.ToolTip => opened ? AutomationEventKind.ToolTipOpened : AutomationEventKind.ToolTipClosed,
        _ => null,
    };
}
