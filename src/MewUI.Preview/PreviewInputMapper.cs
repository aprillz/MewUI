using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Preview;

/// <summary>
/// Maps IDE-side input identifiers (W3C KeyboardEvent.code, W3C button numbers) to the
/// framework's input types.
/// </summary>
internal static class PreviewInputMapper
{
    internal static MouseButton MapButton(int w3cButton) => w3cButton switch
    {
        1 => MouseButton.Middle,
        2 => MouseButton.Right,
        _ => MouseButton.Left,
    };

    internal static ModifierKeys MapModifiers(int flags) => (ModifierKeys)flags;

    internal static Key MapKey(string code)
    {
        // Letter/digit/function blocks are contiguous in both conventions.
        if (code.Length == 4 && code.StartsWith("Key", StringComparison.Ordinal))
        {
            char letter = code[3];
            if (letter >= 'A' && letter <= 'Z')
            {
                return Key.A + (letter - 'A');
            }
        }
        if (code.Length == 6 && code.StartsWith("Digit", StringComparison.Ordinal))
        {
            char digit = code[5];
            if (digit >= '0' && digit <= '9')
            {
                return Key.D0 + (digit - '0');
            }
        }
        if (code.Length == 7 && code.StartsWith("Numpad", StringComparison.Ordinal))
        {
            char digit = code[6];
            if (digit >= '0' && digit <= '9')
            {
                return Key.NumPad0 + (digit - '0');
            }
        }
        if (code.Length >= 2 && code.Length <= 3 && code[0] == 'F' && int.TryParse(code.AsSpan(1), out int function)
            && function >= 1 && function <= 24)
        {
            return Key.F1 + (function - 1);
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
            _ => Key.None,
        };
    }
}
