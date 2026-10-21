using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Platform.MacOS;

/// <summary>
/// The layout texts of a macOS key press. The key symbol is what AppKit put in the event, which already carries an input
/// method's translation; the base symbol is read from the keyboard layout under any input method, so a Korean or
/// Japanese input method still gives the Latin letter of the key.
/// </summary>
internal static partial class MacOSKeyboardLayout
{
    private const string CARBON = "/System/Library/Frameworks/Carbon.framework/Carbon";
    private const string CORE_FOUNDATION = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";

    // NSEventModifierFlags and the Carbon modifier bits UCKeyTranslate takes (shifted right by 8).
    private const ulong NS_CAPS_LOCK = 1UL << 16;
    private const ulong NS_SHIFT = 1UL << 17;
    private const ulong NS_CONTROL = 1UL << 18;
    private const ulong NS_OPTION = 1UL << 19;
    private const uint CARBON_SHIFT = 0x0200;
    private const uint CARBON_CAPS_LOCK = 0x0400;
    private const uint CARBON_OPTION = 0x0800;

    private const ushort UC_KEY_ACTION_DOWN = 0;
    private const uint UC_KEY_TRANSLATE_NO_DEAD_KEYS_MASK = 1;

    // The function keys' characters in an NSEvent (arrows, F1, Home) are in this private-use block.
    private const char FIRST_FUNCTION_KEY_CHARACTER = (char)0xF700;
    private const char LAST_FUNCTION_KEY_CHARACTER = (char)0xF8FF;

    private const uint K_CF_STRING_ENCODING_UTF8 = 0x08000100;

    private static nint _unicodeKeyLayoutDataProperty = -1;
    private static nint _inputSourceIdProperty = -1;

    /// <summary>The key symbol and base key symbol of a key press or release.</summary>
    /// <param name="keyCode">The event's virtual key code.</param>
    /// <param name="modifierFlags">The event's NSEventModifierFlags.</param>
    /// <param name="characters">The event's <c>characters</c>.</param>
    /// <param name="charactersIgnoringModifiers">The event's <c>charactersIgnoringModifiers</c>.</param>
    public static (string? KeySymbol, string? BaseKeySymbol) Symbols(int keyCode, ulong modifierFlags, string? characters, string? charactersIgnoringModifiers)
    {
        uint shiftAndLock = ((modifierFlags & NS_SHIFT) != 0 ? CARBON_SHIFT : 0) | ((modifierFlags & NS_CAPS_LOCK) != 0 ? CARBON_CAPS_LOCK : 0);
        string? baseKeySymbol = Translate(keyCode, shiftAndLock, asciiLayoutUnderInputMethod: true, noDeadKeys: true, out _);

        string? keySymbol;
        if ((modifierFlags & NS_CONTROL) == 0)
        {
            // A dead key leaves characters empty while the key itself has a character.
            bool isDeadKey = string.IsNullOrEmpty(characters) && !string.IsNullOrEmpty(charactersIgnoringModifiers);
            keySymbol = isDeadKey ? null : TextOrNull(characters);
        }
        else
        {
            // With Control the event carries a control character, so the layout is read without it.
            uint withOption = shiftAndLock | ((modifierFlags & NS_OPTION) != 0 ? CARBON_OPTION : 0);
            keySymbol = Translate(keyCode, withOption, asciiLayoutUnderInputMethod: false, noDeadKeys: false, out bool isDeadKey);
            keySymbol = isDeadKey ? null : keySymbol;
        }

        return (keySymbol, baseKeySymbol);
    }

    /// <summary>
    /// The text of a key in a keyboard layout under Carbon modifier bits: the selected layout, or the one under the
    /// selected input method; with <paramref name="asciiLayoutUnderInputMethod"/>, the ASCII-capable layout when an input
    /// method is selected.
    /// </summary>
    private static unsafe string? Translate(int keyCode, uint carbonModifiers, bool asciiLayoutUnderInputMethod, bool noDeadKeys, out bool isDeadKey)
    {
        isDeadKey = false;
        nint property = UnicodeKeyLayoutDataProperty();
        if (property == 0)
        {
            return null;
        }

        nint selected = TISCopyCurrentKeyboardInputSource();
        nint source;
        if (selected != 0 && TISGetInputSourceProperty(selected, property) != 0)
        {
            source = selected;
        }
        else
        {
            // An input method has no layout of its own; it types over the layout under it.
            if (selected != 0)
            {
                CFRelease(selected);
            }

            source = asciiLayoutUnderInputMethod ? TISCopyCurrentASCIICapableKeyboardLayoutInputSource() : TISCopyCurrentKeyboardLayoutInputSource();
        }

        if (source == 0)
        {
            return null;
        }

        try
        {
            nint data = TISGetInputSourceProperty(source, property);
            if (data == 0)
            {
                return null;
            }

            char* buffer = stackalloc char[8];
            uint deadKeyState = 0;
            nuint length = 0;
            int status = UCKeyTranslate(CFDataGetBytePtr(data), (ushort)keyCode, UC_KEY_ACTION_DOWN, (carbonModifiers >> 8) & 0xFF,
                LMGetKbdType(), noDeadKeys ? UC_KEY_TRANSLATE_NO_DEAD_KEYS_MASK : 0, &deadKeyState, 8, &length, buffer);
            if (status != 0)
            {
                return null;
            }

            isDeadKey = length == 0 && deadKeyState != 0;
            return TextOrNull(new string(buffer, 0, (int)length));
        }
        finally
        {
            CFRelease(source);
        }
    }

