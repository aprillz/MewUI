using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Input;

/// <summary>
/// Routes window-level raw input events to the appropriate UI elements (hit testing, mouse-over updates, bubbling).
/// </summary>
internal static class WindowInputRouter
{
    internal static UIElement? GetInputBubbleParent(Window window, UIElement current)
    {
        // Bubble across popup roots back to their owners.
        // This keeps input semantics closer to WPF: a click/wheel inside a popup can be handled by the owning control.
        if (window.TryGetPopupOwner(current, out var owner) && !ReferenceEquals(owner, current))
        {
            return owner;
        }

        return current.Parent as UIElement;
    }

    /// <summary>
    /// Updates the current mouse-over element and the window's mouse-over chain.
    /// </summary>
    public static void UpdateMouseOver(Window window, UIElement? newLeaf)
    {
        if (ReferenceEquals(window.MouseOverElement, newLeaf))
        {
            return;
        }

        window.UpdateMouseOverChain(window.MouseOverElement, newLeaf);
        window.SetMouseOverElement(newLeaf);
        window.UpdateCursorForElement(newLeaf);
    }

    // Delivers events to the captured element regardless of pointer position. Callers that need
    // the true element under the pointer (mouse-over state, focus/popup-close policy) should call
    // window.HitTest directly instead, since capture must not affect those decisions.
    public static UIElement? HitTest(Window window, Point positionInWindow)
        => window.CapturedElement ?? window.HitTest(positionInWindow);

    /// <summary>
    /// Routes a mouse move to the current hit-tested element and updates mouse-over state.
    /// </summary>
    public static void MouseMove(
        Window window,
        Point positionInWindow,
        Point screenPosition,
        bool leftDown,
        bool rightDown,
        bool middleDown,
        ModifierKeys modifiers = ModifierKeys.None)
    {
        // Popup surfaces arrange their hosted subtree in the owner's coordinate space; map the
        // surface-local position into that space once so hit tests and event positions agree
        // with element bounds everywhere downstream.
        positionInWindow = window.SurfacePointToVisualTree(positionInWindow);
        window.UpdateLastMousePosition(positionInWindow, screenPosition);

        // A drag in progress (or a gesture about to be promoted) consumes the move; skip normal routing.
        if (WindowDragDropRouter.OnMouseMove(window, positionInWindow, screenPosition))
        {
            return;
        }

        // Mouse-over must reflect the real element under the pointer, not the capture target,
        // so IsMouseOver stops tracking a captured element once the pointer leaves it.
        var actualHit = window.HitTest(positionInWindow);
        var element = window.CapturedElement ?? actualHit;
        UpdateMouseOver(window, actualHit);

        var args = new MouseEventArgs(positionInWindow, screenPosition, MewUI.MouseButton.Left, leftDown, rightDown, middleDown, modifiers: modifiers)
        {
            OriginalSource = element,
            Source = element,
        };
        for (var current = element; current != null && !args.Handled; current = GetInputBubbleParent(window, current))
        {
            args.Source = current;
            current.RaiseMouseMove(args);
        }
    }

