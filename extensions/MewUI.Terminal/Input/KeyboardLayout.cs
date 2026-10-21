// Replaces the ToUnicodeEx calls of Windows Terminal src/terminal/input/terminalInput.cpp (MIT) KeyboardHelper.

namespace Aprillz.MewUI.Terminal.Input;

/// <summary>The character a key produces under a keyboard layout, for Ctrl and Alt combinations that carry no text.</summary>
internal interface IKeyboardLayout
{
    /// <summary>
    /// The code point <paramref name="virtualKey"/> produces with the given Shift, Caps Lock, Ctrl and Alt state, or
    /// <see cref="TerminalInput.INVALID_CODEPOINT"/> when it produces none or more than one.
    /// </summary>
    uint GetCharacter(int virtualKey, ControlKeyStates state);
}

/// <summary>
/// The texts the platform reported for one key event: what the key types (<see cref="KeySymbol"/>) and what it types
/// with only Shift and Caps Lock (<see cref="BaseKeySymbol"/>). Both null when the platform reports neither.
/// </summary>
internal readonly record struct TerminalKeySymbols(string? KeySymbol, string? BaseKeySymbol)
{
    public bool IsReported => KeySymbol != null || BaseKeySymbol != null;
}

/// <summary>
/// The layout as one key event reports it: the event's key answers from the platform's texts, every other key from
/// <paramref name="fallback"/>. Replaces the US assumption for the key being encoded, so Alt and Ctrl combinations
/// follow the user's layout.
/// </summary>
internal sealed class EventKeyboardLayout(int eventVirtualKey, TerminalKeySymbols symbols, ControlKeyStates eventState, IKeyboardLayout fallback) : IKeyboardLayout
{
    public uint GetCharacter(int virtualKey, ControlKeyStates state)
    {
        if (virtualKey != eventVirtualKey)
        {
            return fallback.GetCharacter(virtualKey, state);
        }

        // With Ctrl or Alt the key types nothing of its own here; the encoder then asks for the key without them.
        if ((state & (ControlKeyStates.CtrlPressed | ControlKeyStates.AltPressed)) != 0)
        {
            return TerminalInput.INVALID_CODEPOINT;
        }

        // Asked without Ctrl and Alt for an event that had them, the key is its base text (ESC a for Alt+a, not ESC å).
        bool eventHadControlOrAlt = (eventState & (ControlKeyStates.CtrlPressed | ControlKeyStates.AltPressed)) != 0;
        string? text = eventHadControlOrAlt ? symbols.BaseKeySymbol : symbols.KeySymbol ?? symbols.BaseKeySymbol;
        return CodePoint(text);
    }

    private static uint CodePoint(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return TerminalInput.INVALID_CODEPOINT;
        }

        bool single = text.Length == 1 || (text.Length == 2 && char.IsSurrogatePair(text[0], text[1]));
        return single ? (uint)char.ConvertToUtf32(text, 0) : TerminalInput.INVALID_CODEPOINT;
    }
}

/// <summary>The US English layout, which has no AltGr level: any Ctrl or Alt state produces nothing.</summary>
internal sealed class UsKeyboardLayout : IKeyboardLayout
{
    public static UsKeyboardLayout Instance { get; } = new();

    public uint GetCharacter(int virtualKey, ControlKeyStates state)
    {
        if ((state & (ControlKeyStates.CtrlPressed | ControlKeyStates.AltPressed)) != 0)
        {
            return TerminalInput.INVALID_CODEPOINT;
        }

        bool shift = (state & ControlKeyStates.ShiftPressed) != 0;
        bool capsLock = (state & ControlKeyStates.CapslockOn) != 0;

        if (virtualKey >= VirtualKey.KEY_A && virtualKey < VirtualKey.KEY_A + 26)
        {
            char letter = (char)('a' + (virtualKey - VirtualKey.KEY_A));
            return shift != capsLock ? char.ToUpperInvariant(letter) : letter;
        }

        if (virtualKey >= VirtualKey.KEY_0 && virtualKey < VirtualKey.KEY_0 + 10)
        {
            const string SHIFTED_DIGITS = ")!@#$%^&*(";
            int digit = virtualKey - VirtualKey.KEY_0;
            return shift ? SHIFTED_DIGITS[digit] : (uint)('0' + digit);
        }

        if (virtualKey >= VirtualKey.NUMPAD0 && virtualKey <= VirtualKey.NUMPAD9)
        {
            return (uint)('0' + (virtualKey - VirtualKey.NUMPAD0));
        }

        var (unshifted, shifted) = virtualKey switch
        {
            VirtualKey.CANCEL => ('\u0003', '\u0003'),
            VirtualKey.SPACE => (' ', ' '),
            VirtualKey.BACK => ('\b', '\b'),
            VirtualKey.TAB => ('\t', '\t'),
            VirtualKey.RETURN => ('\r', '\r'),
            VirtualKey.ESCAPE => ('\u001b', '\u001b'),
            VirtualKey.MULTIPLY => ('*', '*'),
            VirtualKey.ADD => ('+', '+'),
            VirtualKey.SUBTRACT => ('-', '-'),
            VirtualKey.DECIMAL => ('.', '.'),
            VirtualKey.DIVIDE => ('/', '/'),
            VirtualKey.OEM_1 => (';', ':'),
            VirtualKey.OEM_PLUS => ('=', '+'),
            VirtualKey.OEM_COMMA => (',', '<'),
            VirtualKey.OEM_MINUS => ('-', '_'),
            VirtualKey.OEM_PERIOD => ('.', '>'),
            VirtualKey.OEM_2 => ('/', '?'),
            VirtualKey.OEM_3 => ('`', '~'),
            VirtualKey.OEM_4 => ('[', '{'),
            VirtualKey.OEM_5 => ('\\', '|'),
            VirtualKey.OEM_6 => (']', '}'),
            VirtualKey.OEM_7 => ('\'', '"'),
            VirtualKey.OEM_102 => ('\\', '|'),
            _ => ('\0', '\0'),
        };

        if (unshifted == '\0')
        {
            return TerminalInput.INVALID_CODEPOINT;
        }

        return shift ? shifted : unshifted;
    }
}
