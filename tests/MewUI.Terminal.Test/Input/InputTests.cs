// Ported from Windows Terminal src/terminal/adapter/ut_adapter/inputTest.cpp (MIT).
// The original asks the Windows keyboard APIs (ToUnicodeEx, MapVirtualKeyW) for the character of each key under the
// US layout; here TranslateKey and MapVirtualKeyToChar compute the same values from the US layout of the extension.
// Not ported: the checks that INPUT_RECORDs other than key events (mouse, window buffer size, menu) are not handled,
// because the C# API only takes key events.

using Aprillz.MewUI.Terminal.Input;

namespace MewUI.Terminal.Test.Input;

[TestClass]
public sealed class InputTests
{
    private const int LAST_VIRTUAL_KEY = 255;

    // The CSI parameters of F5 to F20.
    private static readonly int[] _functionKeyCodes = [15, 17, 18, 19, 20, 21, 23, 24, 25, 26, 28, 29, 31, 32, 33, 34];

    private static string DescribeKey(ControlKeyStates keyState, int virtualKey)
    {
        return $"state=0x{(uint)keyState:X}, vkey=0x{virtualKey:X2}";
    }

    // Sends a key down; a right Alt needs an Alt key event first, or the modifier is dropped.
    private static void TestKey(string? expected, TerminalInput input, ControlKeyStates keyState, int virtualKey, char character = '\0')
    {
        string description = DescribeKey(keyState, virtualKey);

        if ((keyState & ControlKeyStates.RightAltPressed) != 0)
        {
            var altEvent = new TerminalKeyEvent(true, VirtualKey.MENU, 0, '\0', keyState | ControlKeyStates.EnhancedKey);
            Assert.AreEqual(string.Empty, input.HandleKey(altEvent), $"Verify the Alt key sends nothing. {description}");
        }

        var keyEvent = new TerminalKeyEvent(true, virtualKey, 0, character, keyState);
        Assert.AreEqual(expected, input.HandleKey(keyEvent), $"Verify key was handled if it should have been. {description}");
    }

    // MapVirtualKeyW(vkey, MAPVK_VK_TO_CHAR) on the US layout: the unshifted character, upper case for letters.
    private static char MapVirtualKeyToChar(int virtualKey)
    {
        if (virtualKey == VirtualKey.CANCEL)
        {
            return '\u0003';
        }

        uint codepoint = UsKeyboardLayout.Instance.GetCharacter(virtualKey, ControlKeyStates.None);
        if (codepoint >= TerminalInput.INVALID_CODEPOINT)
        {
            return '\0';
        }

        return char.ToUpperInvariant((char)codepoint);
    }

    // ToUnicodeEx on the US layout with the given modifiers held: Ctrl turns keys into their C0 controls.
    private static char TranslateKey(int virtualKey, ControlKeyStates keyState)
    {
        var layoutState = keyState & (ControlKeyStates.ShiftPressed | ControlKeyStates.CapslockOn);
        uint codepoint = UsKeyboardLayout.Instance.GetCharacter(virtualKey, layoutState);
        char baseCharacter = codepoint >= TerminalInput.INVALID_CODEPOINT ? '\0' : (char)codepoint;

        if ((keyState & ControlKeyStates.CtrlPressed) == 0)
        {
            return baseCharacter;
        }

        bool shiftPressed = (keyState & ControlKeyStates.ShiftPressed) != 0;
        if (char.IsAsciiLetter(baseCharacter))
        {
            return (char)(baseCharacter & 0x1F);
        }

        if (virtualKey == VirtualKey.OEM_4)
        {
            return '\u001b';
        }

        if (virtualKey == VirtualKey.OEM_5)
        {
            return '\u001c';
        }

        if (virtualKey == VirtualKey.OEM_6)
        {
            return '\u001d';
        }

        if (shiftPressed && virtualKey == '6')
        {
            return '\u001e';
        }

        if (shiftPressed && virtualKey == VirtualKey.OEM_MINUS)
        {
            return '\u001f';
        }

        if (shiftPressed && virtualKey == '2')
        {
            return '\0';
        }

        if (virtualKey == VirtualKey.BACK)
        {
            return '\u007f';
        }

        if (virtualKey == VirtualKey.RETURN)
        {
            return '\n';
        }

        if (virtualKey == VirtualKey.ESCAPE)
        {
            return '\u001b';
        }

        return baseCharacter;
    }

