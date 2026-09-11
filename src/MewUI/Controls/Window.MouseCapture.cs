using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI;

public partial class Window
{
    // The window holding the platform mouse capture on this UI thread; platforms scope the capture to the thread.
    [ThreadStatic]
    private static Window? _osCaptureHolder;

    // Popup surfaces with an armed dismiss watch, most recently armed last; releasing a capture arms the last one again.
    [ThreadStatic]
    private static List<Window>? _armedWatches;

    // The button whose press was being routed when the element capture was taken; its release ends that capture.
    private MouseButton? _pressCaptureButton;

    /// <summary>Records that an element capture was taken while the press of <paramref name="button"/> was routed.</summary>
    internal void NotePressCapture(MouseButton button) => _pressCaptureButton = button;

    /// <summary>
    /// Ends a capture taken while the press of <paramref name="button"/> was routed, once its release has been
    /// routed. A capture that release handlers took in place of <paramref name="heldBeforeRelease"/> is kept.
    /// </summary>
    internal void EndPressCapture(MouseButton button, UIElement? heldBeforeRelease)
    {
        if (_pressCaptureButton != button)
        {
            return;
        }

        _pressCaptureButton = null;
        if (_capturedElement != null && ReferenceEquals(_capturedElement, heldBeforeRelease))
        {
            ReleaseLocalCapture();
        }
    }

    /// <summary>Ends the element capture when its holder is <paramref name="subtreeRoot"/> or one of its descendants.</summary>
    internal void ReleaseCaptureWithin(UIElement subtreeRoot)
    {
        if (IsWithin(_capturedElement, subtreeRoot))
        {
            ReleaseLocalCapture();
        }

        if (_captureDelegatedTo is Window delegated && IsWithin(delegated._capturedElement, subtreeRoot))
        {
            _captureDelegatedTo = null;
            delegated.ReleaseLocalCapture();
        }

        static bool IsWithin(UIElement? holder, UIElement root)
            => holder != null && (ReferenceEquals(holder, root) || root.IsAncestorOf(holder));
    }

    /// <summary>
    /// Called by the backend when the platform moved the mouse capture away from this window without the framework
    /// asking. <paramref name="newHolderHandle"/> is the window that took it, or 0 when nothing holds it.
    /// </summary>
    internal void OnOsCaptureLost(nint newHolderHandle)
    {
        if (!ReferenceEquals(_osCaptureHolder, this) || (newHolderHandle != 0 && newHolderHandle == Handle))
        {
            return;
        }

        _osCaptureHolder = null;
        if (newHolderHandle != 0)
        {
            LoseOsCapture(newHolderHandle);
        }
        else
        {
            // Released outside the framework with nothing taking it: an element capture ends and a dismiss watch goes back on.
            if (_capturedElement != null)
            {
                ClearMouseCaptureState();
            }

            RearmPopupWatch();
        }
    }

    /// <summary>Called by the backend when the platform cancelled the mouse mode: the capture ends and an armed popup dismisses.</summary>
    internal void OnOsCaptureCancelled()
    {
        if (!ReferenceEquals(_osCaptureHolder, this))
        {
            return;
        }

        _osCaptureHolder = null;
        LoseOsCapture(0);
    }

    /// <summary>Stops this popup surface's dismiss watch and ends any capture held in it, before the surface goes away.</summary>
    internal void EndPopupWatch()
    {
        _armedWatches?.Remove(this);
        ReleaseLocalCapture();
    }

    /// <summary>Whether the element may hold the mouse capture: in a window's tree, effectively enabled, and visible.</summary>
    private static bool CanHoldMouseCapture(UIElement element)
    {
        if (element.FindVisualRoot() is not Window || !element.IsEffectivelyEnabled)
        {
            return false;
        }

        for (Element? current = element; current != null && current is not Window; current = current.Parent)
        {
            if (current is UIElement uiElement && !uiElement.IsVisible)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Takes the platform mouse capture for this window; the window that held it loses what it held through it.</summary>
    private void AcquireOsCapture()
    {
        var previous = _osCaptureHolder;
        _osCaptureHolder = this;
        if (previous != null && !ReferenceEquals(previous, this))
        {
            previous.LoseOsCapture(Handle);
        }

        _backend?.CaptureMouse();
    }

    /// <summary>Gives the platform mouse capture back when this window holds it.</summary>
    private void ReleaseOsCapture()
    {
        if (!ReferenceEquals(_osCaptureHolder, this))
        {
            return;
        }

        _osCaptureHolder = null;
        _backend?.ReleaseMouseCapture();
    }

    /// <summary>Arms the dismiss watch of the most recently armed open popup surface again when no window holds the capture.</summary>
    private static void RearmPopupWatch()
    {
        var watches = _armedWatches;
        if (watches == null || _osCaptureHolder != null)
        {
            return;
        }

        for (int index = watches.Count - 1; index >= 0; index--)
        {
            var surface = watches[index];
            if (surface._lifetimeState == WindowLifetimeState.Closed || surface.Handle == 0)
            {
                watches.RemoveAt(index);
                continue;
            }

            surface.AcquireOsCapture();
            return;
        }
    }

    /// <summary>Ends what this window held through the platform capture after <paramref name="newHolderHandle"/> took it.</summary>
    private void LoseOsCapture(nint newHolderHandle)
    {
        if (_capturedElement != null)
        {
            ClearMouseCaptureState();
        }

        // A related surface (a submenu) taking the watch keeps the popup open; anything else asks it to dismiss.
        if (_armedWatches?.Contains(this) == true)
        {
            _ = OnPopupSurfaceWatchTransfer(newHolderHandle);
        }
    }

    /// <summary>Ends the element capture held in this window and gives the platform capture back.</summary>
    private void ReleaseLocalCapture()
    {
        ClearMouseCaptureState();
        ReleaseOsCapture();
        RearmPopupWatch();
    }

    /// <summary>Ends an element capture held through this window, in it or in the popup surface it delegated to.</summary>
    private void EndElementCapture()
    {
        if (_capturedElement != null)
        {
            ReleaseLocalCapture();
        }

        if (_captureDelegatedTo is Window delegated && delegated._capturedElement != null)
        {
            _captureDelegatedTo = null;
            delegated.ReleaseLocalCapture();
        }
    }

    /// <summary>Drops a closing window from capture ownership, so the next armed popup watch can take the capture.</summary>
    private void ForgetMouseCapture()
    {
        _armedWatches?.Remove(this);
        if (ReferenceEquals(_osCaptureHolder, this))
        {
            ReleaseLocalCapture();
        }
    }
}
