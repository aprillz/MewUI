using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Headless;

/// <summary>
/// Sends input to a window of a headless session along the path platform backends use, so hit
/// testing, hover, focus, popup closing and bubbling behave as in a running application; each
/// method runs the queued work afterwards.
/// </summary>
/// <remarks>
/// Points are window client coordinates in DIPs. A call from a thread other than the session's UI
/// thread is forwarded to it and returns when the input has been handled.
/// </remarks>
public static class HeadlessWindowExtensions
{
    /// <summary>Moves the mouse to <paramref name="position"/> with no button held.</summary>
    public static void SendMouseMove(this Window window, Point position, ModifierKeys modifiers = ModifierKeys.None)
        => Input(window, () => WindowInputRouter.MouseMove(window, position, position,
            leftDown: false, rightDown: false, middleDown: false, modifiers));

    /// <summary>Presses <paramref name="button"/> at <paramref name="position"/>.</summary>
    public static void SendMouseDown(this Window window, Point position, MouseButton button = MouseButton.Left, ModifierKeys modifiers = ModifierKeys.None)
        => Input(window, () => ButtonCore(window, position, button, isDown: true, clickCount: 1, modifiers));

    /// <summary>Releases <paramref name="button"/> at <paramref name="position"/>.</summary>
    public static void SendMouseUp(this Window window, Point position, MouseButton button = MouseButton.Left, ModifierKeys modifiers = ModifierKeys.None)
        => Input(window, () => ButtonCore(window, position, button, isDown: false, clickCount: 1, modifiers));

    /// <summary>Moves to <paramref name="position"/>, then presses and releases <paramref name="button"/> once.</summary>
    public static void SendClick(this Window window, Point position, MouseButton button = MouseButton.Left, ModifierKeys modifiers = ModifierKeys.None)
        => Input(window, () => ClickCore(window, position, button, modifiers));

    /// <summary>Clicks at <paramref name="position"/>, then presses and releases again with a click count of two.</summary>
    public static void SendDoubleClick(this Window window, Point position, MouseButton button = MouseButton.Left, ModifierKeys modifiers = ModifierKeys.None)
        => Input(window, () =>
        {
            ClickCore(window, position, button, modifiers);
            ButtonCore(window, position, button, isDown: true, clickCount: 2, modifiers);
            ButtonCore(window, position, button, isDown: false, clickCount: 2, modifiers);
        });

    /// <summary>Turns the wheel at <paramref name="position"/>; <paramref name="delta"/> is in notches, positive Y scrolls up.</summary>
    public static void SendMouseWheel(this Window window, Point position, Vector delta, ModifierKeys modifiers = ModifierKeys.None)
        => Input(window, () => WindowInputRouter.MouseWheel(window, position, position, delta, modifiers: modifiers));

    /// <summary>Turns the vertical wheel at <paramref name="position"/>; <paramref name="deltaY"/> is in notches, positive scrolls up.</summary>
    public static void SendMouseWheel(this Window window, Point position, double deltaY, ModifierKeys modifiers = ModifierKeys.None)
        => window.SendMouseWheel(position, new Vector(0, deltaY), modifiers);

    /// <summary>Presses <paramref name="key"/>: preview, bubbling from the focused element, access keys, then Tab navigation when still unhandled.</summary>
    public static void SendKeyDown(this Window window, Key key, ModifierKeys modifiers = ModifierKeys.None)
        => Input(window, () => KeyDownCore(window, key, modifiers));

    /// <summary>Releases <paramref name="key"/>: preview, bubbling from the focused element, then access keys.</summary>
    public static void SendKeyUp(this Window window, Key key, ModifierKeys modifiers = ModifierKeys.None)
        => Input(window, () => KeyUpCore(window, key, modifiers));

    /// <summary>Presses and releases <paramref name="key"/>.</summary>
    public static void SendKeyPress(this Window window, Key key, ModifierKeys modifiers = ModifierKeys.None)
        => Input(window, () =>
        {
            KeyDownCore(window, key, modifiers);
            KeyUpCore(window, key, modifiers);
        });

    /// <summary>Delivers <paramref name="text"/> to the focused text input, as typing does after the key events.</summary>
    public static void SendTextInput(this Window window, string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Input(window, () =>
        {
            if (text.Length > 0)
            {
                WindowInputRouter.TextInput(window, new TextInputEventArgs(text));
            }
        });
    }

