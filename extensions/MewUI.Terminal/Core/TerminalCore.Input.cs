// Ported from Windows Terminal src/cascadia/TerminalCore/Terminal.cpp (MIT): the key, character, mouse and focus input.

using Aprillz.MewUI.Terminal.Input;
using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Core;

internal sealed partial class TerminalCore
{
    // The last key event, so the character event that follows it can be matched to its key.
    private (int VirtualKey, int ScanCode)? _lastKeyEventCodes;

    /// <summary>Whether a key counts as typing (and scrolls back to the bottom), unlike a lone modifier.</summary>
    public static bool IsInputKey(int virtualKey) =>
        virtualKey != VirtualKey.CONTROL
        && virtualKey != VirtualKey.LCONTROL
        && virtualKey != VirtualKey.RCONTROL
        && virtualKey != VirtualKey.MENU
        && virtualKey != VirtualKey.LMENU
        && virtualKey != VirtualKey.RMENU
        && virtualKey != VirtualKey.SHIFT
        && virtualKey != VirtualKey.LSHIFT
        && virtualKey != VirtualKey.RSHIFT
        && virtualKey != VirtualKey.LWIN
        && virtualKey != VirtualKey.RWIN
        && virtualKey != VirtualKey.SNAPSHOT;

    /// <summary>
    /// Encodes a key press or release. Keys that produce a character return null, so the character arrives through
    /// <see cref="SendCharEvent"/> instead; Alt combinations and Tab are encoded here. The platform's texts for the key,
    /// when given, replace the US layout for it; <paramref name="altIsMeta"/> sends an Alt (Option) key that types a
    /// third-level character as ESC and its base text instead of the character.
    /// </summary>
    public string? SendKeyEvent(int virtualKey, int scanCode, ControlKeyStates states, bool keyDown, TerminalKeySymbols symbols = default, bool altIsMeta = false)
    {
        if (!symbols.IsReported)
        {
            return SendKeyEventCore(virtualKey, scanCode, states, keyDown);
        }

        if (keyDown && IsAltPressed(states))
        {
            // A dead key (no text of its own yet) and a third-level character (AltGr, Option) are typed by the
            // platform; Ctrl+Alt counts as AltGr while AltGr aliasing is on.
            bool isDeadKey = symbols.KeySymbol == null && symbols.BaseKeySymbol != null;
            bool typesThirdLevel = symbols.KeySymbol != null && symbols.KeySymbol != symbols.BaseKeySymbol;
            bool thirdLevelIsText = IsCtrlPressed(states) ? _altGrAliasing : !altIsMeta;
            if ((isDeadKey && !altIsMeta) || (typesThirdLevel && thirdLevelIsText))
            {
                _lastKeyEventCodes = (virtualKey, scanCode);
                return null;
            }
        }

        var layout = _terminalInput.KeyboardLayout;
        _terminalInput.KeyboardLayout = new EventKeyboardLayout(virtualKey, symbols, states, layout);
        try
        {
            return SendKeyEventCore(virtualKey, scanCode, states, keyDown);
        }
        finally
        {
            _terminalInput.KeyboardLayout = layout;
        }
    }

    private string? SendKeyEventCore(int virtualKey, int scanCode, ControlKeyStates states, bool keyDown)
    {
        // Only a key that leaves its character to the text input after it is remembered: views pass scan code 0, so
        // an Enter kept here would turn the next input method commit into another Enter.
        if (keyDown)
        {
            _lastKeyEventCodes = null;
            if (IsInputKey(virtualKey))
            {
                TrySnapOnInput();
            }
        }

        if (virtualKey == 0 || virtualKey >= 0xFF)
        {
            return null;
        }

        bool isAltOnlyPressed = IsAltPressed(states) && !IsCtrlPressed(states);

        // Without AltGr aliasing, Ctrl+Alt is a Ctrl and Alt combination and never produces an AltGr character.
        bool isSuppressedAltGrAlias = !_altGrAliasing && IsAltPressed(states) && IsCtrlPressed(states) && !IsAltGrPressed(states);
        uint character = isSuppressedAltGrAlias ? 0 : CharacterFromKeyEvent(virtualKey, states);

        // Control characters (Backspace, Escape, Enter) never arrive as MewUI text input, so their keys are encoded here.
        bool printable = character >= 0x20 && character != 0x7F;
        if (keyDown && !isAltOnlyPressed && virtualKey != VirtualKey.TAB && printable)
        {
            _lastKeyEventCodes = (virtualKey, scanCode);
            return null;
        }

        if (keyDown && virtualKey == VirtualKey.RETURN)
        {
            MarkCommandLineEnd();
        }

        var keyEvent = new TerminalKeyEvent(keyDown, virtualKey, scanCode, character <= 0xFFFF ? (char)character : '\0', states);
        return TerminalInput.HandleKey(keyEvent);
    }

