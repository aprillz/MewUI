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
    /// Gets the position of the mouse relative to the element.
    /// </summary>
    public Point Position { get; }

    /// <summary>
    /// Gets the position of the mouse in screen coordinates.
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
    /// Initializes a new instance of the <see cref="MouseEventArgs"/> class.
    /// </summary>
    /// <param name="position">Mouse position relative to the element (DIPs).</param>
    /// <param name="screenPosition">Mouse position in screen coordinates.</param>
    /// <param name="button">Button associated with the event.</param>
    /// <param name="leftButton">Whether the left button is pressed.</param>
    /// <param name="rightButton">Whether the right button is pressed.</param>
    /// <param name="middleButton">Whether the middle button is pressed.</param>
    /// <param name="clickCount">Click count (1 = single, 2 = double).</param>
    public MouseEventArgs(Point position, Point screenPosition, MouseButton button = MouseButton.Left,
        bool leftButton = false, bool rightButton = false, bool middleButton = false, int clickCount = 1)
    {
        Position = position;
        ScreenPosition = screenPosition;
        Button = button;
        LeftButton = leftButton;
        RightButton = rightButton;
        MiddleButton = middleButton;
        ClickCount = clickCount;
    }
}

/// <summary>
/// Arguments for mouse wheel events.
/// </summary>
public class MouseWheelEventArgs : MouseEventArgs
{
    /// <summary>
    /// Gets the wheel delta (positive = up, negative = down).
    /// </summary>
    public int Delta { get; }

    /// <summary>
    /// Gets whether this is a horizontal scroll event.
    /// </summary>
    public bool IsHorizontal { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MouseWheelEventArgs"/> class.
    /// </summary>
    /// <param name="position">Mouse position relative to the element (DIPs).</param>
    /// <param name="screenPosition">Mouse position in screen coordinates.</param>
    /// <param name="delta">Wheel delta (positive = up, negative = down).</param>
    /// <param name="isHorizontal"><see langword="true"/> for horizontal scroll; otherwise, <see langword="false"/>.</param>
    /// <param name="leftButton">Whether the left button is pressed.</param>
    /// <param name="rightButton">Whether the right button is pressed.</param>
    /// <param name="middleButton">Whether the middle button is pressed.</param>
    public MouseWheelEventArgs(Point position, Point screenPosition, int delta, bool isHorizontal = false,
        bool leftButton = false, bool rightButton = false, bool middleButton = false)
        : base(position, screenPosition, MouseButton.Middle, leftButton, rightButton, middleButton)
    {
        Delta = delta;
        IsHorizontal = isHorizontal;
    }
}
