using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI;

/// <summary>
/// Modifier keys that can be held during an input event.
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
    /// <summary>Meta key (Cmd on macOS, Win on Windows, Super on Linux).</summary>
    Meta = 8,

    /// <summary>
    /// Platform primary command modifier. Resolved to Control or Meta at matching/display time
    /// via <see cref="PlatformConventions.Current"/>.
    /// </summary>
    Primary = 16,
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
    /// Gets the code the platform gave the key. Its meaning differs from platform to platform; <see cref="Key"/> is
    /// the portable identity.
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
    /// Gets the text the key types in the current keyboard layout with Shift, Caps Lock and the layout's third level
    /// (AltGr, Option) applied, or null when it types nothing, starts a dead key, or the platform does not report it.
    /// </summary>
    /// <remarks>
    /// Control, Meta and Alt on its own do not change the value: Ctrl+A gives "a". Where the third level is reached with
    /// Ctrl and Alt together, that combination applies it. A key an input method takes for a composition may report no
    /// value.
    /// </remarks>
    public string? KeySymbol { get; init; }

    /// <summary>
    /// Gets the text the key types in the current keyboard layout with only Shift and Caps Lock applied, or null when it
    /// types nothing or the platform does not report it.
    /// </summary>
    /// <remarks>
    /// A dead key gives its spacing character. Where an input method types its own script over a keyboard layout, this
    /// is the layout's text. When <see cref="KeySymbol"/> is set and differs from this value, the key typed a third-level
    /// character.
    /// </remarks>
    public string? BaseKeySymbol { get; init; }

    /// <summary>
    /// Gets or sets whether the event has been handled.
    /// </summary>
    public bool Handled { get; set; }

    /// <summary>
    /// Initializes a new instance of the <see cref="KeyEventArgs"/> class.
    /// </summary>
    /// <param name="key">Cross-platform key identifier.</param>
    /// <param name="platformKey">The code the platform gave the key.</param>
    /// <param name="modifiers">Modifier keys pressed.</param>
    /// <param name="isRepeat">Whether this is an auto-repeat event.</param>
    public KeyEventArgs(Key key, int platformKey, ModifierKeys modifiers = ModifierKeys.None, bool isRepeat = false)
    {
        Key = key;
        PlatformKey = platformKey;
        Modifiers = modifiers;
        IsRepeat = isRepeat;
    }

    /// <summary>
    /// Creates arguments for a key the platform could not map, with <see cref="MewUI.Key.None"/>.
    /// </summary>
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

    /// <summary>
    /// Gets whether the Meta key is pressed (Cmd on macOS, Win on Windows, Super on Linux).
    /// </summary>
    public bool MetaKey => (Modifiers & ModifierKeys.Meta) != 0;

    /// <summary>
    /// Gets whether the platform's primary command modifier is pressed (Ctrl on Windows/Linux, Cmd on macOS).
    /// </summary>
    public bool PrimaryKey => (Modifiers & PlatformConventions.Current.PrimaryModifier) != 0;
}

/// <summary>
/// Arguments for text input events.
/// </summary>
public class TextInputEventArgs
{
    private string _text;

    /// <summary>
    /// Gets or sets the text being input; a subscriber may assign a replacement, which the control
    /// inserts in place of what arrived.
    /// </summary>
    /// <remarks>
    /// Newlines in an assigned replacement are normalized the same way as on construction.
    /// </remarks>
    public string Text
    {
        get => _text;
        set => _text = NormalizeText(value);
    }

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
        _text = NormalizeText(text);
    }

    internal static string NormalizeText(string? text)
    {
        text ??= string.Empty;
        if (text.Length == 0)
        {
            return string.Empty;
        }

        // Normalize newlines to '\n' so controls get consistent input across platforms.
        // (Some platforms report a carriage return for the Enter key, others a line feed.)
        if (text.IndexOf('\r') >= 0)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        return text;
    }
}