    /// <summary>
    /// Routes a mouse button press/release to the current hit-tested element and manages focus/mouse-over state.
    /// </summary>
    public static void MouseButton(
        Window window,
        Point positionInWindow,
        Point screenPosition,
        MouseButton button,
        bool isDown,
        bool leftDown,
        bool rightDown,
        bool middleDown,
        int clickCount,
        ModifierKeys modifiers = ModifierKeys.None)
    {
        positionInWindow = window.SurfacePointToVisualTree(positionInWindow);
        window.UpdateLastMousePosition(positionInWindow, screenPosition);

        // Focus and popup-close policy must react to the real element under the pointer, not the
        // capture target, so clicking elsewhere while capture is active does not re-focus/keep-open
        // whatever currently holds capture.
        var actualHit = window.HitTest(positionInWindow);
        var element = window.CapturedElement ?? actualHit;
        if (isDown)
        {
            window.AccessKeyManager.OnPointerDown();
            window.OnAfterMouseDownHitTest(positionInWindow, button, actualHit);

            // Record a drag candidate before routing - gesture promotion happens on later MouseMove.
            if (button == MewUI.MouseButton.Left)
            {
                WindowDragDropRouter.OnMouseDown(window, positionInWindow, screenPosition, element);
            }
        }
        else if (button == MewUI.MouseButton.Left)
        {
            // Mouse-up on left button completes any active drag session; if so, skip normal routing.
            if (WindowDragDropRouter.OnMouseUp(window, positionInWindow, screenPosition))
            {
                return;
            }
        }

        UpdateMouseOver(window, actualHit);

        var args = new MouseEventArgs(positionInWindow, screenPosition, button, leftDown, rightDown, middleDown, clickCount: clickCount, modifiers: modifiers)
        {
            OriginalSource = element,
            Source = element,
        };
        if (isDown)
        {
            // Close transient popups before routing the click.
            // This prevents a popup opened by the click (e.g. ContextMenu) from being immediately closed
            // by the post-bubbling close policy pass.
            window.RequestClosePopups(PopupCloseRequest.PointerDown(actualHit));

            if (actualHit?.Focusable == true)
            {
                // Mouse click expresses a precise location; skip GetDefaultFocusTarget redirection
                // so focus lands where the user clicked, not on some off-screen "default" child.
                window.FocusManager.SetFocus(actualHit, resolveDefault: false);
            }
            else
            {
                // WPF-like: focus the nearest focusable ancestor instead of requiring the leaf hit-test target
                // to be focusable (e.g. clicking a Label inside a focusable control should focus the control).
                UIElement? focusTarget = null;
                for (var current = actualHit; current != null; current = GetInputBubbleParent(window, current))
                {
                    if (current.Focusable)
                    {
                        focusTarget = current;
                        break;
                    }
                }

                if (focusTarget == null)
                {
                    if (button is MewUI.MouseButton.Left)
                    {
                        window.FocusManager.ClearFocus();
                    }
                }
                else
                {
                    window.FocusManager.SetFocus(focusTarget, resolveDefault: false);
                }
            }

            for (var current = element; current != null && !args.Handled; current = GetInputBubbleParent(window, current))
            {
                args.Source = current;
                current.RaiseMouseDown(args);
            }

            if (clickCount == 2)
            {
                args = new MouseEventArgs(positionInWindow, screenPosition, button, leftDown, rightDown, middleDown, clickCount: 2, modifiers: modifiers)
                {
                    OriginalSource = element,
                    Source = element,
                };
                for (var current = element; current != null && !args.Handled; current = GetInputBubbleParent(window, current))
                {
                    args.Source = current;
                    current.RaiseMouseDoubleClick(args);
                }
            }
        }
        else
        {
            for (var current = element; current != null && !args.Handled; current = GetInputBubbleParent(window, current))
            {
                args.Source = current;
                current.RaiseMouseUp(args);
            }
            window.RequerySuggested();
        }
    }

    /// <summary>
    /// Routes a mouse wheel event by bubbling from the hit-tested element to the root until handled.
    /// <para><paramref name="delta"/> is in notches. +Y = scroll up, +X = scroll left.
    /// Fractional values represent sub-notch (trackpad / high-res) input.</para>
    /// </summary>
    public static void MouseWheel(
        Window window,
        Point positionInWindow,
        Point screenPosition,
        Vector delta,
        bool leftDown = false,
        bool rightDown = false,
        bool middleDown = false,
        ModifierKeys modifiers = ModifierKeys.None)
    {
        positionInWindow = window.SurfacePointToVisualTree(positionInWindow);
        window.UpdateLastMousePosition(positionInWindow, screenPosition);

        var element = window.HitTest(positionInWindow);
        var args = new MouseWheelEventArgs(positionInWindow, screenPosition, delta, leftDown, rightDown, middleDown, modifiers)
        {
            OriginalSource = element,
            Source = element,
        };

        for (var current = element; current != null && !args.Handled; current = GetInputBubbleParent(window, current))
        {
            args.Source = current;
            current.RaiseMouseWheel(args);
        }
    }

    /// <summary>
    /// Routes a KeyDown event by bubbling from the focused element to the root until handled.
    /// </summary>
    public static void KeyDown(Window window, KeyEventArgs args)
    {
        // Esc cancels an active drag before any normal key routing.
        if (WindowDragDropRouter.IsActive && args.Key == Key.Escape)
        {
            WindowDragDropRouter.CancelActive();
            args.Handled = true;
            return;
        }

        for (var current = ResolveKeyRoutingStart(window); current != null && !args.Handled; current = GetInputBubbleParent(window, current))
        {
            current.RaiseKeyDown(args);
        }
    }

    /// <summary>
    /// Routes a KeyUp event by bubbling from the focused element to the root until handled.
    /// </summary>
    public static void KeyUp(Window window, KeyEventArgs args)
    {
        for (var current = ResolveKeyRoutingStart(window); current != null && !args.Handled; current = GetInputBubbleParent(window, current))
        {
            current.RaiseKeyUp(args);
        }
    }

    private static UIElement ResolveKeyRoutingStart(Window window)
    {
        // A detached (stale) focused element counts as unfocused but is not cleared here:
        // tab navigation anchors off it and virtualization detaches containers routinely.
        var focused = window.FocusManager.FocusedElement;
        if (focused != null && !ReferenceEquals(focused.FindVisualRoot(), window))
        {
            focused = null;
        }

        return focused ?? window;
    }
}
