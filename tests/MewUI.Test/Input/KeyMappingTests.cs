using Aprillz.MewUI;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform.Linux.X11;
using Aprillz.MewUI.Platform.MacOS;
using Aprillz.MewUI.Platform.Win32;

namespace MewUI.Test.Input;

/// <summary>
/// A key is named for its role on a US QWERTY keyboard on every platform: punctuation keys have names, and a layout
/// that types no Latin letter at a position falls back to the key a US layout has there.
/// </summary>
[TestClass]
public sealed class KeyMappingTests
{
    private const int KEYCODE_Z = 52;
    private const int KEYCODE_A = 38;
    private const int KEYCODE_MINUS = 20;
    private const int KEYCODE_LEFT_SHIFT = 50;
    private const int KEYCODE_ISO_EXTRA = 94;

    private const int MAC_KEYCODE_A = 0x00;
    private const int MAC_KEYCODE_M = 0x2E;
    private const int MAC_KEYCODE_SEMICOLON = 0x29;
    private const int MAC_KEYCODE_MINUS = 0x1B;
    private const int MAC_KEYCODE_SLASH = 0x2C;
    private const int MAC_KEYCODE_2 = 0x13;
    private const int MAC_KEYCODE_UNLISTED = 0x7F;

    private const long KEYSYM_CYRILLIC_YA = 0x6D1;
    private const long KEYSYM_SSHARP = 0xDF;
    private const long KEYSYM_SHIFT_LEFT = 0xFFE1;

    [TestMethod]
    [DataRow('.', Key.Period)]
    [DataRow('>', Key.Period)]
    [DataRow('/', Key.Slash)]
    [DataRow('?', Key.Slash)]
    [DataRow('-', Key.Minus)]
    [DataRow('_', Key.Minus)]
    [DataRow('=', Key.Equal)]
    [DataRow('+', Key.Equal)]
    [DataRow('`', Key.Backquote)]
    [DataRow('[', Key.BracketLeft)]
    [DataRow(']', Key.BracketRight)]
    [DataRow('\\', Key.Backslash)]
    [DataRow(';', Key.Semicolon)]
    [DataRow('\'', Key.Quote)]
    [DataRow(',', Key.Comma)]
    [DataRow('a', Key.A)]
    [DataRow('Z', Key.Z)]
    [DataRow('7', Key.D7)]
    [DataRow('!', Key.None)]
    [DataRow('я', Key.None)]
    public void ACharacterNamesTheKeyThatTypesItOnAUsLayout(char character, Key expected)
        => Assert.AreEqual(expected, KeyCharacters.FromCharacter(character));

    [TestMethod]
    [DataRow(0xC0, Key.Backquote)]
    [DataRow(0xBD, Key.Minus)]
    [DataRow(0xBB, Key.Equal)]
    [DataRow(0xDB, Key.BracketLeft)]
    [DataRow(0xDD, Key.BracketRight)]
    [DataRow(0xDC, Key.Backslash)]
    [DataRow(0xBA, Key.Semicolon)]
    [DataRow(0xDE, Key.Quote)]
    [DataRow(0xBC, Key.Comma)]
    [DataRow(0xBE, Key.Period)]
    [DataRow(0xBF, Key.Slash)]
    [DataRow(0xE2, Key.IntlBackslash)]
    [DataRow(0x5D, Key.ContextMenu)]
    [DataRow(0x5A, Key.Z)]
    public void AWin32VirtualKeyMapsToItsKey(int virtualKey, Key expected)
        => Assert.AreEqual(expected, Win32WindowBackend.MapKey(virtualKey));

    [TestMethod]
    [DataRow(0x2EL, Key.Period)]
    [DataRow(0x3EL, Key.Period)]
    [DataRow(0x2FL, Key.Slash)]
    [DataRow(0x2DL, Key.Minus)]
    [DataRow(0x2BL, Key.Equal)]
    [DataRow(0x60L, Key.Backquote)]
    [DataRow(0x7AL, Key.Z)]
    [DataRow(0xFF67L, Key.ContextMenu)]
    [DataRow(KEYSYM_CYRILLIC_YA, Key.None)]
    [DataRow(0xFFB1L, Key.NumPad1)]
    [DataRow(0xFF9CL, Key.End)]
    [DataRow(0xFFAEL, Key.Decimal)]
    [DataRow(0xFFACL, Key.Decimal)]
    [DataRow(0xFF9DL, Key.None)]
    public void AnX11KeysymMapsToItsKey(long keysym, Key expected)
        => Assert.AreEqual(expected, X11KeyMap.FromKeysym(keysym));

    [TestMethod]
    [DataRow(0xFF9CL, true)]
    [DataRow(0xFFB1L, true)]
    [DataRow(0xFF8DL, true)]
    [DataRow(0xFF57L, false)]
    [DataRow(0x31L, false)]
    public void KeypadKeysymsAreTheOnesNumLockChanges(long keysym, bool expected)
        => Assert.AreEqual(expected, X11KeyMap.IsKeypadKeysym(keysym));

    [TestMethod]
    public void AKeyTypingNoLatinLetterIsNamedByItsPosition()
    {
        var key = X11KeyMap.Resolve(KEYSYM_CYRILLIC_YA, KEYCODE_Z, 0, static (_, _, _) => KEYSYM_CYRILLIC_YA);

        Assert.AreEqual(Key.Z, key, "a Cyrillic layout's key at the Z position was not the Z key");
    }

