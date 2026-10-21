using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Platform.MacOS;

/// <summary>
/// Turns a macOS key press into a <see cref="Key"/>: the key the typed Latin letter names, or the key a US keyboard has
/// at that position.
/// </summary>
internal static class MacOSKeyMap
{
    /// <param name="keyCode">The virtual key code, which is the key's position.</param>
    /// <param name="characters">What the key types in the current layout with every modifier but Shift ignored.</param>
    public static Key Resolve(int keyCode, string? characters)
    {
        var position = FromKeyCode(keyCode);
        bool isLetterPosition = position is >= Key.A and <= Key.Z;
        if (position != Key.None && !isLetterPosition && !KeyCharacters.TryGetPunctuation(position, out _))
        {
            return position;
        }

        // A Latin letter names its key wherever the layout puts it; punctuation does only where it took a letter's
        // position. A layout typing neither there (or a dead key) leaves the key named by its position.
        var typed = characters != null && characters.Length == 1 ? KeyCharacters.FromCharacter(characters[0]) : Key.None;
        if (typed is >= Key.A and <= Key.Z || (isLetterPosition && typed != Key.None) || position == Key.None)
        {
            return typed;
        }

        return position;
    }

    /// <summary>The key a US keyboard has at the position of a virtual key code.</summary>
    public static Key FromKeyCode(int keyCode)
    {
        return keyCode switch
        {
            // Letters (kVK_ANSI_*)
            0x00 => Key.A,
            0x01 => Key.S,
            0x02 => Key.D,
            0x03 => Key.F,
            0x04 => Key.H,
            0x05 => Key.G,
            0x06 => Key.Z,
            0x07 => Key.X,
            0x08 => Key.C,
            0x09 => Key.V,
            0x0B => Key.B,
            0x0C => Key.Q,
            0x0D => Key.W,
            0x0E => Key.E,
            0x0F => Key.R,
            0x10 => Key.Y,
            0x11 => Key.T,
            0x12 => Key.D1,
            0x13 => Key.D2,
            0x14 => Key.D3,
            0x15 => Key.D4,
            0x16 => Key.D6,
            0x17 => Key.D5,
            0x19 => Key.D9,
            0x1A => Key.D7,
            0x1C => Key.D8,
            0x1D => Key.D0,
            0x1F => Key.O,
            0x20 => Key.U,
            0x22 => Key.I,
            0x23 => Key.P,
            0x25 => Key.L,
            0x26 => Key.J,
            0x28 => Key.K,
            0x2D => Key.N,
            0x2E => Key.M,

            // Punctuation (kVK_ANSI_*), the ISO extra key and the menu key
            0x32 => Key.Backquote,
            0x1B => Key.Minus,
            0x18 => Key.Equal,
            0x21 => Key.BracketLeft,
            0x1E => Key.BracketRight,
            0x2A => Key.Backslash,
            0x29 => Key.Semicolon,
            0x27 => Key.Quote,
            0x2B => Key.Comma,
            0x2F => Key.Period,
            0x2C => Key.Slash,
            0x0A => Key.IntlBackslash,
            0x6E => Key.ContextMenu,

            0x24 => Key.Enter,      // kVK_Return
            0x30 => Key.Tab,        // kVK_Tab
            0x31 => Key.Space,      // kVK_Space
            0x33 => Key.Backspace,  // kVK_Delete (backspace)
            0x35 => Key.Escape,     // kVK_Escape
            0x75 => Key.Delete,     // kVK_ForwardDelete

            0x7B => Key.Left,
            0x7C => Key.Right,
            0x7D => Key.Down,
            0x7E => Key.Up,

            0x73 => Key.Home,
            0x77 => Key.End,
            0x74 => Key.PageUp,
            0x79 => Key.PageDown,

            // Function keys (kVK_F1..kVK_F12). Apple key codes are non-contiguous.
            0x7A => Key.F1,
            0x78 => Key.F2,
            0x63 => Key.F3,
            0x76 => Key.F4,
            0x60 => Key.F5,
            0x61 => Key.F6,
            0x62 => Key.F7,
            0x64 => Key.F8,
            0x65 => Key.F9,
            0x6D => Key.F10,
            0x67 => Key.F11,
            0x6F => Key.F12,

            // Keypad (kVK_ANSI_Keypad*); the Mac keypad has no Num Lock, its digits always type digits.
            0x52 => Key.NumPad0,
            0x53 => Key.NumPad1,
            0x54 => Key.NumPad2,
            0x55 => Key.NumPad3,
            0x56 => Key.NumPad4,
            0x57 => Key.NumPad5,
            0x58 => Key.NumPad6,
            0x59 => Key.NumPad7,
            0x5B => Key.NumPad8,
            0x5C => Key.NumPad9,
            0x41 => Key.Decimal,
            0x43 => Key.Multiply,
            0x45 => Key.Add,
            0x4B => Key.Divide,
            0x4E => Key.Subtract,
            0x4C => Key.Enter,
            _ => Key.None
        };
    }
}
