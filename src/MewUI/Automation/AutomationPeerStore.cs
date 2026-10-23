using System.Runtime.CompilerServices;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>
/// Holds each element's peer outside the element, so an app with automation off carries no per-element field and
/// the store itself is trimmed away.
/// </summary>
internal static class AutomationPeerStore
{
    private sealed class Slot
    {
        public AutomationPeer? Peer;
    }

    private static readonly ConditionalWeakTable<UIElement, Slot> _slots = new();

    /// <summary>True once the element's peer was created; the peer may be null for an element without one.</summary>
    internal static bool TryGet(UIElement element, out AutomationPeer? peer)
    {
        if (_slots.TryGetValue(element, out var slot))
        {
            peer = slot.Peer;
            return true;
        }

        peer = null;
        return false;
    }

    internal static void Set(UIElement element, AutomationPeer? peer) => _slots.AddOrUpdate(element, new Slot { Peer = peer });
}