    /// <summary>
    /// Drops <paramref name="data"/> at <paramref name="position"/> as an external drag would: enter, one
    /// move over the point, then the drop; returns the effect the target chose.
    /// </summary>
    public static DragDropEffects SendDragDrop(this Window window, Point position, IDataObject data, DragDropEffects allowedEffects = DragDropEffects.Copy)
    {
        ArgumentNullException.ThrowIfNull(data);
        return Input(window, () =>
        {
            WindowDragDropRouter.OnExternalDragEnter(window, new DragEventArgs(data, position, position, allowedEffects));
            WindowDragDropRouter.OnExternalDragOver(window, new DragEventArgs(data, position, position, allowedEffects));
            return WindowDragDropRouter.OnExternalDrop(window, new DragEventArgs(data, position, position, allowedEffects));
        });
    }

    /// <summary>
    /// Returns the pixels the window was last drawn with; throws <see cref="InvalidOperationException"/>
    /// unless the session renders <see cref="HeadlessRendering.Offscreen"/>.
    /// </summary>
    public static HeadlessFrame CaptureFrame(this Window window)
        => Input(window, () => RequireBackend(window).CaptureFrame());

    /// <summary>
    /// Draws the window straight from its visuals, bypassing the retained scene, so a test can compare
    /// <see cref="CaptureFrame"/> against it; render mode only.
    /// </summary>
    internal static HeadlessFrame CaptureReferenceFrame(this Window window)
        => Input(window, () => RequireBackend(window).CaptureReferenceFrame());

    /// <summary>Returns the center of the element's arranged bounds in window coordinates.</summary>
    public static Point CenterInWindow(this UIElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        var bounds = element.Bounds;
        return new Point(bounds.X + (bounds.Width / 2), bounds.Y + (bounds.Height / 2));
    }

    private static void KeyDownCore(Window window, Key key, ModifierKeys modifiers)
    {
        // The order every platform backend raises a key press in.
        var args = new KeyEventArgs(key, platformKey: 0, modifiers);
        window.RaisePreviewKeyDown(args);
        if (args.Handled)
        {
            return;
        }

        WindowInputRouter.KeyDown(window, args);
        window.ProcessAccessKeyDown(args);
        if (!args.Handled && key == Key.Tab)
        {
            if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                window.FocusManager.MoveFocusPrevious();
            }
            else
            {
                window.FocusManager.MoveFocusNext();
            }
        }
    }

    private static void KeyUpCore(Window window, Key key, ModifierKeys modifiers)
    {
        var args = new KeyEventArgs(key, platformKey: 0, modifiers);
        window.RaisePreviewKeyUp(args);
        if (!args.Handled)
        {
            WindowInputRouter.KeyUp(window, args);
        }

        window.ProcessAccessKeyUp(args);
        window.RequerySuggested();
    }

    private static void ClickCore(Window window, Point position, MouseButton button, ModifierKeys modifiers)
    {
        WindowInputRouter.MouseMove(window, position, position, leftDown: false, rightDown: false, middleDown: false, modifiers);
        ButtonCore(window, position, button, isDown: true, clickCount: 1, modifiers);
        ButtonCore(window, position, button, isDown: false, clickCount: 1, modifiers);
    }

    private static void ButtonCore(Window window, Point position, MouseButton button, bool isDown, int clickCount, ModifierKeys modifiers)
        => WindowInputRouter.MouseButton(window, position, position, button, isDown,
            leftDown: isDown && button == MouseButton.Left,
            rightDown: isDown && button == MouseButton.Right,
            middleDown: isDown && button == MouseButton.Middle,
            clickCount, modifiers);

    private static void Input(Window window, Action action)
        => Input(window, () =>
        {
            action();
            return true;
        });

    private static T Input<T>(Window window, Func<T> action)
    {
        ArgumentNullException.ThrowIfNull(window);
        var session = HeadlessSession.Current ?? throw new InvalidOperationException("No headless session is running.");
        return session.Run(() =>
        {
            T result = action();
            session.RunJobs();
            return result;
        });
    }

    private static HeadlessWindowBackend RequireBackend(Window window)
    {
        if (window.Backend is HeadlessWindowBackend backend)
        {
            return backend;
        }

        throw new InvalidOperationException("The window is not shown in the headless session.");
    }
}
