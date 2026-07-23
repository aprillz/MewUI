using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Hosts popups in their own non-activating OS windows (<see cref="PopupWindow"/>). The popup subtree
/// stays rooted in the owner window (portal model, see <see cref="PopupHostSupport"/>); this host only
/// relocates its pixels and input to a top-level surface positioned in screen coordinates, so a popup can
/// extend beyond the owner window's client area. Owner-surface render/hit-test are no-ops here.
/// </summary>
internal sealed class NativePopupHost : IPopupHost
{
    // Upper bound for the popup window's fit-to-content sizing; real work-area clamping is a placement refinement.
    private const double MAX_POPUP_EXTENT = 8192;

    private readonly Window _ownerWindow;
    private readonly List<PopupEntry> _popups;

    internal NativePopupHost(Window ownerWindow, List<PopupEntry> popups)
    {
        _ownerWindow = ownerWindow;
        _popups = popups;
    }

    public void Attach(PopupEntry entry, bool sizeToContent)
    {
        // Chrome is already attached (and the popup style-resolved) by PopupManager before placement
        // was measured; this host only builds the OS surface around it.
        var chrome = (PopupChrome)entry.Chrome!;

        if (sizeToContent)
        {
            entry.Bounds = PopupHostSupport.ResizeToContentWidth(entry.Element, entry.Bounds, MAX_POPUP_EXTENT);
        }

        var initialChromeBounds = SnapRectToDevice(entry.Bounds.Inflate(PopupChrome.ShadowPadding));
        var popupWindow = new PopupWindow(chrome, new Size(initialChromeBounds.Width, initialChromeBounds.Height));
        // The portal subtree is arranged at the chrome's owner-client position, so element bounds
        // inside the popup stay in the owner's coordinate space (identical to in-surface hosting);
        // the popup window translates by this origin at its render/input edges.
        var initialOrigin = new Point(initialChromeBounds.X, initialChromeBounds.Y);
        popupWindow.HostedPortalOrigin = initialOrigin;
        chrome.HostSurface = popupWindow;
        entry.NativeWindow = popupWindow;

        // Platform dismiss watch: an outside press (or the watch being stolen by an unrelated window)
        // light-dismisses the whole transient chain via the owner's close policy. A transfer to a
        // sibling popup surface (submenu) is not a dismiss. Hit-test-invisible popups (ToolTip) are
        // not interactive dismiss surfaces and must NOT capture: capture would redirect mouse moves
        // away from the owner, breaking the hover tracking that keeps the tooltip alive.
        if (entry.Element.IsHitTestVisible)
        {
            popupWindow.WatchTransferAllowed = IsPopupSurfaceHandle;
            popupWindow.DismissRequested = () =>
                _ownerWindow.RequestClosePopups(PopupCloseRequest.PointerDown(null));
        }

        popupWindow.ShowSurface(_ownerWindow, ResolveScreenPosition(initialOrigin));
    }

    private Point? ResolveScreenPosition(Point origin)
    {
        if (_ownerWindow.Handle == 0)
        {
            return null;
        }

        var screenPx = _ownerWindow.ClientToScreen(origin);
        double scale = ResolveScreenScale(screenPx);
        return new Point(screenPx.X / scale, screenPx.Y / scale);
    }

    // The popup content snaps to device pixels in the owner's coordinate space, so the surface it lives on
    // must too: a fractional origin (at e.g. 125% DPI) translates the whole surface onto a fractional device
    // pixel and blurs it, and a fractional size rounds the client area a pixel off the content and leaves a
    // seam at the bottom/right. Snap all four edges so origin and size stay whole device pixels together.
    private Rect SnapRectToDevice(Rect rect)
    {
        double dpiScale = _ownerWindow.DpiScale;
        double x = LayoutRounding.RoundToPixel(rect.X, dpiScale);
        double y = LayoutRounding.RoundToPixel(rect.Y, dpiScale);
        double right = LayoutRounding.RoundToPixel(rect.X + rect.Width, dpiScale);
        double bottom = LayoutRounding.RoundToPixel(rect.Y + rect.Height, dpiScale);
        return new Rect(x, y, right - x, bottom - y);
    }

    private bool IsPopupSurfaceHandle(nint handle)
    {
        if (handle == 0)
        {
            return false;
        }

        for (int i = 0; i < _popups.Count; i++)
        {
            if (_popups[i].NativeWindow is PopupWindow surface && surface.Handle == handle)
            {
                return true;
            }
        }

        return false;
    }

