using System.Runtime.InteropServices;
using System.Text;

using Aprillz.MewUI.Input;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform.Linux.X11;

namespace Aprillz.MewUI.Platform.Linux.Framebuffer;

/// <summary>One key transition resolved against the keyboard layout.</summary>
internal readonly record struct FramebufferKeyStroke(Key Key, int PlatformKey, ModifierKeys Modifiers, string? Text, bool Repeats);

/// <summary>
/// Turns evdev key codes into keys, modifiers and text through xkbcommon. Without xkbcommon keys still resolve
/// by US position and modifiers are tracked here, but no text is produced.
/// </summary>
internal sealed unsafe class FramebufferKeyboard : IDisposable
{
    private const int UTF8_BUFFER_SIZE = 64;

    // evdev key codes (linux/input-event-codes.h) for the fallback path.
    private const uint KEY_LEFTCTRL = 29;
    private const uint KEY_LEFTSHIFT = 42;
    private const uint KEY_RIGHTSHIFT = 54;
    private const uint KEY_LEFTALT = 56;
    private const uint KEY_RIGHTCTRL = 97;
    private const uint KEY_RIGHTALT = 100;
    private const uint KEY_LEFTMETA = 125;
    private const uint KEY_RIGHTMETA = 126;

    private static readonly Func<nint, int, int, long> _keysymOfLayout = KeysymOfLayout;

    private readonly nint _context;
    private readonly nint _keymap;
    private readonly nint _state;
    private ModifierKeys _fallbackModifiers;
    private bool _disposed;

    private FramebufferKeyboard(nint context, nint keymap, nint state, string description)
    {
        _context = context;
        _keymap = keymap;
        _state = state;
        Description = description;
    }

    /// <summary>One line naming the layout source, for the startup log.</summary>
    public string Description { get; }

    public static FramebufferKeyboard Create(string? layout)
    {
        nint context;
        try
        {
            context = LibXkbCommon.xkb_context_new(0);
        }
        catch (DllNotFoundException)
        {
            return new FramebufferKeyboard(0, 0, 0, "keyboard: libxkbcommon missing, US positions only, no text input");
        }

        if (context == 0)
        {
            return new FramebufferKeyboard(0, 0, 0, "keyboard: xkb_context_new failed, US positions only, no text input");
        }

        nint keymap;
        if (string.IsNullOrEmpty(layout))
        {
            keymap = LibXkbCommon.xkb_keymap_new_from_names(context, null, 0);
        }
        else
        {
            nint layoutText = Marshal.StringToCoTaskMemUTF8(layout);
            try
            {
                var names = new XkbRuleNames { Layout = (byte*)layoutText };
                keymap = LibXkbCommon.xkb_keymap_new_from_names(context, &names, 0);
            }
            finally
            {
                Marshal.FreeCoTaskMem(layoutText);
            }
        }

        nint state = keymap != 0 ? LibXkbCommon.xkb_state_new(keymap) : 0;
        if (state == 0)
        {
            if (keymap != 0)
            {
                LibXkbCommon.xkb_keymap_unref(keymap);
            }

            LibXkbCommon.xkb_context_unref(context);
            return new FramebufferKeyboard(0, 0, 0, $"keyboard: no xkb keymap for layout '{layout ?? "default"}', US positions only, no text input");
        }

        return new FramebufferKeyboard(context, keymap, state, $"keyboard: xkb layout {layout ?? Environment.GetEnvironmentVariable("XKB_DEFAULT_LAYOUT") ?? "default"}");
    }

    /// <summary>The modifiers held now, for pointer events.</summary>
    public ModifierKeys CurrentModifiers => _state == 0 ? _fallbackModifiers : ReadModifiers();

    /// <summary>Resolves an auto-repeat of a held key without changing the modifier state.</summary>
    public FramebufferKeyStroke Repeat(uint evdevCode)
    {
        if (_state == 0)
        {
            long fallbackKeysym = FramebufferKeyCodes.KeysymOfSpecialKey(evdevCode);
            var fallbackKey = fallbackKeysym != 0
                ? X11KeyMap.FromKeysym(fallbackKeysym)
                : X11KeyMap.FromPosition((int)(evdevCode + LibXkbCommon.EVDEV_OFFSET));
            return new FramebufferKeyStroke(fallbackKey, (int)fallbackKeysym, _fallbackModifiers, Text: null, Repeats: true);
        }

        uint keycode = evdevCode + LibXkbCommon.EVDEV_OFFSET;
        uint layout = LibXkbCommon.xkb_state_key_get_layout(_state, keycode);
        long keysym = KeysymAt(_keymap, keycode, layout);
        var key = X11KeyMap.Resolve(keysym, (int)keycode, _keymap, _keysymOfLayout);
        return new FramebufferKeyStroke(key, (int)keysym, ReadModifiers(), ReadText(keycode), Repeats: true);
    }

