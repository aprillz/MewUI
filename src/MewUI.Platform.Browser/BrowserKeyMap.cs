using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Platform.Browser;

/// <summary>
/// Maps a DOM keyboard event to a MewUI <see cref="Key"/>. A letter the key types in the current layout names the key,
/// so shortcuts follow the layout as they do on the desktop platforms; every other key is named by its position
/// (<c>KeyboardEvent.code</c>), which a layout typing no Latin letter falls back to as well.
/// </summary>
internal static class BrowserKeyMap
{
    /// <param name="code"><c>KeyboardEvent.code</c>: the physical key.</param>
    /// <param name="key"><c>KeyboardEvent.key</c>: what the key types with the current layout and modifiers, when the page passes it.</param>
    internal static Key Map(string code, string? key = null)
    {
        if (string.IsNullOrEmpty(code))
        {
            return Key.None;
        }

        // The digit row and the keypad type the same digits, and a shifted digit row types punctuation, so position decides.
        bool isLetterPosition = code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal);
        if (key != null && key.Length == 1)
        {
            var typed = KeyCharacters.FromCharacter(key[0]);
            bool typedLetter = typed is >= Key.A and <= Key.Z;
            bool isDigitOrKeypad = code.StartsWith("Digit", StringComparison.Ordinal) || code.StartsWith("Numpad", StringComparison.Ordinal);

            // A letter names its key wherever the layout puts it; punctuation does only where it took a letter's position.
            if (!isDigitOrKeypad && (typedLetter || (isLetterPosition && typed != Key.None)))
            {
                return typed;
            }
        }

        if (isLetterPosition)
        {
            char letter = char.ToUpperInvariant(code[3]);
            return letter is >= 'A' and <= 'Z' ? Key.A + (letter - 'A') : Key.None;
        }

        if (code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal))
        {
            char digit = code[5];
            return digit is >= '0' and <= '9' ? Key.D0 + (digit - '0') : Key.None;
        }

        if (code.StartsWith("Numpad", StringComparison.Ordinal) && code.Length == 7)
        {
            char digit = code[6];
            return digit is >= '0' and <= '9' ? Key.NumPad0 + (digit - '0') : Key.None;
        }

        return code switch
        {
            "Backspace" => Key.Backspace,
            "Tab" => Key.Tab,
            "Enter" or "NumpadEnter" => Key.Enter,
            "Escape" => Key.Escape,
            "Space" => Key.Space,
            "ArrowLeft" => Key.Left,
            "ArrowUp" => Key.Up,
            "ArrowRight" => Key.Right,
            "ArrowDown" => Key.Down,
            "Insert" => Key.Insert,
            "Delete" => Key.Delete,
            "Home" => Key.Home,
            "End" => Key.End,
            "PageUp" => Key.PageUp,
            "PageDown" => Key.PageDown,
            "NumpadAdd" => Key.Add,
            "NumpadSubtract" => Key.Subtract,
            "NumpadMultiply" => Key.Multiply,
            "NumpadDivide" => Key.Divide,
            "NumpadDecimal" => Key.Decimal,
            "Backquote" => Key.Backquote,
            "Minus" => Key.Minus,
            "Equal" => Key.Equal,
            "BracketLeft" => Key.BracketLeft,
            "BracketRight" => Key.BracketRight,
            "Backslash" => Key.Backslash,
            "Semicolon" => Key.Semicolon,
            "Quote" => Key.Quote,
            "Comma" => Key.Comma,
            "Period" => Key.Period,
            "Slash" => Key.Slash,
            "IntlBackslash" => Key.IntlBackslash,
            "ContextMenu" => Key.ContextMenu,
            _ => MapFunctionKey(code),
        };
    }

    /// <summary>
    /// The layout texts of a DOM key event: <c>KeyboardEvent.key</c> when it is one character, and, since the browser
    /// reports no text without the Alt (Option) level, the US text of the key's position while Alt is down.
    /// </summary>
    internal static (string? KeySymbol, string? BaseKeySymbol) Symbols(string code, string? key, ModifierKeys modifiers)
    {
        // Named keys ("Enter", "Dead", "ArrowLeft") are words; a typed character is one code point.
        bool isCharacter = key != null && (key.Length == 1 || (key.Length == 2 && char.IsSurrogatePair(key[0], key[1])));
        string? keySymbol = isCharacter && !(key!.Length == 1 && (key[0] < 0x20 || key[0] == 0x7F)) ? key : null;

        string? baseKeySymbol = (modifiers & ModifierKeys.Alt) != 0
            ? UsText(code, (modifiers & ModifierKeys.Shift) != 0)
            : keySymbol;
        return (keySymbol, baseKeySymbol);
    }

    /// <summary>The text a key types at its position on a US layout, with or without Shift.</summary>
    internal static string? UsText(string code, bool shift)
    {
        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal) && code[3] is >= 'A' and <= 'Z')
        {
            return (shift ? code[3] : char.ToLowerInvariant(code[3])).ToString();
        }

        if (code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal) && code[5] is >= '0' and <= '9')
        {
            const string SHIFTED_DIGITS = ")!@#$%^&*(";
            return (shift ? SHIFTED_DIGITS[code[5] - '0'] : code[5]).ToString();
        }

        var (unshifted, shifted) = code switch
        {
            "Space" => (' ', ' '),
            "Backquote" => ('`', '~'),
            "Minus" => ('-', '_'),
            "Equal" => ('=', '+'),
            "BracketLeft" => ('[', '{'),
            "BracketRight" => (']', '}'),
            "Backslash" => ('\\', '|'),
            "Semicolon" => (';', ':'),
            "Quote" => ('\'', '"'),
            "Comma" => (',', '<'),
            "Period" => ('.', '>'),
            "Slash" => ('/', '?'),
            _ => ('\0', '\0'),
        };

        return unshifted == '\0' ? null : (shift ? shifted : unshifted).ToString();
    }

    private static Key MapFunctionKey(string code)
    {
        if (code.Length < 2 || code[0] != 'F' || !char.IsDigit(code[1]))
        {
            return Key.None;
        }

        return int.TryParse(code.AsSpan(1), out var number) && number is >= 1 and <= 24
            ? Key.F1 + (number - 1)
            : Key.None;
    }
}
