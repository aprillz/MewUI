using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

// libxkbcommon: turns evdev key codes into keysyms and text with the configured layout.
internal static unsafe partial class LibXkbCommon
{
    private const string LIBRARY_NAME = "libxkbcommon.so.0";

    public const int XKB_KEY_UP = 0;
    public const int XKB_KEY_DOWN = 1;
    public const int XKB_STATE_MODS_EFFECTIVE = 1 << 3;
    public const string XKB_MOD_NAME_SHIFT = "Shift";
    public const string XKB_MOD_NAME_CTRL = "Control";
    public const string XKB_MOD_NAME_ALT = "Mod1";
    public const string XKB_MOD_NAME_LOGO = "Mod4";

    // evdev key codes are offset by 8 in the xkb keycode space.
    public const uint EVDEV_OFFSET = 8;

    [LibraryImport(LIBRARY_NAME)]
    public static partial nint xkb_context_new(int flags);

    [LibraryImport(LIBRARY_NAME)]
    public static partial void xkb_context_unref(nint context);

    [LibraryImport(LIBRARY_NAME)]
    public static partial nint xkb_keymap_new_from_names(nint context, XkbRuleNames* names, int flags);

    [LibraryImport(LIBRARY_NAME)]
    public static partial void xkb_keymap_unref(nint keymap);

    [LibraryImport(LIBRARY_NAME)]
    public static partial int xkb_keymap_key_repeats(nint keymap, uint key);

    [LibraryImport(LIBRARY_NAME)]
    public static partial nint xkb_state_new(nint keymap);

    [LibraryImport(LIBRARY_NAME)]
    public static partial void xkb_state_unref(nint state);

    [LibraryImport(LIBRARY_NAME)]
    public static partial int xkb_state_update_key(nint state, uint key, int direction);

    [LibraryImport(LIBRARY_NAME)]
    public static partial uint xkb_state_key_get_one_sym(nint state, uint key);

    [LibraryImport(LIBRARY_NAME)]
    public static partial int xkb_state_key_get_utf8(nint state, uint key, byte* buffer, nuint size);

    [LibraryImport(LIBRARY_NAME, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int xkb_state_mod_name_is_active(nint state, string name, int type);

    [LibraryImport(LIBRARY_NAME)]
    public static partial uint xkb_state_key_get_layout(nint state, uint key);

    [LibraryImport(LIBRARY_NAME)]
    public static partial int xkb_keymap_key_get_syms_by_level(nint keymap, uint key, uint layout, uint level, uint** symsOut);
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct XkbRuleNames
{
    public byte* Rules;
    public byte* Model;
    public byte* Layout;
    public byte* Variant;
    public byte* Options;
}
