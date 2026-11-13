using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Automation;

/// <summary>
/// Splits the automation tree by the native window that shows each peer, for consumers that expose one tree per
/// native window. The content of a popup shown in a popup window of its own belongs to that window's tree, although
/// its peer's parent is the owner window.
/// </summary>
internal static class AutomationSurface
{
    /// <summary>The window whose native surface shows <paramref name="peer"/>, or null when it is not shown.</summary>
    internal static Window? Of(AutomationPeer peer)
    {
        switch (peer)
        {
            case WindowAutomationPeer windowPeer:
                return (Window)windowPeer.Owner;
            case UIElementAutomationPeer elementPeer:
                return elementPeer.Owner.ResolveInputHostWindow();
            case PartAutomationPeer partPeer:
                return partPeer.HostPeer.Owner.ResolveInputHostWindow();
            default:
                return peer.GetWindow();
        }
    }

    /// <summary>The parent of <paramref name="peer"/> in its window's tree: a popup window stands in for the owner window.</summary>
    internal static AutomationPeer? ParentOf(AutomationPeer peer)
    {
        var parent = peer.GetParent();
        if (parent is WindowAutomationPeer windowPeer && Of(peer) is Window surface && !ReferenceEquals(surface, windowPeer.Owner))
        {
            return surface.GetAutomationPeer();
        }

        return parent;
    }

    /// <summary>
    /// The children of <paramref name="peer"/> in its window's tree. A window leaves out the popups shown in popup
    /// windows of their own, and a popup window lists the content it shows.
    /// </summary>
    internal static IReadOnlyList<AutomationPeer> ChildrenOf(AutomationPeer peer)
    {
        if (peer is not WindowAutomationPeer windowPeer)
        {
            return peer.GetChildren();
        }

        var window = (Window)windowPeer.Owner;
        var children = new List<AutomationPeer>();
        if (window.HostedPortalRoot is Element portal)
        {
            UIElementAutomationPeer.CollectChildren(portal, children);
        }
        else
        {
            foreach (var child in windowPeer.GetChildren())
            {
                if (ReferenceEquals(Of(child), window))
                {
                    children.Add(child);
                }
            }
        }

        return children;
    }

    /// <summary>The bounds of <paramref name="peer"/> in screen units, or an empty rectangle when it has none.</summary>
    internal static Rect ScreenBoundsOf(AutomationPeer peer)
    {
        var window = peer.GetWindow();
        if (window == null)
        {
            return Rect.Empty;
        }

        var bounds = peer.GetBoundingRectangle();
        if (bounds.IsEmpty)
        {
            return Rect.Empty;
        }

        return window.AutomationClientToScreen(bounds, ElementOf(peer));
    }

    /// <summary>
    /// The deepest shown peer under <paramref name="screenPoint"/> in the tree below <paramref name="root"/>, the
    /// topmost child first; <paramref name="root"/> itself when none of its children is there.
    /// </summary>
    internal static AutomationPeer HitTest(AutomationPeer root, Point screenPoint)
    {
        var current = root;
        while (true)
        {
            AutomationPeer? hit = null;
            var children = ChildrenOf(current);
            for (int index = children.Count - 1; index >= 0; index--)
            {
                var child = children[index];
                if (!child.IsOffscreen() && ScreenBoundsOf(child).Contains(screenPoint))
                {
                    hit = child;
                    break;
                }
            }

            if (hit == null)
            {
                return current;
            }

            current = hit;
        }
    }

    /// <summary>The peer that has keyboard focus in the tree of <paramref name="surface"/>, or null when focus is elsewhere.</summary>
    internal static AutomationPeer? FocusedIn(Window surface)
    {
        // Content shown in a popup window keeps the owner window's focus.
        var focusWindow = surface.HostedPortalRoot?.FindVisualRoot() as Window ?? surface;
        AutomationPeer? focused = null;
        for (Element? current = focusWindow.FocusManager.FocusedElement; current != null; current = current.Parent)
        {
            if (current is UIElement element && element.GetAutomationPeer() is AutomationPeer candidate
                && AutomationProperties.GetAccessibilityView(element) != AccessibilityView.Raw)
            {
                focused = candidate;
                break;
            }
        }

        if (focused is UIElementAutomationPeer elementPeer && elementPeer.FocusedDescendant is AutomationPeer descendant)
        {
            focused = descendant;
        }

        if (focused != null && ReferenceEquals(Of(focused), surface))
        {
            return focused;
        }
        else
        {
            return null;
        }
    }

    private static UIElement? ElementOf(AutomationPeer peer) => peer switch
    {
        UIElementAutomationPeer elementPeer => elementPeer.Owner,
        PartAutomationPeer partPeer => partPeer.HostPeer.Owner,
        _ => null,
    };
}
