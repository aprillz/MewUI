// Ported from Windows Terminal src/terminal/input/terminalInput.hpp and terminalInput.cpp (MIT).

using System.Text;

namespace Aprillz.MewUI.Terminal.Input;

/// <summary>A key press or release, as the view reports it.</summary>
/// <param name="KeyDown">Whether the key went down (or repeats) rather than up.</param>
/// <param name="VirtualKey">The <see cref="Input.VirtualKey"/> code; 0 or <see cref="VirtualKey.PACKET"/> for text only.</param>
/// <param name="ScanCode">The hardware scan code, or 0 when unknown.</param>
/// <param name="Character">The UTF-16 code unit the key produced with its modifiers, or 0.</param>
/// <param name="ControlKeyState">The modifier and lock key state.</param>
internal readonly record struct TerminalKeyEvent(bool KeyDown, int VirtualKey, int ScanCode, char Character, ControlKeyStates ControlKeyState);

/// <summary>
/// Turns key, mouse and focus events into the byte sequences the application expects under its input modes. A result
/// of null means the event was not handled; an empty string means it was handled and sends nothing.
/// </summary>
internal sealed partial class TerminalInput
{
    public enum Mode
    {
        LineFeed,
        Ansi,
        AutoRepeat,
        Keypad,
        CursorKey,
        BackarrowKey,
        Win32,
        SendC1,
        Utf8MouseEncoding,
        SgrMouseEncoding,
        DefaultMouseTracking,
        ButtonEventMouseTracking,
        AnyEventMouseTracking,
        FocusEvent,
        AlternateScroll,
    }

    public const uint INVALID_CODEPOINT = 0x110000;

    private const uint CSI_SHIFT = 1;
    private const uint CSI_ALT = 2;
    private const uint CSI_CTRL = 4;

    // Application keypad mode (DECKPAM) is off in Windows Terminal releases: applications that enable it with
    // their cursor key mode would turn the number pad into escape sequences.
    private static readonly bool _keypadModeEnabled = false;

    private const int DEFAULT_INPUT_MODES = (1 << (int)Mode.Ansi) | (1 << (int)Mode.AutoRepeat) | (1 << (int)Mode.AlternateScroll);

    private const int MOUSE_TRACKING_MODES =
        (1 << (int)Mode.DefaultMouseTracking) | (1 << (int)Mode.ButtonEventMouseTracking) | (1 << (int)Mode.AnyEventMouseTracking);

    private const int MOUSE_ENCODING_MODES = (1 << (int)Mode.Utf8MouseEncoding) | (1 << (int)Mode.SgrMouseEncoding);

    // The modes that change the sequences the keyboard map produces.
    private const int KEY_MAP_MODES = (1 << (int)Mode.LineFeed) | (1 << (int)Mode.Ansi) | (1 << (int)Mode.Keypad)
        | (1 << (int)Mode.CursorKey) | (1 << (int)Mode.BackarrowKey) | (1 << (int)Mode.SendC1);

    private int _inputMode = DEFAULT_INPUT_MODES;
    private bool _inAlternateBuffer;
    private IKeyboardLayout _keyboardLayout = UsKeyboardLayout.Instance;

    private char _leadingSurrogate;
    private int? _lastVirtualKeyCode;
    private ControlKeyStates _lastControlKeyState;
    private ControlKeyStates _previousControlKeyState;

    private string _csi = string.Empty;
    private string _ss3 = string.Empty;
    private string _focusInSequence = string.Empty;
    private string _focusOutSequence = string.Empty;

    public TerminalInput()
    {
        InitKeyboardMap();
    }

    /// <summary>The layout that gives the characters of Ctrl and Alt combinations; US English unless the view sets one.</summary>
    public IKeyboardLayout KeyboardLayout
    {
        get => _keyboardLayout;
        set => _keyboardLayout = value;
    }

    public static string? MakeUnhandled() => null;

    public static string MakeOutput(ReadOnlySpan<char> text) => text.ToString();

    public void UseMainScreenBuffer() => _inAlternateBuffer = false;

    public void UseAlternateScreenBuffer() => _inAlternateBuffer = true;

