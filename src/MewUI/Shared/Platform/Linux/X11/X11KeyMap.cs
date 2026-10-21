using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Platform.Linux.X11;

/// <summary>
/// Turns an X11 key press into a <see cref="Key"/>: the key its keysym names, or for a layout that types no Latin
/// letter there, the key another layout group or the US layout gives that position.
/// </summary>
internal static class X11KeyMap
{
    private const int LAYOUT_GROUPS = 4;

    // The extra key of ISO keyboards, left of Z; its keysyms (less, greater) are the comma and period keys' elsewhere.
    private const int ISO_EXTRA_KEYCODE = 94;

    // Positions of the letter, digit and punctuation keys by keycode (the evdev scan code plus 8), as a US layout names them.
    private static readonly Key[] _usLayoutByKeycode = BuildUsLayout();

    /// <summary>
    /// The key of a press: by its keysym, else by the keysym another layout group gives its keycode, else by its position.
    /// </summary>
    /// <param name="keysym">The unshifted keysym of the press in the current layout.</param>
    /// <param name="keycode">The keycode of the press.</param>
    /// <param name="state">Passed to <paramref name="keysymOfGroup"/>, so the caller's lookup needs no closure.</param>
    /// <param name="keysymOfGroup">The unshifted keysym a keycode has in a layout group, or 0.</param>
    public static Key Resolve<TState>(long keysym, int keycode, TState state, Func<TState, int, int, long> keysymOfGroup)
    {
        if (keycode == ISO_EXTRA_KEYCODE)
        {
            return Key.IntlBackslash;
        }

        var key = FromKeysym(keysym);
        for (int group = 0; group < LAYOUT_GROUPS && key == Key.None; group++)
        {
            key = FromKeysym(keysymOfGroup(state, keycode, group));
        }

        return key != Key.None ? key : FromPosition(keycode);
    }

    /// <summary>The key a keysym names, or <see cref="Key.None"/> for one that names none (a letter of another script, a modifier).</summary>
    public static Key FromKeysym(long keysym)
    {
        // Function keys
        if (keysym is >= 0xFFBE and <= 0xFFD5) // XK_F1..XK_F24
        {
            return (Key)((int)Key.F1 + (int)(keysym - 0xFFBE));
        }

        if (keysym is >= 0xFFB0 and <= 0xFFB9) // XK_KP_0..XK_KP_9
        {
            return (Key)((int)Key.NumPad0 + (int)(keysym - 0xFFB0));
        }

        // Keysyms of printable ASCII are the characters themselves.
        if (keysym is > 0x20 and < 0x7F)
        {
            return KeyCharacters.FromCharacter((char)keysym);
        }

        // Keypad keysyms (XK_KP_*) follow the same keys as their main-block counterparts.
        return keysym switch
        {
            0x20 => Key.Space,
            0xFF08 => Key.Backspace,
            0xFF09 or 0xFF89 => Key.Tab,
            0xFF0D or 0xFF8D => Key.Enter,
            0xFF1B => Key.Escape,
            0xFF50 or 0xFF95 => Key.Home,
            0xFF51 or 0xFF96 => Key.Left,
            0xFF52 or 0xFF97 => Key.Up,
            0xFF53 or 0xFF98 => Key.Right,
            0xFF54 or 0xFF99 => Key.Down,
            0xFF55 or 0xFF9A => Key.PageUp,
            0xFF56 or 0xFF9B => Key.PageDown,
            0xFF57 or 0xFF9C => Key.End,
            0xFF63 or 0xFF9E => Key.Insert,
            0xFFFF or 0xFF9F => Key.Delete,
            0xFF67 => Key.ContextMenu,
            0xFFAA => Key.Multiply,
            0xFFAB => Key.Add,
            0xFFAD => Key.Subtract,
            0xFFAC or 0xFFAE => Key.Decimal,
            0xFFAF => Key.Divide,
            _ => Key.None
        };
    }

    /// <summary>Whether a keysym is one of the keypad's (XK_KP_*), whose meaning follows Num Lock.</summary>
    public static bool IsKeypadKeysym(long keysym) => keysym is >= 0xFF80 and <= 0xFFBD;

    /// <summary>The key a US layout has at a keycode's position, for the letter, digit and punctuation keys only.</summary>
    public static Key FromPosition(int keycode)
        => keycode >= 0 && keycode < _usLayoutByKeycode.Length ? _usLayoutByKeycode[keycode] : Key.None;

    private static Key[] BuildUsLayout()
    {
        var keys = new Key[ISO_EXTRA_KEYCODE + 1];
        Row(keys, 10, Key.D1, Key.D2, Key.D3, Key.D4, Key.D5, Key.D6, Key.D7, Key.D8, Key.D9, Key.D0, Key.Minus, Key.Equal);
        Row(keys, 24, Key.Q, Key.W, Key.E, Key.R, Key.T, Key.Y, Key.U, Key.I, Key.O, Key.P, Key.BracketLeft, Key.BracketRight);
        Row(keys, 38, Key.A, Key.S, Key.D, Key.F, Key.G, Key.H, Key.J, Key.K, Key.L, Key.Semicolon, Key.Quote, Key.Backquote);
        Row(keys, 51, Key.Backslash, Key.Z, Key.X, Key.C, Key.V, Key.B, Key.N, Key.M, Key.Comma, Key.Period, Key.Slash);
        keys[ISO_EXTRA_KEYCODE] = Key.IntlBackslash;
        return keys;
    }

    private static void Row(Key[] keys, int firstKeycode, params Key[] row)
    {
        for (int index = 0; index < row.Length; index++)
        {
            keys[firstKeycode + index] = row[index];
        }
    }
}
