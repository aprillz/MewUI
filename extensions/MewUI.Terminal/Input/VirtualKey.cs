// Ported from Windows Terminal src/terminal/input/terminalInput.cpp (MIT): the key codes its encoding tables index by.

namespace Aprillz.MewUI.Terminal.Input;

/// <summary>
/// Key codes for <see cref="TerminalInput"/>. The values are the Windows virtual-key codes, because the encoding
/// tables and the original tests are keyed by them; the view converts MewUI keys to these on every platform.
/// </summary>
internal static class VirtualKey
{
    public const int CANCEL = 0x03;
    public const int BACK = 0x08;
    public const int TAB = 0x09;
    public const int CLEAR = 0x0C;
    public const int RETURN = 0x0D;
    public const int SHIFT = 0x10;
    public const int CONTROL = 0x11;
    public const int MENU = 0x12;
    public const int PAUSE = 0x13;
    public const int CAPITAL = 0x14;
    public const int ESCAPE = 0x1B;
    public const int SPACE = 0x20;
    public const int PRIOR = 0x21;
    public const int NEXT = 0x22;
    public const int END = 0x23;
    public const int HOME = 0x24;
    public const int LEFT = 0x25;
    public const int UP = 0x26;
    public const int RIGHT = 0x27;
    public const int DOWN = 0x28;
    public const int SNAPSHOT = 0x2C;
    public const int INSERT = 0x2D;
    public const int DELETE = 0x2E;

    // '0' to '9' and 'A' to 'Z' are their ASCII codes.
    public const int KEY_0 = 0x30;
    public const int KEY_A = 0x41;

    public const int LWIN = 0x5B;
    public const int RWIN = 0x5C;
    public const int APPS = 0x5D;
    public const int NUMPAD0 = 0x60;
    public const int NUMPAD9 = 0x69;
    public const int MULTIPLY = 0x6A;
    public const int ADD = 0x6B;
    public const int SEPARATOR = 0x6C;
    public const int SUBTRACT = 0x6D;
    public const int DECIMAL = 0x6E;
    public const int DIVIDE = 0x6F;
    public const int F1 = 0x70;
    public const int F2 = 0x71;
    public const int F3 = 0x72;
    public const int F4 = 0x73;
    public const int F5 = 0x74;
    public const int F11 = 0x7A;
    public const int F12 = 0x7B;
    public const int F13 = 0x7C;
    public const int F20 = 0x83;
    public const int F24 = 0x87;
    public const int NUMLOCK = 0x90;
    public const int SCROLL = 0x91;
    public const int LSHIFT = 0xA0;
    public const int RSHIFT = 0xA1;
    public const int LCONTROL = 0xA2;
    public const int RCONTROL = 0xA3;
    public const int LMENU = 0xA4;
    public const int RMENU = 0xA5;

    // The US-layout punctuation keys.
    public const int OEM_1 = 0xBA;
    public const int OEM_PLUS = 0xBB;
    public const int OEM_COMMA = 0xBC;
    public const int OEM_MINUS = 0xBD;
    public const int OEM_PERIOD = 0xBE;
    public const int OEM_2 = 0xBF;
    public const int OEM_3 = 0xC0;
    public const int OEM_4 = 0xDB;
    public const int OEM_5 = 0xDC;
    public const int OEM_6 = 0xDD;
    public const int OEM_7 = 0xDE;
    public const int OEM_102 = 0xE2;

    // A key event that only carries a character, as input methods and paste send.
    public const int PACKET = 0xE7;
}