    /// <summary>Sets an input mode; the mouse tracking modes, and the mouse encodings, exclude each other.</summary>
    public void SetInputMode(Mode mode, bool enabled)
    {
        int bit = 1 << (int)mode;

        // Changing the tracking mode forgets the last reported position and button.
        if ((bit & MOUSE_TRACKING_MODES) != 0)
        {
            _inputMode &= ~MOUSE_TRACKING_MODES;
            _mouseInputState = MouseInputState.Initial;
        }

        if ((bit & MOUSE_ENCODING_MODES) != 0 && enabled)
        {
            _inputMode &= ~MOUSE_ENCODING_MODES;
        }

        if (enabled)
        {
            _inputMode |= bit;
        }
        else
        {
            _inputMode &= ~bit;
        }

        if ((bit & KEY_MAP_MODES) != 0)
        {
            InitKeyboardMap();
        }
    }

    public bool GetInputMode(Mode mode) => (_inputMode & (1 << (int)mode)) != 0;

    public void ResetInputModes()
    {
        _inputMode = DEFAULT_INPUT_MODES;
        _mouseInputState = MouseInputState.Initial;
        InitKeyboardMap();
    }

    public string? HandleKey(TerminalKeyEvent keyEvent)
    {
        var key = new SanitizedKeyEvent
        {
            VirtualKey = keyEvent.VirtualKey,
            ScanCode = keyEvent.ScanCode,
            Codepoint = keyEvent.Character,
            ControlKeyState = TrackControlKeyState(keyEvent),
            KeyDown = keyEvent.KeyDown,
        };

        // A surrogate pair arrives as two key events; the first is held until the second.
        if (char.IsHighSurrogate(keyEvent.Character))
        {
            _leadingSurrogate = keyEvent.Character;
            return MakeNoOutput();
        }

        if (_leadingSurrogate != 0 && char.IsLowSurrogate(keyEvent.Character))
        {
            key.Codepoint = (uint)char.ConvertToUtf32(_leadingSurrogate, keyEvent.Character);
        }

        _leadingSurrogate = '\0';

        if (key.KeyDown)
        {
            // Text without a key (input methods, paste) is sent as is.
            if (key.VirtualKey == VirtualKey.PACKET || key.VirtualKey == 0)
            {
                return MakeCharOutput(key.Codepoint);
            }

            // Alt+numpad digits compose a character, which arrives with the Alt release; the digits send nothing.
            if (key.VirtualKey >= VirtualKey.NUMPAD0 && key.VirtualKey <= VirtualKey.NUMPAD9
                && (key.ControlKeyState == ControlKeyStates.LeftAltPressed || key.ControlKeyState == ControlKeyStates.RightAltPressed))
            {
                return MakeNoOutput();
            }
        }

        // A key that goes down again without going up is a repeat; modifiers are tracked by their state flags.
        if (key.KeyDown)
        {
            var flags = ControlKeyStateFromVirtualKey(key.VirtualKey, key.ControlKeyState);
            if (flags != ControlKeyStates.None)
            {
                key.KeyRepeat = (_previousControlKeyState & flags) != 0;
            }
            else
            {
                key.KeyRepeat = _lastVirtualKeyCode == key.VirtualKey;
                _lastVirtualKeyCode = key.VirtualKey;
            }
        }
        else
        {
            _lastVirtualKeyCode = null;
        }

        if (key.KeyRepeat)
        {
            if ((key.VirtualKey >= VirtualKey.SHIFT && key.VirtualKey <= VirtualKey.MENU)
                || (key.VirtualKey >= VirtualKey.LSHIFT && key.VirtualKey <= VirtualKey.RMENU)
                || !GetInputMode(Mode.AutoRepeat))
            {
                return MakeNoOutput();
            }
        }

        // Modifier keys alone send nothing.
        if ((key.VirtualKey >= VirtualKey.SHIFT && key.VirtualKey <= VirtualKey.MENU)
            || (key.VirtualKey >= VirtualKey.LSHIFT && key.VirtualKey <= VirtualKey.RMENU))
        {
            return MakeNoOutput();
        }

        if (!key.KeyDown)
        {
            // The character of an Alt+numpad composition arrives with the Alt release.
            if ((key.ControlKeyState & ControlKeyStates.NumlockOn) != 0 && key.VirtualKey == VirtualKey.MENU && key.Codepoint != 0)
            {
                return MakeCharOutput(key.Codepoint);
            }

            return MakeNoOutput();
        }

        // Ctrl+Alt with a printable character is AltGr, not a Ctrl or Alt combination.
        bool anyCtrlPressed = (key.ControlKeyState & ControlKeyStates.CtrlPressed) != 0;
        bool bothCtrlPressed = (key.ControlKeyState & ControlKeyStates.CtrlPressed) == ControlKeyStates.CtrlPressed;
        bool anyAltPressed = (key.ControlKeyState & ControlKeyStates.AltPressed) != 0;
        bool bothAltPressed = (key.ControlKeyState & ControlKeyStates.AltPressed) == ControlKeyStates.AltPressed;
        key.AltGrPressed = anyAltPressed && anyCtrlPressed && key.Codepoint > 0x20 && key.Codepoint != 0x7F;
        key.CtrlPressed = bothCtrlPressed || (anyCtrlPressed && !key.AltGrPressed);
        key.AltPressed = bothAltPressed || (anyAltPressed && !key.AltGrPressed);
        key.ShiftPressed = (key.ControlKeyState & ControlKeyStates.ShiftPressed) != 0;

        var enc = new EncodingHelper();
        if (key.CtrlPressed)
        {
            enc.CsiModifier |= CSI_CTRL;
        }

        if (key.AltPressed)
        {
            enc.CsiModifier |= CSI_ALT;
        }

        if (key.ShiftPressed)
        {
            enc.CsiModifier |= CSI_SHIFT;
        }

        EncodeRegular(ref enc, key);

        var seq = new StringBuilder();
        if (!FormatEncodingHelper(ref enc, key, seq))
        {
            FormatFallback(key, seq);
        }

        return seq.ToString();
    }