    // The sequence of a key that carries modifiers as a parameter, with 'm' standing for the modifier digit.
    private static string? GetModifiedSequenceTemplate(int virtualKey)
    {
        switch (virtualKey)
        {
            case VirtualKey.UP:
                return "\u001b[1;mA";
            case VirtualKey.DOWN:
                return "\u001b[1;mB";
            case VirtualKey.RIGHT:
                return "\u001b[1;mC";
            case VirtualKey.LEFT:
                return "\u001b[1;mD";
            case VirtualKey.CLEAR:
                return "\u001b[1;mE";
            case VirtualKey.HOME:
                return "\u001b[1;mH";
            case VirtualKey.INSERT:
                return "\u001b[2;m~";
            case VirtualKey.DELETE:
                return "\u001b[3;m~";
            case VirtualKey.END:
                return "\u001b[1;mF";
            case VirtualKey.PRIOR:
                return "\u001b[5;m~";
            case VirtualKey.NEXT:
                return "\u001b[6;m~";
            case >= VirtualKey.F1 and <= VirtualKey.F4:
                return "\u001b[1;m" + (char)('P' + (virtualKey - VirtualKey.F1));
            case >= VirtualKey.F5 and <= VirtualKey.F20:
                return $"\u001b[{_functionKeyCodes[virtualKey - VirtualKey.F5]};m~";
            default:
                return null;
        }
    }

    private static string GetUnmodifiedExpectedOutput(int virtualKey, char character)
    {
        switch (virtualKey)
        {
            case VirtualKey.TAB:
                return "\t";
            case VirtualKey.BACK:
                return "\u007f";
            case VirtualKey.ESCAPE:
                return "\u001b";
            case VirtualKey.PAUSE:
                return "\u001a";
            case VirtualKey.UP:
                return "\u001b[A";
            case VirtualKey.DOWN:
                return "\u001b[B";
            case VirtualKey.RIGHT:
                return "\u001b[C";
            case VirtualKey.LEFT:
                return "\u001b[D";
            case VirtualKey.CLEAR:
                return "\u001b[E";
            case VirtualKey.HOME:
                return "\u001b[H";
            case VirtualKey.INSERT:
                return "\u001b[2~";
            case VirtualKey.DELETE:
                return "\u001b[3~";
            case VirtualKey.END:
                return "\u001b[F";
            case VirtualKey.PRIOR:
                return "\u001b[5~";
            case VirtualKey.NEXT:
                return "\u001b[6~";
            case >= VirtualKey.F1 and <= VirtualKey.F4:
                return "\u001bO" + (char)('P' + (virtualKey - VirtualKey.F1));
            case >= VirtualKey.F5 and <= VirtualKey.F20:
                return $"\u001b[{_functionKeyCodes[virtualKey - VirtualKey.F5]}~";
            case VirtualKey.CANCEL:
                return "\u0003";
            default:
                bool synthesizedKeyPress = virtualKey == VirtualKey.PACKET || virtualKey == 0;
                if (character != 0 || synthesizedKeyPress)
                {
                    return character.ToString();
                }

                return string.Empty;
        }
    }

    [TestMethod]
    public void TerminalInputTests()
    {
        var input = new TerminalInput();

        // Sending every possible VKEY at the input stream for interception during key down.
        for (int virtualKey = 0; virtualKey < LAST_VIRTUAL_KEY; virtualKey++)
        {
            char character = MapVirtualKeyToChar(virtualKey);
            var keyEvent = new TerminalKeyEvent(true, virtualKey, 0, character, ControlKeyStates.None);

            // Unhandled keys are expected to return an empty string.
            string expected = GetUnmodifiedExpectedOutput(virtualKey, character);

            Assert.AreEqual(expected, input.HandleKey(keyEvent), $"Verify key was handled if it should have been. vkey=0x{virtualKey:X2}");
        }

        // Sending every possible VKEY at the input stream for interception during key up.
        for (int virtualKey = 0; virtualKey < LAST_VIRTUAL_KEY; virtualKey++)
        {
            var keyEvent = new TerminalKeyEvent(false, virtualKey, 0, '\0', ControlKeyStates.None);

            Assert.AreEqual(string.Empty, input.HandleKey(keyEvent), $"Verify output is blank. vkey=0x{virtualKey:X2}");
        }
    }

