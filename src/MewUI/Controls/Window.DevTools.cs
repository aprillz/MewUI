using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Diagnostics;

namespace Aprillz.MewUI;

public partial class Window
{
    private readonly WindowDevTools? _devTools;
    private UIElement? _lastInspectorHover;
    private bool _lastInspectorInfoPanelAvoidsMouse;

    /// <summary>This window's development tools, or null when the app did not opt into them.</summary>
    public WindowDevTools? DevTools => _devTools;

    // Forwarders so the DevTools windows keep reaching the overlay through the window they target.
    internal DebugInspectorOverlay? DebugInspectorOverlay => _devTools?.InspectorOverlay;

    internal void ToggleDebugInspector() => _devTools?.ToggleInspector();

    // Relays for the DevTools windows, which live outside Window and cannot see these private
    // members. Kept as index accessors so PopupManager/AdornerEntry stay unexposed.
    internal int DebugPopupCount => _popupManager.Count;

    internal Element DebugPopupAt(int index) => _popupManager.ElementAt(index);

    internal int DebugAdornerCount => _adorners.Count;

    internal Element DebugAdornerElementAt(int index) => _adorners[index].Element;

    /// <summary>
    /// Called from <see cref="UpdateLastMousePosition"/>. Triggers an overlay redraw only
    /// when the element under the cursor actually changes, so cursor moves inside a single
    /// element no longer churn the inspector at every input tick.
    /// </summary>
    private void InvalidateInspectorOverlayIfHoverChanged()
    {
        var overlay = _devTools?.InspectorOverlay;
        if (overlay == null)
        {
            _lastInspectorHover = null;
            return;
        }

        var hovered = HitTest(_lastMousePositionDip);
        if (hovered is Adorner)
        {
            hovered = null;
        }

        bool infoPanelAvoidsMouse = overlay.ShouldAvoidMouse(_lastMousePositionDip);
        if (ReferenceEquals(hovered, _lastInspectorHover) &&
            infoPanelAvoidsMouse == _lastInspectorInfoPanelAvoidsMouse)
        {
            return;
        }

        _lastInspectorHover = hovered;
        _lastInspectorInfoPanelAvoidsMouse = infoPanelAvoidsMouse;
        overlay.InvalidateVisual();
    }
}
