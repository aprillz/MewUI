using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;

namespace Aprillz.MewUI.Platform.Win32;

/// <summary>
/// The text each virtual key types in one keyboard layout, under Shift, Caps Lock and Ctrl+Alt, read once so that key
/// events never call the translation API: translating during a key event can disturb a pending dead key.
/// </summary>
internal sealed class Win32KeyboardLayout
{
    private const int VIRTUAL_KEYS = 256;
    // Columns of the table: bit 0 Shift, bit 1 Caps Lock, bit 2 Ctrl+Alt (the third level, AltGr).
    private const int STATES = 8;
    private const int SHIFT_STATE = 1;
    private const int CAPS_LOCK_STATE = 2;
    private const int CONTROL_ALT_STATE = 4;
    // ToUnicodeEx: leave the keyboard state (pending dead keys) unchanged. Windows 10 1607 and later.
    private const uint KEEP_KEYBOARD_STATE = 0x4;
    private const uint MAPVK_VK_TO_VSC = 0;

    // Layouts read so far by handle; key events arrive on the UI thread only.
    private static readonly Dictionary<nint, Win32KeyboardLayout> _layouts = new();

    private readonly Cell[] _cells = new Cell[VIRTUAL_KEYS * STATES];

    private Win32KeyboardLayout(nint handle)
    {
        Handle = handle;
    }

    /// <summary>The keyboard layout handle (HKL) this table was read from.</summary>
    public nint Handle { get; }

    /// <summary>What a key types in one state: its text, and whether it is a dead key (the text is then its spacing form).</summary>
    public readonly record struct Cell(string? Text, bool IsDeadKey)
    {
        public bool TypesSomething => Text != null;
    }

    /// <summary>The two layout texts of a key event.</summary>
    public readonly record struct Symbols(string? KeySymbol, string? BaseKeySymbol, bool TypesThirdLevel);

    /// <summary>
    /// The table of a layout, read now when no dead key is pending; null while one is, since the table would then hold
    /// the dead key's combinations.
    /// </summary>
    public static Win32KeyboardLayout? Get(nint handle)
    {
        if (handle == 0)
        {
            return null;
        }

        if (_layouts.TryGetValue(handle, out var layout))
        {
            return layout;
        }

        if (IsDeadKeyPending(handle))
        {
            return null;
        }

        layout = Read(handle);
        _layouts[handle] = layout;
        return layout;
    }

    /// <summary>Reads a layout's table ahead of key events, as when the window is created or the layout changes.</summary>
    public static void Prepare(nint handle) => _ = Get(handle);

    /// <summary>The cell of a key in a state, for tests and lookups.</summary>
    public Cell GetCell(int virtualKey, bool shift, bool capsLock, bool controlAlt)
    {
        if ((uint)virtualKey >= VIRTUAL_KEYS)
        {
            return default;
        }

        int state = (shift ? SHIFT_STATE : 0) | (capsLock ? CAPS_LOCK_STATE : 0) | (controlAlt ? CONTROL_ALT_STATE : 0);
        return _cells[virtualKey * STATES + state];
    }

    /// <summary>
    /// The key symbols of a press: the third level when Ctrl and Alt are both down and the key has one, else the
    /// Shift/Caps Lock text; a dead key gives no key symbol and its spacing form as the base symbol.
    /// </summary>
    public Symbols Lookup(int virtualKey, bool shift, bool capsLock, bool controlAlt)
    {
        var baseCell = GetCell(virtualKey, shift, capsLock, controlAlt: false);
        var thirdCell = controlAlt ? GetCell(virtualKey, shift, capsLock, controlAlt: true) : default;
        bool typesThirdLevel = thirdCell.TypesSomething;

        var symbolCell = typesThirdLevel ? thirdCell : baseCell;
        string? keySymbol = symbolCell.IsDeadKey ? null : symbolCell.Text;
        return new Symbols(keySymbol, baseCell.Text, typesThirdLevel);
    }

    /// <summary>Whether a dead key typed earlier is waiting for the next key in this thread's keyboard state.</summary>
    internal static unsafe bool IsDeadKeyPending(nint handle)
    {
        // With a dead key pending, Space types the dead key's spacing form instead of a space.
        byte* state = stackalloc byte[VIRTUAL_KEYS];
        new Span<byte>(state, VIRTUAL_KEYS).Clear();
        char* buffer = stackalloc char[8];
        uint scanCode = User32.MapVirtualKeyEx(VirtualKeys.VK_SPACE, MAPVK_VK_TO_VSC, handle);
        int length = User32.ToUnicodeEx(VirtualKeys.VK_SPACE, scanCode, state, buffer, 8, KEEP_KEYBOARD_STATE, handle);
        return !(length == 1 && buffer[0] == ' ');
    }

    private static unsafe Win32KeyboardLayout Read(nint handle)
    {
        var layout = new Win32KeyboardLayout(handle);
        byte* state = stackalloc byte[VIRTUAL_KEYS];
        char* buffer = stackalloc char[8];

        for (int column = 0; column < STATES; column++)
        {
            new Span<byte>(state, VIRTUAL_KEYS).Clear();
            if ((column & SHIFT_STATE) != 0)
            {
                state[VirtualKeys.VK_SHIFT] = 0x80;
            }

            if ((column & CAPS_LOCK_STATE) != 0)
            {
                state[VirtualKeys.VK_CAPITAL] = 0x01;
            }

            if ((column & CONTROL_ALT_STATE) != 0)
            {
                state[VirtualKeys.VK_CONTROL] = 0x80;
                state[VirtualKeys.VK_MENU] = 0x80;
            }

            for (int virtualKey = 0; virtualKey < VIRTUAL_KEYS; virtualKey++)
            {
                uint scanCode = User32.MapVirtualKeyEx((uint)virtualKey, MAPVK_VK_TO_VSC, handle);
                if (scanCode == 0)
                {
                    continue;
                }

                int length = User32.ToUnicodeEx((uint)virtualKey, scanCode, state, buffer, 8, KEEP_KEYBOARD_STATE, handle);
                bool isDeadKey = length < 0;
                string? text = length == 0 ? null : new string(buffer, 0, Math.Abs(length));
                if (text != null && IsControlText(text))
                {
                    text = null;
                }

                layout._cells[virtualKey * STATES + column] = new Cell(text, isDeadKey && text != null);
            }
        }

        return layout;
    }

    // Enter, Tab, Escape, Backspace and the Ctrl letters translate to control characters, which are not key text.
    private static bool IsControlText(string text) => text.Length == 1 && (text[0] < 0x20 || text[0] == 0x7F);
}