    [TestMethod]
    public void TestFocusEvents()
    {
        var input = new TerminalInput();

        Assert.IsNull(input.HandleFocus(false));
        Assert.IsNull(input.HandleFocus(true));

        input.SetInputMode(TerminalInput.Mode.FocusEvent, true);

        Assert.AreEqual("\u001b[O", input.HandleFocus(false));
        Assert.AreEqual("\u001b[I", input.HandleFocus(true));
    }

    // Right Alt, left Alt, both, then each Ctrl and Shift combination the original lists.
    [TestMethod]
    [DataRow(0x0001)]
    [DataRow(0x0002)]
    [DataRow(0x0003)]
    [DataRow(0x0004)]
    [DataRow(0x0005)]
    [DataRow(0x0006)]
    [DataRow(0x0007)]
    [DataRow(0x0008)]
    [DataRow(0x000A)]
    [DataRow(0x000C)]
    [DataRow(0x000E)]
    [DataRow(0x0010)]
    [DataRow(0x0011)]
    [DataRow(0x0012)]
    [DataRow(0x0013)]
    public void TerminalInputModifierKeyTests(int modifierKeyState)
    {
        var keyState = (ControlKeyStates)modifierKeyState;
        var input = new TerminalInput();

        const int SLASH_VIRTUAL_KEY = VirtualKey.OEM_2;

        // VkKeyScanW(0) is Ctrl+Shift+2 on the US layout.
        const int NULL_VIRTUAL_KEY = '2';

        bool anyCtrlPressed = (keyState & ControlKeyStates.CtrlPressed) != 0;
        bool bothCtrlPressed = (keyState & ControlKeyStates.CtrlPressed) == ControlKeyStates.CtrlPressed;
        bool anyAltPressed = (keyState & ControlKeyStates.AltPressed) != 0;
        bool bothAltPressed = (keyState & ControlKeyStates.AltPressed) == ControlKeyStates.AltPressed;
        bool shiftPressed = (keyState & ControlKeyStates.ShiftPressed) != 0;

        // Sending every possible VKEY at the input stream for interception during key down.
        for (int virtualKey = 0; virtualKey < LAST_VIRTUAL_KEY; virtualKey++)
        {
            char character = TranslateKey(virtualKey, keyState);
            bool altGrPressed = anyAltPressed && anyCtrlPressed && character > 0x20 && character != 0x7F;
            bool ctrlPressed = bothCtrlPressed || (anyCtrlPressed && !altGrPressed);
            bool altPressed = bothAltPressed || (anyAltPressed && !altGrPressed);

            if (ctrlPressed)
            {
                // For Ctrl-/ see DifferentModifiersTest.
                if (virtualKey == VirtualKey.DIVIDE || virtualKey == SLASH_VIRTUAL_KEY)
                {
                    continue;
                }

                // For Ctrl-@ and Ctrl-Space see TerminalInputNullKeyTests.
                if (virtualKey == NULL_VIRTUAL_KEY || virtualKey == ' ')
                {
                    continue;
                }
            }

            // Unhandled keys are expected to return an empty string.
            string expected = string.Empty;
            string? template = GetModifiedSequenceTemplate(virtualKey);

            if (virtualKey == VirtualKey.BACK)
            {
                // Backspace is different from other keys, DifferentModifiersTest covers it.
                continue;
            }
            else if (template != null)
            {
                int modifier = (shiftPressed ? 1 : 0) + (altPressed ? 2 : 0) + (ctrlPressed ? 4 : 0);
                if (modifier == 0)
                {
                    continue;
                }

                var chars = template.ToCharArray();
                chars[chars.Length - 2] = (char)('1' + modifier);
                expected = new string(chars);
            }
            else if (virtualKey == VirtualKey.PAUSE)
            {
                expected = "\u001a";
            }
            else if (virtualKey == VirtualKey.PACKET || virtualKey == 0)
            {
                // VK_PACKET and 0 virtual keys are used for synthesized key presses.
                expected = character.ToString();
            }
            else if (virtualKey == VirtualKey.RETURN)
            {
                if (altPressed)
                {
                    expected = ctrlPressed ? "\u001b\n" : "\u001b\r";
                }
                else
                {
                    expected = ctrlPressed ? "\n" : "\r";
                }
            }
            else if (virtualKey == VirtualKey.TAB)
            {
                if (altPressed)
                {
                    // Alt+Tab isn't possible, the system reserves it.
                    continue;
                }
                else if (shiftPressed)
                {
                    expected = "\u001b[Z";
                }
                else
                {
                    expected = "\t";
                }
            }
            else
            {
                // OEM keys require special case handling when combined with a Ctrl modifier, but otherwise work like regular keys.
                bool oemKey = virtualKey == VirtualKey.OEM_2 || virtualKey == VirtualKey.OEM_3 || virtualKey == VirtualKey.OEM_4
                    || virtualKey == VirtualKey.OEM_5 || virtualKey == VirtualKey.OEM_6 || virtualKey == VirtualKey.OEM_102;
                if (oemKey && ctrlPressed)
                {
                    continue;
                }

                // Map VK_ESCAPE, etc., to their corresponding character value, if needed.
                if (character == 0)
                {
                    character = MapVirtualKeyToChar(virtualKey);
                }

                if (ctrlPressed && virtualKey >= '1' && virtualKey <= '9')
                {
                    // Ctrl and a digit give very specific control characters, which CtrlNumTest checks.
                    continue;
                }

                if (virtualKey >= VirtualKey.NUMPAD0 && virtualKey <= VirtualKey.NUMPAD9)
                {
                    // Numpad keys have the same complications as digits with Ctrl, and with Alt they compose characters.
                    continue;
                }

                if (altPressed && ctrlPressed && character > '@' && character <= '~')
                {
                    // Alt+Key generates ESC and Ctrl+key; Ctrl+key masks all but the 5 low bits.
                    expected = "\u001b" + (char)(character & 0b11111);
                    character = '\0';
                }
                else if (altPressed && character != 0)
                {
                    // Alt+Key generates ESC and the key.
                    expected = "\u001b" + character;
                    if (ctrlPressed)
                    {
                        character = '\0';
                    }
                }
                else if (ctrlPressed && character > '@' && character <= '~')
                {
                    expected = ((char)(character & 0b11111)).ToString();
                }
                else if (character != 0)
                {
                    expected = character.ToString();
                }
            }

            TestKey(expected, input, keyState, virtualKey, character);
        }
    }