    /// <summary>Encodes a 0-based viewport cell mouse event, clamped to the viewport.</summary>
    public string? SendMouseEvent(TilPoint viewportPos, TerminalMouseEvent button, ControlKeyStates states, int wheelDelta, TerminalInput.MouseButtonState state)
    {
        var clamped = GetMutableViewport().ToOrigin().Clamp(viewportPos);
        return TerminalInput.HandleMouse(clamped.X, clamped.Y, button, states, wheelDelta, state);
    }

    /// <summary>Encodes a typed character, using the key event that produced it when it is known.</summary>
    public string? SendCharEvent(char character, int scanCode, ControlKeyStates states)
    {
        int virtualKey = TakeVirtualKeyFromLastKeyEvent(scanCode);
        if (virtualKey == 0)
        {
            virtualKey = VirtualKeyFromCharacter(character);
        }

        if (virtualKey == VirtualKey.RETURN)
        {
            MarkCommandLineEnd();
        }

        var keyDown = new TerminalKeyEvent(true, virtualKey, scanCode, character, states);
        return TerminalInput.HandleKey(keyDown);
    }

    /// <summary>Marks where the output of the command line ended by Enter starts, for shell integration.</summary>
    private void MarkCommandLineEnd()
    {
        if (InAltBuffer)
        {
            return;
        }

        ClearQuickFixRequested?.Invoke();

        if (_autoMarkPrompts && _mainBuffer != null)
        {
            bool createdMark = _mainBuffer.StartOutput();
            if (createdMark)
            {
                _mainBuffer.ManuallyMarkRowAsPrompt(_mainBuffer.Cursor.Position.Y);
                NotifyScrollEvent();
            }
        }
    }

    /// <summary>Reports a focus change to the application when it asked for them (mode 1004).</summary>
    public string? FocusChanged(bool focused)
    {
        if (_focused == focused)
        {
            return null;
        }

        _focused = focused;
        return TerminalInput.HandleFocus(focused);
    }

    public bool IsTrackingMouseInput => TerminalInput.IsTrackingMouseInput;

    public bool ShouldSendAlternateScroll(TerminalMouseEvent button, int delta) => TerminalInput.ShouldSendAlternateScroll(button, delta);

    private static bool IsAltPressed(ControlKeyStates states) => (states & ControlKeyStates.AltPressed) != 0;

    private static bool IsCtrlPressed(ControlKeyStates states) => (states & ControlKeyStates.CtrlPressed) != 0;

    private static bool IsAltGrPressed(ControlKeyStates states) =>
        (states & (ControlKeyStates.RightAltPressed | ControlKeyStates.LeftCtrlPressed)) == (ControlKeyStates.RightAltPressed | ControlKeyStates.LeftCtrlPressed);

    /// <summary>The character the key produces with Shift, Ctrl and Alt as given, or 0.</summary>
    private uint CharacterFromKeyEvent(int virtualKey, ControlKeyStates states)
    {
        var keyState = states & (ControlKeyStates.ShiftPressed | ControlKeyStates.CtrlPressed | ControlKeyStates.AltPressed | ControlKeyStates.CapslockOn);
        uint character = _terminalInput.KeyboardLayout.GetCharacter(virtualKey, keyState);
        return character >= TerminalInput.INVALID_CODEPOINT ? 0 : character;
    }

    private int TakeVirtualKeyFromLastKeyEvent(int scanCode)
    {
        var codes = _lastKeyEventCodes ?? (0, 0);
        _lastKeyEventCodes = null;
        return codes.ScanCode == scanCode ? codes.VirtualKey : 0;
    }

    /// <summary>The key that types a character on a US layout, or 0, for characters that arrive without a key event.</summary>
    private static int VirtualKeyFromCharacter(char character)
    {
        if (character >= 'a' && character <= 'z')
        {
            return VirtualKey.KEY_A + (character - 'a');
        }

        if (character >= 'A' && character <= 'Z')
        {
            return VirtualKey.KEY_A + (character - 'A');
        }

        if (character >= '0' && character <= '9')
        {
            return VirtualKey.KEY_0 + (character - '0');
        }

        return character switch
        {
            '\r' => VirtualKey.RETURN,
            '\t' => VirtualKey.TAB,
            '\b' => VirtualKey.BACK,
            ' ' => VirtualKey.SPACE,
            '\u001b' => VirtualKey.ESCAPE,
            _ => 0,
        };
    }
}
