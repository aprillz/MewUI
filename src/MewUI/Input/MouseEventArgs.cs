using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI;

/// <summary>
/// Mouse button enumeration.
/// </summary>
public enum MouseButton
{
    /// <summary>Left button.</summary>
    Left,
    /// <summary>Right button.</summary>
    Right,
    /// <summary>Middle button (wheel).</summary>
    Middle,
    /// <summary>First extra button.</summary>
    XButton1,
    /// <summary>Second extra button.</summary>
    XButton2
}

/// <summary>
/// Arguments for mouse events.
/// </summary>
public class MouseEventArgs
{
    /// <summary>
    /// Gets the original element that was hit-tested for this event.
    /// This value remains stable while the event bubbles.
    /// </summary>
    internal UIElement? OriginalSource { get; set; }

    /// <summary>
    /// Gets the current element receiving the event as it bubbles.
    /// </summary>
    public UIElement? Source { get; internal set; }

    /// <summary>
    /// Gets the mouse position relative to the window (root) in DIPs.
    /// Not exposed publicly (WPF-style); use <see cref="GetPosition"/> to obtain coordinates.
    /// </summary>
    internal Point Position { get; }

    /// <summary>
    /// Gets the position of the mouse in screen coordinates in device pixels.
    /// </summary>
    public Point ScreenPosition { get; }

    /// <summary>
    /// Gets which mouse button was pressed/released.
    /// </summary>
    public MouseButton Button { get; }

    /// <summary>
    /// Gets whether the left button is currently pressed.
    /// </summary>
    public bool LeftButton { get; }

    /// <summary>
    /// Gets whether the right button is currently pressed.
    /// </summary>
    public bool RightButton { get; }

    /// <summary>
    /// Gets whether the middle button is currently pressed.
    /// </summary>
    public bool MiddleButton { get; }

    /// <summary>
    /// Gets or sets whether the event has been handled.
    /// </summary>
    public bool Handled { get; set; }

    /// <summary>
    /// Gets the click count (1 = single, 2 = double).
    /// </summary>
    public int ClickCount { get; }

    /// <summary>
    /// Gets the keyboard modifier keys held during this mouse event.
    /// </summary>
    public ModifierKeys Modifiers { get; }

    /// <summary>Gets whether the Control key was held.</summary>
    public bool ControlKey => (Modifiers & ModifierKeys.Control) != 0;

    /// <summary>Gets whether the Shift key was held.</summary>
    public bool ShiftKey => (Modifiers & ModifierKeys.Shift) != 0;

    /// <summary>Gets whether the Alt key was held.</summary>
    public bool AltKey => (Modifiers & ModifierKeys.Alt) != 0;

    /// <summary>Gets whether the Meta key (Cmd/Win/Super) was held.</summary>
    public bool MetaKey => (Modifiers & ModifierKeys.Meta) != 0;

    /// <summary>Gets whether the platform primary command modifier (Ctrl on Windows/Linux, Cmd on macOS) was held.</summary>
    public bool PrimaryKey => (Modifiers & PlatformConventions.Current.PrimaryModifier) != 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="MouseEventArgs"/> class.
    /// </summary>
    /// <param name="positionInWindow">Mouse position relative to the window (root) (DIPs).</param>
    /// <param name="screenPosition">Mouse position in screen coordinates in device pixels.</param>
    /// <param name="button">Button associated with the event.</param>
    /// <param name="leftButton">Whether the left button is pressed.</param>
    /// <param name="rightButton">Whether the right button is pressed.</param>
    /// <param name="middleButton">Whether the middle button is pressed.</param>
    /// <param name="clickCount">Click count (1 = single, 2 = double).</param>
    /// <param name="modifiers">Keyboard modifier keys held during the event.</param>
    public MouseEventArgs(Point positionInWindow, Point screenPosition, MouseButton button = MouseButton.Left,
        bool leftButton = false, bool rightButton = false, bool middleButton = false, int clickCount = 1,
        ModifierKeys modifiers = ModifierKeys.None)
    {
        Position = positionInWindow;
        ScreenPosition = screenPosition;
        Button = button;
        LeftButton = leftButton;
        RightButton = rightButton;
        MiddleButton = middleButton;
        ClickCount = clickCount;
        Modifiers = modifiers;
    }

    /// <summary>
    /// Gets the mouse position relative to the specified element (DIPs).
    /// Equivalent to WPF's <c>MouseEventArgs.GetPosition</c> behavior.
    /// </summary>
    public Point GetPosition(UIElement relativeTo)
    {
        ArgumentNullException.ThrowIfNull(relativeTo);

        var root = relativeTo.FindVisualRoot();
        if (root is not Window window || window.Handle == 0)
        {
            throw new InvalidOperationException("The visual is not connected to a window.");
        }

        return window.TranslatePoint(Position, relativeTo);
    }
}

/// <summary>
/// Arguments for mouse wheel events.
/// </summary>
public class MouseWheelEventArgs : MouseEventArgs
{
    /// <summary>
    /// Gets the wheel scroll delta in notches.
    /// <para>1.0 corresponds to one mouse-wheel notch. Trackpads and high-resolution
    /// devices may produce fractional values.</para>
    /// <para>Sign convention: <see cref="Vector.Y"/> positive = scroll-up intent
    /// (toward earlier content); <see cref="Vector.X"/> positive = scroll-left intent.</para>
    /// </summary>
    public Vector Delta { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MouseWheelEventArgs"/> class.
    /// </summary>
    /// <param name="position">Mouse position relative to the element (DIPs).</param>
    /// <param name="screenPosition">Mouse position in screen coordinates in device pixels.</param>
    /// <param name="delta">Wheel delta in notches. +Y = scroll up, +X = scroll left.
    /// Magnitude under 1.0 indicates a sub-notch (trackpad / high-res) input.</param>
    /// <param name="leftButton">Whether the left button is pressed.</param>
    /// <param name="rightButton">Whether the right button is pressed.</param>
    /// <param name="middleButton">Whether the middle button is pressed.</param>
    /// <param name="modifiers">Keyboard modifier keys held during the event.</param>
    public MouseWheelEventArgs(Point position, Point screenPosition, Vector delta,
        bool leftButton = false, bool rightButton = false, bool middleButton = false,
        ModifierKeys modifiers = ModifierKeys.None)
        : base(position, screenPosition, MouseButton.Middle, leftButton, rightButton, middleButton,
            clickCount: 1, modifiers: modifiers)
    {
        Delta = delta;
    }
}