    [TestMethod]
    public void AnotherLayoutGroupNamesTheKeyBeforeItsPositionDoes()
    {
        // A second group (AZERTY) types q where the US layout has a.
        var key = X11KeyMap.Resolve(KEYSYM_CYRILLIC_YA, KEYCODE_A, 0, static (_, _, group) => group == 1 ? 'q' : KEYSYM_CYRILLIC_YA);

        Assert.AreEqual(Key.Q, key);
    }

    [TestMethod]
    public void AKeysymTheLayoutNamesWinsOverThePosition()
    {
        // AZERTY: the key at the US a position types q.
        Assert.AreEqual(Key.Q, X11KeyMap.Resolve('q', KEYCODE_A, 0, static (_, _, _) => 'q'));
    }

    [TestMethod]
    public void ALatinLetterWithoutAKeyFallsBackToItsPosition()
    {
        // German: the key right of 0 types sharp s, which no key is named for.
        Assert.AreEqual(Key.Minus, X11KeyMap.Resolve(KEYSYM_SSHARP, KEYCODE_MINUS, 0, static (_, _, _) => KEYSYM_SSHARP));
    }

    [TestMethod]
    public void TheIsoExtraKeyIsItsOwnKeyWhateverItTypes()
    {
        Assert.AreEqual(Key.IntlBackslash, X11KeyMap.Resolve('<', KEYCODE_ISO_EXTRA, 0, static (_, _, _) => '<'));
    }

    [TestMethod]
    public void AModifierIsNotNamedByItsPosition()
    {
        Assert.AreEqual(Key.None, X11KeyMap.Resolve(KEYSYM_SHIFT_LEFT, KEYCODE_LEFT_SHIFT, 0, static (_, _, _) => KEYSYM_SHIFT_LEFT));
    }

    [TestMethod]
    public void APunctuationShortcutShowsItsCharacter()
    {
        Assert.AreEqual("Ctrl+.", new KeyGesture(Key.Period, ModifierKeys.Control).ToDisplayString());
        Assert.AreEqual("Ctrl+Shift+-", new KeyGesture(Key.Minus, ModifierKeys.Control | ModifierKeys.Shift).ToDisplayString());
    }

    [TestMethod]
    [DataRow(0x32, Key.Backquote)]
    [DataRow(0x1B, Key.Minus)]
    [DataRow(0x18, Key.Equal)]
    [DataRow(0x21, Key.BracketLeft)]
    [DataRow(0x1E, Key.BracketRight)]
    [DataRow(0x2A, Key.Backslash)]
    [DataRow(0x29, Key.Semicolon)]
    [DataRow(0x27, Key.Quote)]
    [DataRow(0x2B, Key.Comma)]
    [DataRow(0x2F, Key.Period)]
    [DataRow(0x2C, Key.Slash)]
    [DataRow(0x0A, Key.IntlBackslash)]
    [DataRow(0x6E, Key.ContextMenu)]
    [DataRow(0x06, Key.Z)]
    [DataRow(0x1A, Key.D7)]
    [DataRow(0x7F, Key.None)]
    public void AMacKeyCodeMapsToTheKeyAtItsPosition(int keyCode, Key expected)
        => Assert.AreEqual(expected, MacOSKeyMap.FromKeyCode(keyCode));

    [TestMethod]
    [DataRow(MAC_KEYCODE_A, "a", Key.A, "US: the key types its own letter")]
    [DataRow(MAC_KEYCODE_A, "A", Key.A, "Shift does not change the key")]
    [DataRow(MAC_KEYCODE_A, "q", Key.Q, "AZERTY: the key at the US a position types q")]
    [DataRow(MAC_KEYCODE_M, ",", Key.Comma, "AZERTY: the key at the US m position types a comma")]
    [DataRow(MAC_KEYCODE_SEMICOLON, "m", Key.M, "AZERTY: the key at the US semicolon position types m")]
    [DataRow(MAC_KEYCODE_A, "ф", Key.A, "Cyrillic: no Latin letter, so the position names the key")]
    [DataRow(MAC_KEYCODE_A, "", Key.A, "a dead key types nothing yet")]
    [DataRow(MAC_KEYCODE_A, null, Key.A, "no characters reported")]
    [DataRow(MAC_KEYCODE_MINUS, "ß", Key.Minus, "German: a Latin letter no key is named for")]
    [DataRow(MAC_KEYCODE_SLASH, "-", Key.Slash, "punctuation at a punctuation position stays with the position")]
    [DataRow(MAC_KEYCODE_2, "é", Key.D2, "the digit row is always named by position")]
    [DataRow(MAC_KEYCODE_2, "@", Key.D2, "a shifted digit is still its digit key")]
    [DataRow(MAC_KEYCODE_UNLISTED, "x", Key.X, "a key code outside the table falls back to what it types")]
    public void AMacKeyIsNamedByItsLatinLetterAndOtherwiseByItsPosition(int keyCode, string? characters, Key expected, string because)
        => Assert.AreEqual(expected, MacOSKeyMap.Resolve(keyCode, characters), because);
}