    [TestMethod]
    public void TerminalInputNullKeyTests()
    {
        var input = new TerminalInput();

        // VkKeyScanExW(0) on the US layout is Ctrl+Shift+2, so the key is '2'.
        int virtualKey = '2';
        var keyState = ControlKeyStates.LeftCtrlPressed;
        var keyEvent = new TerminalKeyEvent(true, virtualKey, 0, '\0', keyState);

        Assert.AreEqual("\0", input.HandleKey(keyEvent), "Verify key was handled if it should have been. Ctrl+2");

        virtualKey = VirtualKey.SPACE;
        keyEvent = keyEvent with { VirtualKey = virtualKey };
        Assert.AreEqual("\0", input.HandleKey(keyEvent), "Verify key was handled if it should have been. Ctrl+Space");

        keyState = ControlKeyStates.LeftCtrlPressed | ControlKeyStates.LeftAltPressed;
        keyEvent = keyEvent with { ControlKeyState = keyState };
        Assert.AreEqual("\u001b\0", input.HandleKey(keyEvent), "Verify key was handled if it should have been. LeftCtrl+LeftAlt+Space");

        keyState = ControlKeyStates.RightCtrlPressed | ControlKeyStates.LeftAltPressed;
        keyEvent = keyEvent with { ControlKeyState = keyState };
        Assert.AreEqual("\u001b\0", input.HandleKey(keyEvent), "Verify key was handled if it should have been. RightCtrl+LeftAlt+Space");
    }

