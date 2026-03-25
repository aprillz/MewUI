namespace Aprillz.MewUI;

/// <summary>
/// Binds a <see cref="KeyGesture"/> to an action. Used by <see cref="Window.KeyBindings"/>
/// for global keyboard shortcuts.
/// </summary>
public sealed class KeyBinding
{
    /// <summary>
    /// Gets the gesture that triggers this binding.
    /// </summary>
    public KeyGesture Gesture { get; }

    /// <summary>
    /// Gets the action to execute when the gesture matches.
    /// </summary>
    public Action Execute { get; }

    /// <summary>
    /// Gets or sets an optional predicate that determines whether the binding can execute.
    /// </summary>
    public Func<bool>? CanExecute { get; set; }

    public KeyBinding(KeyGesture gesture, Action execute)
    {
        Gesture = gesture;
        Execute = execute ?? throw new ArgumentNullException(nameof(execute));
    }

    /// <summary>
    /// Attempts to handle the key event. Returns true and marks the event as handled if the gesture matches.
    /// </summary>
    public bool TryHandle(KeyEventArgs e)
    {
        if (Gesture.Matches(e) && (CanExecute?.Invoke() ?? true))
        {
            Execute();
            e.Handled = true;
            return true;
        }

        return false;
    }
}
