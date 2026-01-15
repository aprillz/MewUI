namespace Aprillz.MewUI;

/// <summary>
/// Modifier keys enumeration.
/// </summary>
[Flags]
public enum ModifierKeys
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Windows = 8
}

/// <summary>
/// Arguments for keyboard events.
/// </summary>
public class KeyEventArgs
{
    /// <summary>
    /// Gets the platform-independent key.
    /// </summary>
    public Key Key { get; }

    /// <summary>
    /// Gets the platform-specific key code (e.g. Win32 virtual-key).
    /// </summary>
    public int PlatformKey { get; }

    /// <summary>
    /// Gets the modifier keys that were pressed.
    /// </summary>
    public ModifierKeys Modifiers { get; }

    /// <summary>
    /// Gets whether this is a repeated key press.
    /// </summary>
    public bool IsRepeat { get; }

    /// <summary>
    /// Gets or sets whether the event has been handled.
    /// </summary>
    public bool Handled { get; set; }

    public KeyEventArgs(Key key, int platformKey, ModifierKeys modifiers = ModifierKeys.None, bool isRepeat = false)
    {
        Key = key;
        PlatformKey = platformKey;
        Modifiers = modifiers;
        IsRepeat = isRepeat;
    }

    public KeyEventArgs(int platformKey, ModifierKeys modifiers = ModifierKeys.None, bool isRepeat = false)
        : this(Key.None, platformKey, modifiers, isRepeat)
    {
    }

    /// <summary>
    /// Gets whether the Control key is pressed.
    /// </summary>
    public bool ControlKey => (Modifiers & ModifierKeys.Control) != 0;

    /// <summary>
    /// Gets whether the Shift key is pressed.
    /// </summary>
    public bool ShiftKey => (Modifiers & ModifierKeys.Shift) != 0;

    /// <summary>
    /// Gets whether the Alt key is pressed.
    /// </summary>
    public bool AltKey => (Modifiers & ModifierKeys.Alt) != 0;
}

/// <summary>
/// Arguments for text input events.
/// </summary>
public class TextInputEventArgs
{
    /// <summary>
    /// Gets the text that was input.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets or sets whether the event has been handled.
    /// </summary>
    public bool Handled { get; set; }

    public TextInputEventArgs(string text)
    {
        Text = text;
    }
}