    public string? HandleFocus(bool focused)
    {
        if (!GetInputMode(Mode.FocusEvent))
        {
            return MakeUnhandled();
        }

        return MakeOutput(focused ? _focusInSequence : _focusOutSequence);
    }

    private void InitKeyboardMap()
    {
        if (GetInputMode(Mode.SendC1))
        {
            _csi = "\u009b";
            _ss3 = "\u008f";
            _focusInSequence = "\u009bI";
            _focusOutSequence = "\u009bO";
        }
        else
        {
            _csi = "\u001b[";
            _ss3 = "\u001bO";
            _focusInSequence = "\u001b[I";
            _focusOutSequence = "\u001b[O";
        }
    }

    /// <summary>
    /// Records the modifier state for repeat detection. A right Alt that appears without its own key press is the
    /// AltGr of a layout switch and is dropped.
    /// </summary>
    private ControlKeyStates TrackControlKeyState(TerminalKeyEvent keyEvent)
    {
        _previousControlKeyState = _lastControlKeyState;
        var pressedKeyState = ~_lastControlKeyState & keyEvent.ControlKeyState;
        _lastControlKeyState = keyEvent.ControlKeyState;

        bool rightAltDown = keyEvent.KeyDown && keyEvent.VirtualKey == VirtualKey.MENU && (keyEvent.ControlKeyState & ControlKeyStates.EnhancedKey) != 0;
        if ((pressedKeyState & ControlKeyStates.RightAltPressed) != 0 && !rightAltDown)
        {
            _lastControlKeyState &= ~ControlKeyStates.RightAltPressed;
        }

        return _lastControlKeyState;
    }

