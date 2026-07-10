using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;

namespace MewUI.Test.Infrastructure;

/// <summary>
/// Injects input into a headless window through <see cref="WindowInputRouter"/>, the same
/// entry points the platform backends use, so routing (hit test, mouse-over, focus, popup
/// close policy, bubbling) matches production behavior. Run PerformLayout first so hit
/// testing sees arranged bounds.
/// </summary>
internal static class HeadlessInput
{
    public static void SendMouseMove(this Window window, Point position)
        => WindowInputRouter.MouseMove(window, position, position,
            leftDown: false, rightDown: false, middleDown: false);

    public static void SendMouseDown(this Window window, Point position, MouseButton button = MouseButton.Left,
        ModifierKeys modifiers = ModifierKeys.None)
        => WindowInputRouter.MouseButton(window, position, position, button, isDown: true,
            leftDown: button == MouseButton.Left,
            rightDown: button == MouseButton.Right,
            middleDown: button == MouseButton.Middle,
            clickCount: 1,
            modifiers: modifiers);

    public static void SendMouseUp(this Window window, Point position, MouseButton button = MouseButton.Left,
        ModifierKeys modifiers = ModifierKeys.None)
        => WindowInputRouter.MouseButton(window, position, position, button, isDown: false,
            leftDown: false, rightDown: false, middleDown: false,
            clickCount: 1,
            modifiers: modifiers);

    public static void SendClick(this Window window, Point position, MouseButton button = MouseButton.Left,
        ModifierKeys modifiers = ModifierKeys.None)
    {
        window.SendMouseMove(position);
        window.SendMouseDown(position, button, modifiers);
        window.SendMouseUp(position, button, modifiers);
    }

    public static void SendMouseWheel(this Window window, Point position, double deltaY)
        => WindowInputRouter.MouseWheel(window, position, position, new Vector(0, deltaY));

    public static void SendKeyDown(this Window window, Key key, ModifierKeys modifiers = ModifierKeys.None)
        => WindowInputRouter.KeyDown(window, new KeyEventArgs(key, platformKey: 0, modifiers));

    public static void SendKeyUp(this Window window, Key key, ModifierKeys modifiers = ModifierKeys.None)
        => WindowInputRouter.KeyUp(window, new KeyEventArgs(key, platformKey: 0, modifiers));

    public static void SendKeyPress(this Window window, Key key, ModifierKeys modifiers = ModifierKeys.None)
    {
        window.SendKeyDown(key, modifiers);
        window.SendKeyUp(key, modifiers);
    }

    /// <summary>
    /// Center of the element's arranged bounds in window coordinates.
    /// </summary>
    public static Point CenterOf(this UIElement element)
    {
        var bounds = element.Bounds;
        return new Point(bounds.X + bounds.Width / 2, bounds.Y + bounds.Height / 2);
    }
}