    /// <summary>Resolves a key transition and advances the modifier state. Text is reported for presses only.</summary>
    public FramebufferKeyStroke Process(uint evdevCode, bool pressed)
    {
        if (_state == 0)
        {
            return ProcessWithoutXkb(evdevCode, pressed);
        }

        uint keycode = evdevCode + LibXkbCommon.EVDEV_OFFSET;

        // The event carries the modifiers held before it, as X11 key events do.
        var modifiers = ReadModifiers();
        uint layout = LibXkbCommon.xkb_state_key_get_layout(_state, keycode);
        long keysym = KeysymAt(_keymap, keycode, layout);
        var key = X11KeyMap.Resolve(keysym, (int)keycode, _keymap, _keysymOfLayout);

        string? text = pressed ? ReadText(keycode) : null;
        LibXkbCommon.xkb_state_update_key(_state, keycode, pressed ? LibXkbCommon.XKB_KEY_DOWN : LibXkbCommon.XKB_KEY_UP);
        bool repeats = LibXkbCommon.xkb_keymap_key_repeats(_keymap, keycode) != 0;
        return new FramebufferKeyStroke(key, (int)keysym, modifiers, text, repeats);
    }

    private string? ReadText(uint keycode)
    {
        byte* buffer = stackalloc byte[UTF8_BUFFER_SIZE];
        int length = LibXkbCommon.xkb_state_key_get_utf8(_state, keycode, buffer, UTF8_BUFFER_SIZE);
        return length > 0 ? Encoding.UTF8.GetString(buffer, Math.Min(length, UTF8_BUFFER_SIZE - 1)) : null;
    }

    private ModifierKeys ReadModifiers()
    {
        var modifiers = ModifierKeys.None;
        if (IsActive(LibXkbCommon.XKB_MOD_NAME_SHIFT))
        {
            modifiers |= ModifierKeys.Shift;
        }

        if (IsActive(LibXkbCommon.XKB_MOD_NAME_CTRL))
        {
            modifiers |= ModifierKeys.Control;
        }

        if (IsActive(LibXkbCommon.XKB_MOD_NAME_ALT))
        {
            modifiers |= ModifierKeys.Alt;
        }

        if (IsActive(LibXkbCommon.XKB_MOD_NAME_LOGO))
        {
            modifiers |= ModifierKeys.Meta;
        }

        return modifiers;
    }

    private bool IsActive(string modifierName)
        => LibXkbCommon.xkb_state_mod_name_is_active(_state, modifierName, LibXkbCommon.XKB_STATE_MODS_EFFECTIVE) > 0;

    private static long KeysymOfLayout(nint keymap, int keycode, int layout)
        => KeysymAt(keymap, (uint)keycode, (uint)layout);

    private static long KeysymAt(nint keymap, uint keycode, uint layout)
    {
        uint* symbols = null;
        int count = LibXkbCommon.xkb_keymap_key_get_syms_by_level(keymap, keycode, layout, 0, &symbols);
        return count > 0 && symbols != null ? symbols[0] : 0;
    }

    private FramebufferKeyStroke ProcessWithoutXkb(uint evdevCode, bool pressed)
    {
        var modifiers = _fallbackModifiers;
        var modifier = evdevCode switch
        {
            KEY_LEFTSHIFT or KEY_RIGHTSHIFT => ModifierKeys.Shift,
            KEY_LEFTCTRL or KEY_RIGHTCTRL => ModifierKeys.Control,
            KEY_LEFTALT or KEY_RIGHTALT => ModifierKeys.Alt,
            KEY_LEFTMETA or KEY_RIGHTMETA => ModifierKeys.Meta,
            _ => ModifierKeys.None,
        };

        if (modifier != ModifierKeys.None)
        {
            _fallbackModifiers = pressed ? _fallbackModifiers | modifier : _fallbackModifiers & ~modifier;
        }

        long keysym = FramebufferKeyCodes.KeysymOfSpecialKey(evdevCode);
        var key = keysym != 0
            ? X11KeyMap.FromKeysym(keysym)
            : X11KeyMap.FromPosition((int)(evdevCode + LibXkbCommon.EVDEV_OFFSET));
        return new FramebufferKeyStroke(key, (int)keysym, modifiers, Text: null, Repeats: modifier == ModifierKeys.None);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_state != 0)
        {
            LibXkbCommon.xkb_state_unref(_state);
            LibXkbCommon.xkb_keymap_unref(_keymap);
            LibXkbCommon.xkb_context_unref(_context);
        }
    }
}

/// <summary>Keysyms of the non-character keys by evdev code, for keyboards resolved without xkbcommon.</summary>
internal static class FramebufferKeyCodes
{
    public static long KeysymOfSpecialKey(uint evdevCode) => evdevCode switch
    {
        1 => 0xFF1B,              // Escape
        14 => 0xFF08,             // BackSpace
        15 => 0xFF09,             // Tab
        28 or 96 => 0xFF0D,       // Return, KP_Enter
        57 => 0x20,               // space
        >= 59 and <= 68 => 0xFFBE + (evdevCode - 59), // F1..F10
        87 => 0xFFC8,             // F11
        88 => 0xFFC9,             // F12
        102 => 0xFF50,            // Home
        103 => 0xFF52,            // Up
        104 => 0xFF55,            // Prior
        105 => 0xFF51,            // Left
        106 => 0xFF53,            // Right
        107 => 0xFF57,            // End
        108 => 0xFF54,            // Down
        109 => 0xFF56,            // Next
        110 => 0xFF63,            // Insert
        111 => 0xFFFF,            // Delete
        127 => 0xFF67,            // Menu
        _ => 0,
    };
}