    private static ControlKeyStates ControlKeyStateFromVirtualKey(int virtualKey, ControlKeyStates controlKeyState)
    {
        bool enhanced = (controlKeyState & ControlKeyStates.EnhancedKey) != 0;
        return virtualKey switch
        {
            VirtualKey.SHIFT or VirtualKey.LSHIFT or VirtualKey.RSHIFT => ControlKeyStates.ShiftPressed,
            VirtualKey.CONTROL => enhanced ? ControlKeyStates.RightCtrlPressed : ControlKeyStates.LeftCtrlPressed,
            VirtualKey.LCONTROL => ControlKeyStates.LeftCtrlPressed,
            VirtualKey.RCONTROL => ControlKeyStates.RightCtrlPressed,
            VirtualKey.MENU => enhanced ? ControlKeyStates.RightAltPressed : ControlKeyStates.LeftAltPressed,
            VirtualKey.LMENU => ControlKeyStates.LeftAltPressed,
            VirtualKey.RMENU => ControlKeyStates.RightAltPressed,
            _ => ControlKeyStates.None,
        };
    }

    /// <summary>The C0 control a Ctrl combination of <paramref name="codepoint"/> sends, as xterm maps them.</summary>
    private static uint MakeCtrlChar(uint codepoint)
    {
        if (codepoint >= '@' && codepoint <= '~')
        {
            return codepoint & 0b11111;
        }

        if (codepoint == ' ')
        {
            return 0x00;
        }

        if (codepoint == '/')
        {
            return 0x1F;
        }

        if (codepoint == '?')
        {
            return 0x7F;
        }

        // Ctrl+2 to Ctrl+8 send the controls that need Shift on a US layout.
        if (codepoint >= '2' && codepoint <= '8')
        {
            ReadOnlySpan<byte> numericCtrls = [0, 27, 28, 29, 30, 31, 127];
            return numericCtrls[(int)(codepoint - '2')];
        }

        return codepoint;
    }

    private static string MakeCharOutput(uint codepoint)
    {
        var builder = new StringBuilder(2);
        PushCodepoint(builder, codepoint);
        return builder.ToString();
    }

    private static string MakeNoOutput() => string.Empty;

    private static void PushCodepoint(StringBuilder builder, uint codepoint)
    {
        if (codepoint <= 0xFFFF)
        {
            builder.Append((char)codepoint);
        }
        else
        {
            uint offset = codepoint - 0x10000;
            builder.Append((char)((offset >> 10) + 0xD800));
            builder.Append((char)((offset & 0x3FF) + 0xDC00));
        }
    }

    /// <summary>The key event with the modifier interpretation the encoders need.</summary>
    private struct SanitizedKeyEvent
    {
        public int VirtualKey;
        public int ScanCode;
        public uint Codepoint;
        public ControlKeyStates ControlKeyState;
        public bool KeyDown;
        public bool KeyRepeat;
        public bool AltGrPressed;
        public bool CtrlPressed;
        public bool AltPressed;
        public bool ShiftPressed;
    }

    /// <summary>The parts of a key sequence: a CSI or SS3 final with its parameters, or a plain string.</summary>
    private struct EncodingHelper
    {
        public uint CsiUnicodeKeyCode;

        // 1-based in the final sequence; 0 here means no modifier.
        public uint CsiModifier;
        public char CsiFinal;
        public char Ss3Final;
        public bool AltPrefix;
        public string? Plain;
    }