    [TestMethod]
    public void DifferentModifiersTest()
    {
        var input = new TerminalInput();

        // Sending a bunch of keystrokes that are a little weird.
        var keyState = ControlKeyStates.None;
        int virtualKey = VirtualKey.BACK;
        TestKey("\u007f", input, keyState, virtualKey);

        keyState = ControlKeyStates.LeftCtrlPressed;
        virtualKey = VirtualKey.BACK;
        TestKey("\b", input, keyState, virtualKey, '\b');
        keyState = ControlKeyStates.RightCtrlPressed;
        TestKey("\b", input, keyState, virtualKey, '\b');

        keyState = ControlKeyStates.LeftAltPressed;
        virtualKey = VirtualKey.BACK;
        TestKey("\u001b\u007f", input, keyState, virtualKey, '\b');
        keyState = ControlKeyStates.RightAltPressed;
        TestKey("\u001b\u007f", input, keyState, virtualKey, '\b');

        keyState = ControlKeyStates.LeftCtrlPressed;
        virtualKey = VirtualKey.DELETE;
        TestKey("\u001b[3;5~", input, keyState, virtualKey);
        keyState = ControlKeyStates.RightCtrlPressed;
        TestKey("\u001b[3;5~", input, keyState, virtualKey);

        keyState = ControlKeyStates.LeftAltPressed;
        virtualKey = VirtualKey.DELETE;
        TestKey("\u001b[3;3~", input, keyState, virtualKey);
        keyState = ControlKeyStates.RightAltPressed;
        TestKey("\u001b[3;3~", input, keyState, virtualKey);

        keyState = ControlKeyStates.LeftCtrlPressed;
        virtualKey = VirtualKey.TAB;
        TestKey("\t", input, keyState, virtualKey);
        keyState = ControlKeyStates.RightCtrlPressed;
        TestKey("\t", input, keyState, virtualKey);

        keyState = ControlKeyStates.ShiftPressed;
        virtualKey = VirtualKey.TAB;
        TestKey("\u001b[Z", input, keyState, virtualKey);

        // C-/ -> C-_ -> 0x1f
        keyState = ControlKeyStates.LeftCtrlPressed;
        virtualKey = VirtualKey.OEM_2;
        TestKey("\u001f", input, keyState, virtualKey);
        keyState = ControlKeyStates.RightCtrlPressed;
        TestKey("\u001f", input, keyState, virtualKey);

        // M-/ -> ESC /
        keyState = ControlKeyStates.LeftAltPressed;
        virtualKey = VirtualKey.OEM_2;
        TestKey("\u001b/", input, keyState, virtualKey, '/');
        keyState = ControlKeyStates.RightAltPressed;
        TestKey("\u001b/", input, keyState, virtualKey, '/');

        // C-? -> DEL -> 0x7f, with Shift to tell '/' and '?' apart (microsoft/terminal#4947).
        virtualKey = VirtualKey.OEM_2;
        TestKey("\u007f", input, ControlKeyStates.ShiftPressed | ControlKeyStates.LeftCtrlPressed, virtualKey);
        TestKey("\u007f", input, ControlKeyStates.ShiftPressed | ControlKeyStates.RightCtrlPressed, virtualKey);

        // C-M-/ -> 0x1b0x1f; left Ctrl with right Alt is skipped, because that's AltGr.
        virtualKey = VirtualKey.OEM_2;
        TestKey("\u001b\u001f", input, ControlKeyStates.LeftCtrlPressed | ControlKeyStates.LeftAltPressed, virtualKey);
        TestKey("\u001b\u001f", input, ControlKeyStates.RightCtrlPressed | ControlKeyStates.LeftAltPressed, virtualKey);
        TestKey("\u001b\u001f", input, ControlKeyStates.RightCtrlPressed | ControlKeyStates.RightAltPressed, virtualKey);

        // C-M-? -> 0x1b0x7f; left Ctrl with right Alt is skipped, because that's AltGr.
        virtualKey = VirtualKey.OEM_2;
        TestKey("\u001b\u007f", input, ControlKeyStates.ShiftPressed | ControlKeyStates.LeftCtrlPressed | ControlKeyStates.LeftAltPressed, virtualKey);
        TestKey("\u001b\u007f", input, ControlKeyStates.ShiftPressed | ControlKeyStates.RightCtrlPressed | ControlKeyStates.LeftAltPressed, virtualKey);
        TestKey("\u001b\u007f", input, ControlKeyStates.ShiftPressed | ControlKeyStates.RightCtrlPressed | ControlKeyStates.RightAltPressed, virtualKey);
    }

    [TestMethod]
    public void CtrlNumTest()
    {
        var input = new TerminalInput();

        // Ctrl+2 is skipped in the original too: it sends NUL, which TerminalInputNullKeyTests covers.
        var keyState = ControlKeyStates.LeftCtrlPressed;
        TestKey("1", input, keyState, '1');
        TestKey("\u001b", input, keyState, '3');
        TestKey("\u001c", input, keyState, '4');
        TestKey("\u001d", input, keyState, '5');
        TestKey("\u001e", input, keyState, '6');
        TestKey("\u001f", input, keyState, '7');
        TestKey("\u007f", input, keyState, '8');
        TestKey("9", input, keyState, '9');
    }

