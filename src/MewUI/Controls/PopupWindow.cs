using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

/// <summary>
/// A framework-internal, non-activating popup surface: a top-level OS window that hosts an
/// externally-owned popup subtree as its visual root without adopting it (the subtree's Parent stays
/// with the owner window). It sizes itself to that subtree and floats above the owner without stealing
/// focus, but unlike an overlay it still receives mouse input.
/// </summary>
internal sealed class PopupWindow : Window
{
    public PopupWindow(Element hostedRoot, Size clientSize, bool transparent = true)
    {
        Kind = WindowKind.Popup;
        Topmost = true;
        ShowInTaskbar = false;
        StartupLocation = WindowStartupLocation.Manual;
        AllowsTransparency = transparent;
        Padding = new(0);
        Background = transparent ? Color.Transparent : Theme.Palette.WindowBackground;
        // Fixed size driven by the host (owner-computed popup bounds + chrome padding). Fit-to-content
        // would re-measure the content unconstrained and balloon wide popups (e.g. dropdown lists).
        WindowSize = WindowSize.Fixed(Math.Max(1, clientSize.Width), Math.Max(1, clientSize.Height));
        SetHostedPortalRoot(hostedRoot);
    }

    /// <summary>Raised when the platform dismiss watch requests light-dismiss (outside press, watch stolen).</summary>
    internal Action? DismissRequested { get; set; }

    /// <summary>Decides whether the window taking over the dismiss watch is a related popup surface.</summary>
    internal Func<nint, bool>? WatchTransferAllowed { get; set; }

    /// <summary>
    /// Shows this popup surface at the given screen position (DIPs) in one atomic operation:
    /// the position is latched before the backend maps the window, and the dismiss watch is armed
    /// once it is visible. Set <see cref="DismissRequested"/> before calling to opt into the watch.
    /// </summary>
    internal void ShowSurface(Window owner, Point? screenPosition)
    {
        // Position must be resolved before Show: backends place the window before mapping it,
        // and mapping first would flash the surface at a stale position.
        if (screenPosition is Point position)
        {
            StartupPosition = position;
        }

        Show(owner);

        if (DismissRequested != null)
        {
            RecapturePopupSurface();
        }
    }

    /// <summary>
    /// Dismisses this popup surface in one atomic operation: callbacks are dropped, the surface is
    /// unmapped, the hosted portal content is detached, and the window is closed.
    /// </summary>
    internal void DismissSurface()
    {
        DismissRequested = null;
        WatchTransferAllowed = null;

        // Unmap BEFORE detaching the portal content: once the portal root is nulled, any render
        // pass on this window paints an empty frame, and on platforms where close/unmap takes a
        // round-trip those blank frames reach the screen as a visible flicker of the old popup.
        Hide();

        SetHostedPortalRoot(null);
        Close();
    }

    internal override void OnPopupSurfaceOutsidePress() => DismissRequested?.Invoke();

    internal override bool OnPopupSurfaceWatchTransfer(nint newHolderHandle)
    {
        if (WatchTransferAllowed?.Invoke(newHolderHandle) == true)
        {
            return true;
        }

        DismissRequested?.Invoke();
        return false;
    }

    internal override bool IsPopupPressForwardTarget(nint windowHandle)
    {
        if (windowHandle == 0)
        {
            return false;
        }

        if (Owner is Window owner && owner.Handle == windowHandle)
        {
            return true;
        }

        return WatchTransferAllowed?.Invoke(windowHandle) == true;
    }
}