    private void EncodeRegular(ref EncodingHelper enc, in SanitizedKeyEvent key)
    {
        bool modified = enc.CsiModifier != 0;
        bool enhanced = (key.ControlKeyState & ControlKeyStates.EnhancedKey) != 0;
        bool ansi = GetInputMode(Mode.Ansi);

        switch (key.VirtualKey)
        {
            case VirtualKey.BACK:
                // DECBKM swaps which of Backspace and Ctrl+Backspace sends DEL.
                enc.AltPrefix = true;
                enc.Plain = key.CtrlPressed == GetInputMode(Mode.BackarrowKey) ? "\u007f" : "\b";
                break;
            case VirtualKey.TAB:
                enc.AltPrefix = true;
                if (!key.ShiftPressed)
                {
                    enc.Plain = "\t";
                }
                else
                {
                    enc.Plain = GetInputMode(Mode.SendC1) ? "\u009bZ" : "\u001b[Z";
                }

                break;
            case VirtualKey.RETURN:
                enc.AltPrefix = true;
                if (_keypadModeEnabled && GetInputMode(Mode.Keypad) && enhanced)
                {
                    if (ansi)
                    {
                        enc.Ss3Final = 'M';
                    }
                    else
                    {
                        enc.Plain = "\u001b?M";
                    }
                }
                else if (!key.CtrlPressed)
                {
                    enc.Plain = GetInputMode(Mode.LineFeed) ? "\r\n" : "\r";
                }
                else
                {
                    enc.Plain = "\n";
                }

                break;
            case VirtualKey.PAUSE:
                enc.Plain = "\u001a";
                break;
            case >= VirtualKey.F1 and <= VirtualKey.F4:
                if (ansi)
                {
                    char final = (char)('P' + (key.VirtualKey - VirtualKey.F1));
                    if (modified)
                    {
                        enc.CsiFinal = final;
                    }
                    else
                    {
                        enc.Ss3Final = final;
                    }
                }
                else
                {
                    enc.Plain = "\u001b" + (char)('P' + (key.VirtualKey - VirtualKey.F1));
                }

                break;
            case >= VirtualKey.F5 and <= VirtualKey.F20:
                if (ansi)
                {
                    ReadOnlySpan<byte> codes = [15, 17, 18, 19, 20, 21, 23, 24, 25, 26, 28, 29, 31, 32, 33, 34];
                    enc.CsiFinal = '~';
                    enc.CsiUnicodeKeyCode = codes[key.VirtualKey - VirtualKey.F5];
                }
                else
                {
                    // VT52 only has F11 to F13, as ESC, BS and LF.
                    enc.Plain = key.VirtualKey switch
                    {
                        VirtualKey.F11 => "\u001b",
                        VirtualKey.F12 => "\b",
                        VirtualKey.F13 => "\n",
                        _ => null,
                    };
                }

                break;
            case >= VirtualKey.LEFT and <= VirtualKey.DOWN:
                if (ansi)
                {
                    ReadOnlySpan<char> finals = ['D', 'A', 'C', 'B'];
                    SetCursorKeyFinal(ref enc, finals[key.VirtualKey - VirtualKey.LEFT], modified);
                }
                else
                {
                    enc.Plain = "\u001b" + "DACB"[key.VirtualKey - VirtualKey.LEFT];
                }

                break;
            case VirtualKey.CLEAR:
                if (ansi)
                {
                    SetCursorKeyFinal(ref enc, 'E', modified);
                }
                else
                {
                    enc.Plain = "\u001bE";
                }

                break;
            case VirtualKey.HOME:
                if (ansi)
                {
                    SetCursorKeyFinal(ref enc, 'H', modified);
                }
                else
                {
                    enc.Plain = "\u001bH";
                }

                break;
            case VirtualKey.END:
                if (ansi)
                {
                    SetCursorKeyFinal(ref enc, 'F', modified);
                }
                else
                {
                    enc.Plain = "\u001bF";
                }

                break;
            case VirtualKey.INSERT:
            case VirtualKey.DELETE:
                if (ansi)
                {
                    enc.CsiFinal = '~';
                    enc.CsiUnicodeKeyCode = (uint)(2 + (key.VirtualKey - VirtualKey.INSERT));
                }

                break;
            case VirtualKey.PRIOR:
            case VirtualKey.NEXT:
                if (ansi)
                {
                    enc.CsiFinal = '~';
                    enc.CsiUnicodeKeyCode = (uint)(5 + (key.VirtualKey - VirtualKey.PRIOR));
                }

                break;
            case >= VirtualKey.NUMPAD0 and <= VirtualKey.NUMPAD9:
                if (_keypadModeEnabled && GetInputMode(Mode.Keypad))
                {
                    char final = (char)('p' + (key.VirtualKey - VirtualKey.NUMPAD0));
                    if (ansi)
                    {
                        enc.Ss3Final = final;
                    }
                    else
                    {
                        enc.Plain = "\u001b?" + final;
                    }
                }

                break;
            case >= VirtualKey.MULTIPLY and <= VirtualKey.DIVIDE:
                if (_keypadModeEnabled && GetInputMode(Mode.Keypad))
                {
                    char final = (char)('j' + (key.VirtualKey - VirtualKey.MULTIPLY));
                    if (ansi)
                    {
                        enc.Ss3Final = final;
                    }
                    else
                    {
                        enc.Plain = "\u001b?" + final;
                    }
                }

                break;
        }
    }

