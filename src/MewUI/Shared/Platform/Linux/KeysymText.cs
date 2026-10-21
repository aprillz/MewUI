using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Platform.Linux;

/// <summary>The text an X keysym stands for, shared by the X11 and framebuffer hosts.</summary>
internal static class KeysymText
{
    private const long FIRST_DEAD_KEYSYM = 0xFE50;
    private const long LAST_DEAD_KEYSYM = 0xFE93;
    // Keysyms 0x01000100 and up encode a Unicode code point directly.
    private const long UNICODE_KEYSYM_BASE = 0x01000000;

    private static bool _xkbCommonMissing;

    /// <summary>Whether a keysym is a dead key (XK_dead_*), which types nothing until the next key.</summary>
    public static bool IsDeadKeysym(long keysym) => keysym is >= FIRST_DEAD_KEYSYM and <= LAST_DEAD_KEYSYM;

    /// <summary>
    /// The text a keysym types, a dead key's spacing form, or null for a keysym that types nothing or a control
    /// character.
    /// </summary>
    public static string? ToText(long keysym)
    {
        uint codePoint = IsDeadKeysym(keysym) ? DeadKeySpacing(keysym) : CodePoint(keysym);
        bool isText = codePoint >= 0x20 && !(codePoint >= 0x7F && codePoint < 0xA0) && codePoint <= 0x10FFFF
            && !(codePoint >= 0xD800 && codePoint < 0xE000);
        return isText ? char.ConvertFromUtf32((int)codePoint) : null;
    }

    private static uint CodePoint(long keysym)
    {
        if (keysym is (>= 0x20 and <= 0x7E) or (>= 0xA0 and <= 0xFF))
        {
            return (uint)keysym;
        }

        if (keysym is >= UNICODE_KEYSYM_BASE + 0x100 and <= UNICODE_KEYSYM_BASE + 0x10FFFF)
        {
            return (uint)(keysym - UNICODE_KEYSYM_BASE);
        }

        // The older keysyms of other scripts (Cyrillic, Greek, Hebrew, ...) need xkbcommon's table.
        if (_xkbCommonMissing || keysym <= 0 || keysym > uint.MaxValue)
        {
            return 0;
        }

        try
        {
            return LibXkbCommon.xkb_keysym_to_utf32((uint)keysym);
        }
        catch (DllNotFoundException)
        {
            _xkbCommonMissing = true;
            return 0;
        }
        catch (EntryPointNotFoundException)
        {
            _xkbCommonMissing = true;
            return 0;
        }
    }

    // The spacing form of each accent a dead key adds, as the layout shows it on the key.
    private static uint DeadKeySpacing(long keysym) => keysym switch
    {
        0xFE50 => '`',
        0xFE51 => '´',
        0xFE52 => '^',
        0xFE53 => '~',
        0xFE54 => '¯',
        0xFE55 => '˘',
        0xFE56 => '˙',
        0xFE57 => '¨',
        0xFE58 => '˚',
        0xFE59 => '˝',
        0xFE5A => 'ˇ',
        0xFE5B => '¸',
        0xFE5C => '˛',
        0xFE5D => 'ͺ',
        0xFE5E => '゛',
        0xFE5F => '゜',
        _ => 0,
    };
}
