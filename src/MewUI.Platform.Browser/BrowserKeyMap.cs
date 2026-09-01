using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Platform.Browser;

/// <summary>
/// Maps a DOM <c>KeyboardEvent.code</c> to a MewUI <see cref="Key"/>. The code names a physical
/// key, so the mapping stays correct under non-US layouts; the typed character arrives separately
/// as text input.
/// </summary>
internal static class BrowserKeyMap
{
    internal static Key Map(string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return Key.None;
        }

        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal))
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
            _ => MapFunctionKey(code),
        };
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
