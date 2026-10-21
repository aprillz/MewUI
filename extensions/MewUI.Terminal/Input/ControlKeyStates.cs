// Ported from Windows Terminal src/cascadia/TerminalCore/ControlKeyStates.hpp (MIT).

namespace Aprillz.MewUI.Terminal.Input;

/// <summary>The modifier and lock key state of a key or mouse event, with the values of the console key event flags.</summary>
[Flags]
internal enum ControlKeyStates : uint
{
    None = 0,
    RightAltPressed = 0x0001,
    LeftAltPressed = 0x0002,
    RightCtrlPressed = 0x0004,
    LeftCtrlPressed = 0x0008,
    ShiftPressed = 0x0010,
    NumlockOn = 0x0020,
    ScrolllockOn = 0x0040,
    CapslockOn = 0x0080,

    // The key is one of the extended (gray) keys: right Ctrl and Alt, the cursor block, keypad Enter and divide.
    EnhancedKey = 0x0100,
    RightWinPressed = 0x0200,
    LeftWinPressed = 0x0400,

    AltPressed = RightAltPressed | LeftAltPressed,
    CtrlPressed = RightCtrlPressed | LeftCtrlPressed,
}
