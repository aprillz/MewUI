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
    }

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
        bool middleDown)
    {
        window.UpdateLastMousePosition(positionInWindow, screenPosition);

        var element = HitTest(window, positionInWindow);
        UpdateMouseOver(window, element);

        var args = new MouseEventArgs(positionInWindow, screenPosition, MewUI.MouseButton.Left, leftDown, rightDown, middleDown);
        for (var current = element; current != null && !args.Handled; current = GetInputBubbleParent(window, current))
        {
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
        int clickCount)
    {
        window.UpdateLastMousePosition(positionInWindow, screenPosition);

        if (isDown)
        {
            window.OnBeforeMouseDown(positionInWindow, button);
        }

        var element = HitTest(window, positionInWindow);
        if (isDown)
        {
            window.OnAfterMouseDownHitTest(positionInWindow, button, element);
        }

        UpdateMouseOver(window, element);

        var args = new MouseEventArgs(positionInWindow, screenPosition, button, leftDown, rightDown, middleDown, clickCount: clickCount)
        {
            OriginalSource = element,
            Source = element,
        };
        if (isDown)
        {
            if (element?.Focusable == true)
            {
                window.FocusManager.SetFocus(element);
            }

            for (var current = element; current != null && !args.Handled; current = GetInputBubbleParent(window, current))
            {
                args.Source = current;
                current.RaiseMouseDown(args);
            }

            if (clickCount == 2)
            {
                args = new MouseEventArgs(positionInWindow, screenPosition, button, leftDown, rightDown, middleDown, clickCount: 2)
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
    /// </summary>
    public static void MouseWheel(
        Window window,
        Point positionInWindow,
        Point screenPosition,
        int delta,
        bool isHorizontal,
        bool leftDown = false,
        bool rightDown = false,
        bool middleDown = false)
    {
        window.UpdateLastMousePosition(positionInWindow, screenPosition);

        var element = window.HitTest(positionInWindow);
        var args = new MouseWheelEventArgs(positionInWindow, screenPosition, delta, isHorizontal, leftDown, rightDown, middleDown)
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
        for (var current = window.FocusManager.FocusedElement; current != null && !args.Handled; current = GetInputBubbleParent(window, current))
        {
            current.RaiseKeyDown(args);
        }
    }

    /// <summary>
    /// Routes a KeyUp event by bubbling from the focused element to the root until handled.
    /// </summary>
    public static void KeyUp(Window window, KeyEventArgs args)
    {
        for (var current = window.FocusManager.FocusedElement; current != null && !args.Handled; current = GetInputBubbleParent(window, current))
        {
            current.RaiseKeyUp(args);
        }
    }
}