    /// <summary>
    /// Whether the selected input source is an input method that composes (Korean, Japanese kana) rather than a keyboard
    /// layout. A Japanese input method's alphanumeric mode types like a layout and does not count.
    /// </summary>
    public static unsafe bool IsComposingInputMethodSelected()
    {
        nint layoutProperty = UnicodeKeyLayoutDataProperty();
        nint idProperty = InputSourceIdProperty();
        if (layoutProperty == 0)
        {
            return false;
        }

        nint selected = TISCopyCurrentKeyboardInputSource();
        if (selected == 0)
        {
            return false;
        }

        try
        {
            if (TISGetInputSourceProperty(selected, layoutProperty) != 0)
            {
                return false;
            }

            nint id = idProperty != 0 ? TISGetInputSourceProperty(selected, idProperty) : 0;
            byte* buffer = stackalloc byte[256];
            string? sourceId = id != 0 && CFStringGetCString(id, buffer, 256, K_CF_STRING_ENCODING_UTF8) != 0
                ? Marshal.PtrToStringUTF8((nint)buffer)
                : null;
            return sourceId == null || !sourceId.EndsWith(".Roman", StringComparison.Ordinal);
        }
        finally
        {
            CFRelease(selected);
        }
    }

    private static string? TextOrNull(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        char first = text[0];
        bool isControl = text.Length == 1 && (first < 0x20 || first == 0x7F);
        bool isFunctionKey = first >= FIRST_FUNCTION_KEY_CHARACTER && first <= LAST_FUNCTION_KEY_CHARACTER;
        return isControl || isFunctionKey ? null : text;
    }

    private static nint UnicodeKeyLayoutDataProperty() => ReadProperty(ref _unicodeKeyLayoutDataProperty, "kTISPropertyUnicodeKeyLayoutData");

    private static nint InputSourceIdProperty() => ReadProperty(ref _inputSourceIdProperty, "kTISPropertyInputSourceID");

    // The TIS property keys are exported CFStringRefs, read once from the framework.
    private static nint ReadProperty(ref nint cached, string name)
    {
        if (cached == -1)
        {
            cached = 0;
            if (NativeLibrary.TryLoad(CARBON, out nint carbon) && NativeLibrary.TryGetExport(carbon, name, out nint address))
            {
                cached = Marshal.ReadIntPtr(address);
            }
        }

        return cached;
    }

    [LibraryImport(CARBON)]
    private static partial nint TISCopyCurrentKeyboardInputSource();

    [LibraryImport(CARBON)]
    private static partial nint TISCopyCurrentKeyboardLayoutInputSource();

    [LibraryImport(CARBON)]
    private static partial nint TISCopyCurrentASCIICapableKeyboardLayoutInputSource();

    [LibraryImport(CARBON)]
    private static partial nint TISGetInputSourceProperty(nint inputSource, nint propertyKey);

    [LibraryImport(CARBON)]
    private static partial uint LMGetKbdType();

    [LibraryImport(CARBON)]
    private static unsafe partial int UCKeyTranslate(nint keyLayout, ushort virtualKeyCode, ushort keyAction, uint modifierKeyState,
        uint keyboardType, uint keyTranslateOptions, uint* deadKeyState, nuint maxStringLength, nuint* actualStringLength, char* unicodeString);

    [LibraryImport(CORE_FOUNDATION)]
    private static partial nint CFDataGetBytePtr(nint data);

    [LibraryImport(CORE_FOUNDATION)]
    private static partial void CFRelease(nint value);

    [LibraryImport(CORE_FOUNDATION)]
    private static unsafe partial byte CFStringGetCString(nint value, byte* buffer, nint bufferSize, uint encoding);
}
