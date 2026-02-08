namespace Aprillz.MewUI.Input;

internal static class WindowInputRouter
{
    public static void UpdateMouseOver(Window window, ref UIElement? mouseOverElement, UIElement? newLeaf)
    {
        if (ReferenceEquals(mouseOverElement, newLeaf))
        {
            return;
        }

        window.UpdateMouseOverChain(mouseOverElement, newLeaf);
        mouseOverElement = newLeaf;
    }

    public static UIElement? HitTest(Window window, UIElement? capturedElement, Point positionInWindow)
        => capturedElement ?? window.HitTest(positionInWindow);

    public static void MouseMove(
        Window window,
        ref UIElement? mouseOverElement,
        UIElement? capturedElement,
        Point positionInWindow,
        Point screenPosition,
        bool leftDown,
        bool rightDown,
        bool middleDown)
    {
        window.UpdateLastMousePosition(positionInWindow, screenPosition);

        var element = HitTest(window, capturedElement, positionInWindow);
        UpdateMouseOver(window, ref mouseOverElement, element);

        var args = new MouseEventArgs(positionInWindow, screenPosition, global::Aprillz.MewUI.MouseButton.Left, leftDown, rightDown, middleDown);
        element?.RaiseMouseMove(args);
    }

    public static void MouseButton(
        Window window,
        ref UIElement? mouseOverElement,
        UIElement? capturedElement,
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

        var element = HitTest(window, capturedElement, positionInWindow);
        if (isDown)
        {
            window.OnAfterMouseDownHitTest(positionInWindow, button, element);
        }

        UpdateMouseOver(window, ref mouseOverElement, element);

        var args = new MouseEventArgs(positionInWindow, screenPosition, button, leftDown, rightDown, middleDown, clickCount: clickCount);
        if (isDown)
        {
            if (element?.Focusable == true)
            {
                window.FocusManager.SetFocus(element);
            }

            element?.RaiseMouseDown(args);

            if (clickCount == 2)
            {
                var dblArgs = new MouseEventArgs(positionInWindow, screenPosition, button, leftDown, rightDown, middleDown, clickCount: 2);
                element?.RaiseMouseDoubleClick(dblArgs);
            }
        }
        else
        {
            element?.RaiseMouseUp(args);
            window.RequerySuggested();
        }
    }

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
        var args = new MouseWheelEventArgs(positionInWindow, screenPosition, delta, isHorizontal, leftDown, rightDown, middleDown);

        for (var current = element; current != null && !args.Handled; current = current.Parent as UIElement)
        {
            current.RaiseMouseWheel(args);
        }
    }
}
