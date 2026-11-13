using Aprillz.MewUI.Automation;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

public partial class Window
{
    internal int OpenPopupCount => _popupManager.Count;

    internal UIElement OpenPopupAt(int index) => _popupManager.ElementAt(index);

    /// <summary>The subtree this window shows for another window as a popup surface, or null.</summary>
    internal Element? HostedPortalRoot => _hostedPortalRoot;

    /// <summary>
    /// Converts a rectangle in this window's client area to screen units, the way the popup holding
    /// <paramref name="element"/> converts it when that popup stays where it opened while the window moves.
    /// </summary>
    internal Rect AutomationClientToScreen(Rect clientRect, UIElement? element)
    {
        var pinnedOrigin = _popupManager.ResolvePlacementClientOrigin(element);
        var topLeft = PopupClientToScreen(clientRect.TopLeft, pinnedOrigin);
        var bottomRight = PopupClientToScreen(clientRect.BottomRight, pinnedOrigin);
        return new Rect(topLeft.X, topLeft.Y, Math.Max(0, bottomRight.X - topLeft.X), Math.Max(0, bottomRight.Y - topLeft.Y));
    }

    protected override AutomationPeer? OnCreateAutomationPeer() => new WindowAutomationPeer(this);
}
