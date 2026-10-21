using Aprillz.MewUI.Native;

using NativeX11 = Aprillz.MewUI.Native.X11;

namespace Aprillz.MewUI.Platform.Linux.X11;

/// <summary>Xlib's translation of a key press, kept apart from <see cref="X11KeyMap"/>, which other Linux hosts share.</summary>
internal static class X11KeyTranslation
{
    // Core state masks (X.h): Control, Mod1 (Alt), Mod4 (Super) and Mod5 (AltGr, ISO_Level3_Shift).
    private const uint CONTROL_MASK = 1u << 2;
    private const uint MOD1_MASK = 1u << 3;
    private const uint MOD4_MASK = 1u << 6;
    private const uint MOD5_MASK = 1u << 7;

    /// <summary>
    /// The keysym of a press with its Shift, lock and layout group state applied, as Xlib translates it for text: the
    /// keypad's 1 with Num Lock on, ? for Shift with /.
    /// </summary>
    public static unsafe long TranslatedKeysym(XKeyEvent keyEvent)
    {
        byte* text = stackalloc byte[8];
        NativeX11.XLookupString(ref keyEvent, text, 8, out nint keysym, null);
        return keysym.ToInt64();
    }

    /// <summary>
    /// The layout texts of a press: with Shift, the locks and AltGr (the key symbol), and with Shift and the locks only
    /// (the base key symbol). Control, Alt and Super are left out of both.
    /// </summary>
    public static (string? KeySymbol, string? BaseKeySymbol) KeySymbols(XKeyEvent keyEvent)
    {
        var withThirdLevel = keyEvent;
        withThirdLevel.state = keyEvent.state & ~(CONTROL_MASK | MOD1_MASK | MOD4_MASK);
        long symbolKeysym = TranslatedKeysym(withThirdLevel);

        var withoutThirdLevel = withThirdLevel;
        withoutThirdLevel.state = withThirdLevel.state & ~MOD5_MASK;
        long baseKeysym = TranslatedKeysym(withoutThirdLevel);

        string? keySymbol = KeysymText.IsDeadKeysym(symbolKeysym) ? null : KeysymText.ToText(symbolKeysym);
        return (keySymbol, KeysymText.ToText(baseKeysym));
    }
}
