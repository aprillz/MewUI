using Aprillz.MewUI.Native;

using NativeX11 = Aprillz.MewUI.Native.X11;

namespace Aprillz.MewUI.Platform.Linux.X11;

/// <summary>Xlib's translation of a key press, kept apart from <see cref="X11KeyMap"/>, which other Linux hosts share.</summary>
internal static class X11KeyTranslation
{
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
}