    [TestMethod]
    public void BackarrowKeyModeTest()
    {
        var input = new TerminalInput();
        const int VIRTUAL_KEY = VirtualKey.BACK;

        // Sending backspace key combos with DECBKM enabled.
        input.SetInputMode(TerminalInput.Mode.BackarrowKey, true);
        TestKey("\b", input, ControlKeyStates.None, VIRTUAL_KEY);
        TestKey("\b", input, ControlKeyStates.ShiftPressed, VIRTUAL_KEY);
        TestKey("\u007f", input, ControlKeyStates.LeftCtrlPressed, VIRTUAL_KEY);
        TestKey("\u007f", input, ControlKeyStates.LeftCtrlPressed | ControlKeyStates.ShiftPressed, VIRTUAL_KEY);
        TestKey("\u001b\b", input, ControlKeyStates.LeftAltPressed, VIRTUAL_KEY);
        TestKey("\u001b\b", input, ControlKeyStates.LeftAltPressed | ControlKeyStates.ShiftPressed, VIRTUAL_KEY);
        TestKey("\u001b\u007f", input, ControlKeyStates.LeftAltPressed | ControlKeyStates.LeftCtrlPressed, VIRTUAL_KEY);
        TestKey("\u001b\u007f", input, ControlKeyStates.LeftAltPressed | ControlKeyStates.LeftCtrlPressed | ControlKeyStates.ShiftPressed, VIRTUAL_KEY);

        // Sending backspace key combos with DECBKM disabled.
        input.SetInputMode(TerminalInput.Mode.BackarrowKey, false);
        TestKey("\u007f", input, ControlKeyStates.None, VIRTUAL_KEY);
        TestKey("\u007f", input, ControlKeyStates.ShiftPressed, VIRTUAL_KEY);
        TestKey("\b", input, ControlKeyStates.LeftCtrlPressed, VIRTUAL_KEY);
        TestKey("\b", input, ControlKeyStates.LeftCtrlPressed | ControlKeyStates.ShiftPressed, VIRTUAL_KEY);
        TestKey("\u001b\u007f", input, ControlKeyStates.LeftAltPressed, VIRTUAL_KEY);
        TestKey("\u001b\u007f", input, ControlKeyStates.LeftAltPressed | ControlKeyStates.ShiftPressed, VIRTUAL_KEY);
        TestKey("\u001b\b", input, ControlKeyStates.LeftAltPressed | ControlKeyStates.LeftCtrlPressed, VIRTUAL_KEY);
        TestKey("\u001b\b", input, ControlKeyStates.LeftAltPressed | ControlKeyStates.LeftCtrlPressed | ControlKeyStates.ShiftPressed, VIRTUAL_KEY);
    }

    [TestMethod]
    public void AutoRepeatModeTest()
    {
        var down = new TerminalKeyEvent(true, 'A', 0, 'A', ControlKeyStates.None);
        var up = new TerminalKeyEvent(false, 'A', 0, 'A', ControlKeyStates.None);
        var input = new TerminalInput();

        // Sending repeating keypresses with DECARM disabled.
        input.SetInputMode(TerminalInput.Mode.AutoRepeat, false);
        Assert.AreEqual("A", input.HandleKey(down));
        Assert.AreEqual(string.Empty, input.HandleKey(down));
        Assert.AreEqual(string.Empty, input.HandleKey(down));
        Assert.AreEqual(string.Empty, input.HandleKey(up));

        // Sending repeating keypresses with DECARM enabled.
        input.SetInputMode(TerminalInput.Mode.AutoRepeat, true);
        Assert.AreEqual("A", input.HandleKey(down));
        Assert.AreEqual("A", input.HandleKey(down));
        Assert.AreEqual("A", input.HandleKey(down));
        Assert.AreEqual(string.Empty, input.HandleKey(up));
    }

    [TestMethod]
    public void SendC1ControlTest()
    {
        var input = new TerminalInput();

        // Sending keys with C1 control sequences.
        input.SetInputMode(TerminalInput.Mode.SendC1, true);

        TestKey("\u009bH", input, ControlKeyStates.None, VirtualKey.HOME);
        TestKey("\u008fP", input, ControlKeyStates.None, VirtualKey.F1);

        // Sending keys with 7-bit escape sequences.
        input.SetInputMode(TerminalInput.Mode.SendC1, false);

        TestKey("\u001b[H", input, ControlKeyStates.None, VirtualKey.HOME);
        TestKey("\u001bOP", input, ControlKeyStates.None, VirtualKey.F1);
    }
}