    /// <summary>Cursor keys use SS3 in application cursor mode (DECCKM) unless modified, and CSI otherwise.</summary>
    private void SetCursorKeyFinal(ref EncodingHelper enc, char final, bool modified)
    {
        bool csi = modified || !GetInputMode(Mode.CursorKey);
        if (csi)
        {
            enc.CsiFinal = final;
        }
        else
        {
            enc.Ss3Final = final;
        }
    }

    private bool FormatEncodingHelper(ref EncodingHelper enc, in SanitizedKeyEvent key, StringBuilder seq)
    {
        if (enc.AltPrefix && key.AltPressed && GetInputMode(Mode.Ansi))
        {
            seq.Append('\u001b');
        }

        if (enc.CsiFinal != 0)
        {
            // A modified key always has a key code, 1 if it had none, so the modifier lands in the second parameter.
            if (enc.CsiModifier != 0)
            {
                enc.CsiModifier += 1;
                enc.CsiUnicodeKeyCode = Math.Max(enc.CsiUnicodeKeyCode, 1);
            }

            seq.Append(_csi);
            if (enc.CsiUnicodeKeyCode != 0)
            {
                seq.Append(enc.CsiUnicodeKeyCode);
            }

            if (enc.CsiModifier != 0)
            {
                seq.Append(';');
                seq.Append(enc.CsiModifier);
            }

            seq.Append(enc.CsiFinal);
            return true;
        }

        if (enc.Ss3Final != 0)
        {
            seq.Append(_ss3);
            seq.Append(enc.Ss3Final);
            return true;
        }

        if (!string.IsNullOrEmpty(enc.Plain))
        {
            seq.Append(enc.Plain);
            return true;
        }

        return false;
    }

    /// <summary>Sends the character of the key: with Ctrl as its C0 control, with Alt after ESC.</summary>
    private void FormatFallback(in SanitizedKeyEvent key, StringBuilder seq)
    {
        if (!key.KeyDown)
        {
            return;
        }

        uint codepoint = key.Codepoint;

        // Ctrl+Space produces a space, which has to become NUL through the unmodified key.
        bool ctrlSpaceKey = key.CtrlPressed && key.VirtualKey == VirtualKey.SPACE;

        if (codepoint != 0 && !ctrlSpaceKey)
        {
            if (key.CtrlPressed)
            {
                codepoint = MakeCtrlChar(codepoint);
            }
        }
        else if (key.AltPressed || key.CtrlPressed)
        {
            // Ctrl and Alt combinations often produce no character, so ask the layout what the key would produce without them.
            codepoint = GetUnmodifiedKeyboardKey(key);
            if (codepoint >= INVALID_CODEPOINT)
            {
                return;
            }

            if (key.CtrlPressed)
            {
                codepoint = MakeCtrlChar(codepoint);

                // A key whose character has no control (Ctrl+Shift+2 gives '@' on US, other layouts other characters)
                // falls back to the key's US position, as Windows itself does.
                if (codepoint >= ' ' && key.VirtualKey >= '2' && key.VirtualKey <= 'Z')
                {
                    codepoint = MakeCtrlChar((uint)key.VirtualKey);
                }
            }
        }
        else
        {
            return;
        }

        if (key.AltPressed && GetInputMode(Mode.Ansi))
        {
            seq.Append('\u001b');
        }

        PushCodepoint(seq, codepoint);
    }

    /// <summary>The character of the key without Ctrl and Alt, keeping AltGr.</summary>
    private uint GetUnmodifiedKeyboardKey(in SanitizedKeyEvent key)
    {
        var controlKeyState = key.ControlKeyState & ~(ControlKeyStates.AltPressed | ControlKeyStates.CtrlPressed);
        if (key.AltGrPressed)
        {
            controlKeyState |= ControlKeyStates.LeftCtrlPressed | ControlKeyStates.RightAltPressed;
        }

        return _keyboardLayout.GetCharacter(key.VirtualKey & 0xFF, controlKeyState);
    }
}
