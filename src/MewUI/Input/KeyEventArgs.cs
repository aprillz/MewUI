namespace Aprillz.MewUI;

/// <summary>
/// Modifier keys enumeration.
/// </summary>
[Flags]
public enum ModifierKeys
{
    /// <summary>No modifier keys.</summary>
    None = 0,
    /// <summary>Control key.</summary>
    Control = 1,
    /// <summary>Shift key.</summary>
    Shift = 2,
    /// <summary>Alt key.</summary>
    Alt = 4,
    /// <summary>Windows / Command key.</summary>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyEventArgs"/> class.
    /// </summary>
    /// <param name="key">Cross-platform key identifier.</param>
    /// <param name="platformKey">Platform-specific key code (e.g. Win32 virtual-key).</param>
    /// <param name="modifiers">Modifier keys pressed.</param>
    /// <param name="isRepeat">Whether this is an auto-repeat event.</param>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="TextInputEventArgs"/> class.
    /// </summary>
    /// <param name="text">Input text.</param>
    public TextInputEventArgs(string text)
    {
        Text = NormalizeText(text);
    }

    internal static string NormalizeText(string? text)
    {
        text ??= string.Empty;
        if (text.Length == 0)
        {
            return string.Empty;
        }

        // Normalize newlines to '\n' so controls get consistent input across platforms.
        // (Win32 typically emits '\r' via WM_CHAR, other platforms vary.)
        if (text.IndexOf('\r') >= 0)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        return text;
    }
}
