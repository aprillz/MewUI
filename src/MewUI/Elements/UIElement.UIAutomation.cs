using Aprillz.MewUI.Automation;

namespace Aprillz.MewUI.Controls;

public abstract partial class UIElement
{
    /// <summary>
    /// Creates the peer that describes this element to automation: the built-in peer for a framework control, null
    /// otherwise. Null leaves the element out of the automation tree and presents its children in its place, which
    /// is right for panels and decorators.
    /// </summary>
    protected virtual AutomationPeer? OnCreateAutomationPeer() => AutomationPeerFactory.Create(this);

    /// <summary>Gets the element's automation peer, creating it on first use; null when automation is off or the element has none.</summary>
    public AutomationPeer? GetAutomationPeer()
    {
        if (!AutomationGate.IsSupported)
        {
            return null;
        }

        if (!AutomationPeerStore.TryGet(this, out var peer))
        {
            peer = OnCreateAutomationPeer();
            AutomationPeerStore.Set(this, peer);
        }

        return peer;
    }

    /// <summary>The peer only if something already asked for it; changes are announced only for peers in use.</summary>
    internal AutomationPeer? AutomationPeerIfCreated
        => AutomationPeerStore.TryGet(this, out var peer) ? peer : null;

    /// <summary>Announces that the children of the nearest ancestor-or-self peer in use changed.</summary>
    internal static void NotifyAutomationStructureChanged(Element? element)
    {
        if (!AutomationEvents.HasSinks)
        {
            return;
        }

        for (var current = element; current != null; current = current.Parent)
        {
            if (current is UIElement uiElement && uiElement.AutomationPeerIfCreated != null)
            {
                uiElement.AutomationPeerIfCreated.RaiseStructureChangedFromTree();
                return;
            }
        }
    }
}
