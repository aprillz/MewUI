using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>
/// Decides which elements get a peer. Each framework control creates its own built-in peer in its
/// <c>OnCreateAutomationPeer</c>, so an app carries only the peers of the controls it uses.
/// </summary>
internal static class AutomationPeerFactory
{
    /// <summary>
    /// True when the nearest ancestor in the tree is a leaf. An element below a leaf got no peer, so the walk passes
    /// over it to the leaf itself.
    /// </summary>
    internal static bool IsBelowLeaf(UIElement element)
    {
        for (Element? ancestor = element.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor is UIElement ancestorElement && ancestorElement.GetAutomationPeer() is UIElementAutomationPeer peer)
            {
                return peer.IsLeaf;
            }
        }

        return false;
    }
}