    public void Layout(PopupEntry entry)
    {
        var popupWindow = entry.NativeWindow;
        if (popupWindow == null || _ownerWindow.Handle == 0)
        {
            return;
        }

        // The popup content sits at entry.Bounds in owner-client DIPs; the chrome (with shadow padding)
        // starts above-left of it. Size the popup window exactly to the chrome and place it so the
        // content lands at the same screen position it would occupy in-surface; the portal layout
        // arranges the chrome at that same owner-client position (HostedPortalOrigin).
        var chromeBounds = SnapRectToDevice(entry.Bounds.Inflate(PopupChrome.ShadowPadding));
        var origin = new Point(chromeBounds.X, chromeBounds.Y);
        popupWindow.HostedPortalOrigin = origin;
        var currentSize = popupWindow.WindowSize;
        if (currentSize.Width != chromeBounds.Width || currentSize.Height != chromeBounds.Height)
        {
            popupWindow.WindowSize = WindowSize.Fixed(Math.Max(1, chromeBounds.Width), Math.Max(1, chromeBounds.Height));
        }

        var screenPx = _ownerWindow.ClientToScreen(origin);
        double scale = ResolveScreenScale(screenPx);
        popupWindow.MoveTo(screenPx.X / scale, screenPx.Y / scale);
    }

    public void UpdateBounds(PopupEntry entry, Rect bounds)
    {
        entry.Bounds = bounds;
        Layout(entry);
    }

    public void OnOwnerChanged(PopupEntry entry)
    {
        if (entry.Chrome is PopupChrome chrome)
        {
            chrome.ContextParentOverride = entry.Owner;
        }
    }

    public void Detach(PopupEntry entry)
    {
        if (entry.Chrome is PopupChrome chrome)
        {
            chrome.HostSurface = null;
        }

        if (entry.NativeWindow is PopupWindow popupWindow)
        {
            popupWindow.DismissSurface();
            entry.NativeWindow = null;
        }

        PopupHostSupport.DetachChrome(entry);

        // Closing a submenu drops the platform dismiss watch with it; re-arm it on the topmost
        // surviving interactive popup surface so outside presses keep dismissing the rest of the chain.
        for (int i = _popups.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(_popups[i].Host, this)
                && _popups[i].Element.IsHitTestVisible
                && _popups[i].NativeWindow is PopupWindow remaining)
            {
                remaining.RecapturePopupSurface();
                break;
            }
        }
    }

    // Native popups draw and hit-test in their own windows, so the owner surface does nothing for them.
    public void Render(IGraphicsContext context) { }

    public UIElement? HitTest(Point point) => null;

    public bool HasLayoutDirty()
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            var entry = _popups[i];
            if (!ReferenceEquals(entry.Host, this))
            {
                continue;
            }

            var element = entry.Element;
            if (element.IsMeasureDirty || element.IsArrangeDirty)
            {
                return true;
            }
        }

        return false;
    }

    public void LayoutDirty()
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            var entry = _popups[i];
            if (!ReferenceEquals(entry.Host, this) || !entry.Element.IsVisible)
            {
                continue;
            }

            if (!entry.Element.IsMeasureDirty && !entry.Element.IsArrangeDirty)
            {
                continue;
            }

            Layout(entry);
        }
    }

    public void NotifyThemeChanged(Theme oldTheme, Theme newTheme)
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            var entry = _popups[i];
            if (!ReferenceEquals(entry.Host, this))
            {
                continue;
            }

            PopupHostSupport.ApplyThemeChange((UIElement?)entry.Chrome ?? entry.Element, oldTheme, newTheme);
        }
    }

    public void NotifyDpiChanged(uint oldDpi, uint newDpi)
    {
        for (int i = 0; i < _popups.Count; i++)
        {
            var entry = _popups[i];
            if (!ReferenceEquals(entry.Host, this))
            {
                continue;
            }

            PopupHostSupport.ApplyDpiChange((UIElement?)entry.Chrome ?? entry.Element, oldDpi, newDpi);
        }
    }

    private double ResolveScreenScale(Point screenPx)
    {
        if (!Application.IsRunning)
        {
            return 1.0;
        }

        uint dpi = Application.Current.PlatformHost.GetDpiForPoint(screenPx);
        return dpi > 0 ? dpi / 96.0 : 1.0;
    }
}
