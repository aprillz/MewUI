using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Platform.MacOS;

/// <summary>Turns a <see cref="KeyGesture"/> into an <c>NSMenuItem</c> key equivalent and modifier mask.</summary>
internal static class MacOSKeyEquivalents
{
    private const ulong SHIFT_MASK = 1UL << 17;
    private const ulong CONTROL_MASK = 1UL << 18;
    private const ulong OPTION_MASK = 1UL << 19;
    private const ulong COMMAND_MASK = 1UL << 20;
    private const char UP_ARROW = '';
    private const char DOWN_ARROW = '';
    private const char LEFT_ARROW = '';
    private const char RIGHT_ARROW = '';
    private const char F1 = '';
    private const char FORWARD_DELETE = '';
    private const char HOME = '';
    private const char END = '';
    private const char PAGE_UP = '';
    private const char PAGE_DOWN = '';

    /// <summary>False for a key a menu item cannot show.</summary>
    public static bool TryGet(KeyGesture gesture, out string keyEquivalent, out ulong modifierMask)
    {
        modifierMask = 0;
        keyEquivalent = string.Empty;
        if (!TryGetCharacter(gesture.Key, out char character))
        {
            return false;
        }

        // A lowercase letter with the Shift bit, not an uppercase one: AppKit reads an uppercase letter as Shift by itself.
        keyEquivalent = character.ToString();
        var modifiers = gesture.Modifiers;
        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            modifierMask |= SHIFT_MASK;
        }
        if ((modifiers & ModifierKeys.Control) != 0)
        {
            modifierMask |= CONTROL_MASK;
        }
        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            modifierMask |= OPTION_MASK;
        }
        if ((modifiers & ModifierKeys.Meta) != 0)
        {
            modifierMask |= COMMAND_MASK;
        }

        return true;
    }

    private static bool TryGetCharacter(Key key, out char character)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            character = (char)('a' + (key - Key.A));
            return true;
        }

        if (key >= Key.D0 && key <= Key.D9)
        {
            character = (char)('0' + (key - Key.D0));
            return true;
        }

        if (key >= Key.F1 && key <= Key.F24)
        {
            character = (char)(F1 + (key - Key.F1));
            return true;
        }

        character = key switch
        {
            Key.Enter => '\r',
            Key.Escape => '\u001B',
            Key.Tab => '\t',
            Key.Space => ' ',
            Key.Backspace => '\b',
            Key.Delete => FORWARD_DELETE,
            Key.Up => UP_ARROW,
            Key.Down => DOWN_ARROW,
            Key.Left => LEFT_ARROW,
            Key.Right => RIGHT_ARROW,
            Key.Home => HOME,
            Key.End => END,
            Key.PageUp => PAGE_UP,
            Key.PageDown => PAGE_DOWN,
            _ => KeyCharacters.TryGetPunctuation(key, out char punctuation) ? punctuation : '\0',
        };
        return character != '\0';
    }
}
