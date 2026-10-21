using Aprillz.MewUI;
using Aprillz.MewUI.Terminal;
using Aprillz.MewUI.Terminal.Core;
using Aprillz.MewUI.Terminal.Input;
using Aprillz.MewUI.Terminal.Types;

namespace MewUI.Terminal.Test.Input;

/// <summary>
/// The path a key takes in the terminal view: the key event first, and when the terminal leaves it unhandled, the text
/// input the platform delivers for it. Platforms deliver no control characters as text.
/// </summary>
[TestClass]
public sealed class KeyRoutingTests
{
    private static TerminalCore CreateTerminal()
    {
        var term = new TerminalCore(true);
        term.Create(new TilSize(80, 24), 0, new Renderer());
        return term;
    }

    private static string Type(TerminalCore term, Key key, ModifierKeys modifiers, string? text)
    {
        var states = TerminalKeyMap.ToControlKeyStates(modifiers);
        int virtualKey = TerminalKeyMap.ToVirtualKey(key);
        string? output = term.SendKeyEvent(virtualKey, 0, states, keyDown: true);
        if (output != null)
        {
            return output;
        }

        if (string.IsNullOrEmpty(text))
        {
            return "<unhandled without text>";
        }

        return term.SendCharEvent(text[0], 0, ControlKeyStates.None) ?? "<char unhandled>";
    }

    [TestMethod]
    [DataRow(Key.Backspace, ModifierKeys.None, null, "\u007f")]
    [DataRow(Key.Backspace, ModifierKeys.Control, null, "\b")]
    [DataRow(Key.Backspace, ModifierKeys.Alt, null, "\u001b\u007f")]
    [DataRow(Key.Escape, ModifierKeys.None, null, "\u001b")]
    [DataRow(Key.Enter, ModifierKeys.None, null, "\r")]
    [DataRow(Key.Enter, ModifierKeys.Alt, null, "\u001b\r")]
    [DataRow(Key.Enter, ModifierKeys.Control, null, "\n")]
    [DataRow(Key.Tab, ModifierKeys.None, null, "\t")]
    [DataRow(Key.Tab, ModifierKeys.Shift, null, "\u001b[Z")]
    [DataRow(Key.Up, ModifierKeys.None, null, "\u001b[A")]
    [DataRow(Key.Down, ModifierKeys.None, null, "\u001b[B")]
    [DataRow(Key.Right, ModifierKeys.None, null, "\u001b[C")]
    [DataRow(Key.Left, ModifierKeys.None, null, "\u001b[D")]
    [DataRow(Key.Up, ModifierKeys.Shift, null, "\u001b[1;2A")]
    [DataRow(Key.Left, ModifierKeys.Control, null, "\u001b[1;5D")]
    [DataRow(Key.Right, ModifierKeys.Alt, null, "\u001b[1;3C")]
    [DataRow(Key.Home, ModifierKeys.None, null, "\u001b[H")]
    [DataRow(Key.End, ModifierKeys.None, null, "\u001b[F")]
    [DataRow(Key.PageUp, ModifierKeys.None, null, "\u001b[5~")]
    [DataRow(Key.PageDown, ModifierKeys.None, null, "\u001b[6~")]
    [DataRow(Key.Insert, ModifierKeys.None, null, "\u001b[2~")]
    [DataRow(Key.Delete, ModifierKeys.None, null, "\u001b[3~")]
    [DataRow(Key.F1, ModifierKeys.None, null, "\u001bOP")]
    [DataRow(Key.F4, ModifierKeys.None, null, "\u001bOS")]
    [DataRow(Key.F5, ModifierKeys.None, null, "\u001b[15~")]
    [DataRow(Key.F12, ModifierKeys.None, null, "\u001b[24~")]
    [DataRow(Key.F1, ModifierKeys.Shift, null, "\u001b[1;2P")]
    [DataRow(Key.A, ModifierKeys.Control, null, "\u0001")]
    [DataRow(Key.C, ModifierKeys.Control, null, "\u0003")]
    [DataRow(Key.Z, ModifierKeys.Control, null, "\u001a")]
    [DataRow(Key.BracketLeft, ModifierKeys.Control, null, "\u001b")]
    [DataRow(Key.Backslash, ModifierKeys.Control, null, "\u001c")]
    [DataRow(Key.BracketRight, ModifierKeys.Control, null, "\u001d")]
    [DataRow(Key.Space, ModifierKeys.Control, null, "\0")]
    [DataRow(Key.D2, ModifierKeys.Control, null, "\0")]
    [DataRow(Key.A, ModifierKeys.Alt, null, "\u001ba")]
    [DataRow(Key.A, ModifierKeys.Alt | ModifierKeys.Shift, null, "\u001bA")]
    [DataRow(Key.A, ModifierKeys.Control | ModifierKeys.Alt, null, "\u001b\u0001")]
    [DataRow(Key.A, ModifierKeys.None, "a", "a")]
    [DataRow(Key.A, ModifierKeys.Shift, "A", "A")]
    [DataRow(Key.D1, ModifierKeys.Shift, "!", "!")]
    [DataRow(Key.Space, ModifierKeys.None, " ", " ")]
    [DataRow(Key.Slash, ModifierKeys.None, "/", "/")]
    public void KeyProducesSequence(Key key, ModifierKeys modifiers, string? text, string expected)
    {
        var term = CreateTerminal();
        Assert.AreEqual(Escape(expected), Escape(Type(term, key, modifiers, text)), $"{modifiers}+{key}");
    }

    /// <summary>
    /// Every key under every Shift, Ctrl and Alt combination, in the default modes and with application cursor keys and
    /// backarrow mode set, sends what Windows Terminal sends: the key and its layout character encoded together.
    /// </summary>
    [TestMethod]
    [DataRow("")]
    [DataRow("\u001b[?1h")]
    [DataRow("\u001b[?67h")]
    [DataRow("\u001b[20h")]
    public void EveryKeyMatchesCombinedKeyAndCharacterEncoding(string modes)
    {
        var routed = CreateTerminal();
        var reference = CreateTerminal();
        routed.Write(modes);
        reference.Write(modes);

        var failures = new List<string>();
        int checkedCount = 0;
        foreach (var key in Enum.GetValues<Key>())
        {
            int virtualKey = TerminalKeyMap.ToVirtualKey(key);
            if (key == Key.None)
            {
                continue;
            }

            if (virtualKey == 0)
            {
                failures.Add($"{key}: no virtual key");
                continue;
            }

            for (int combination = 0; combination < 8; combination++)
            {
                var modifiers = ModifierKeys.None;
                if ((combination & 1) != 0)
                {
                    modifiers |= ModifierKeys.Shift;
                }

                if ((combination & 2) != 0)
                {
                    modifiers |= ModifierKeys.Control;
                }

                if ((combination & 4) != 0)
                {
                    modifiers |= ModifierKeys.Alt;
                }

                var states = TerminalKeyMap.ToControlKeyStates(modifiers);
                uint layoutCharacter = UsKeyboardLayout.Instance.GetCharacter(virtualKey, states);
                char character = layoutCharacter >= TerminalInput.INVALID_CODEPOINT ? '\0' : (char)layoutCharacter;
                string expected = reference.TerminalInput.HandleKey(new TerminalKeyEvent(true, virtualKey, 0, character, states)) ?? string.Empty;

                // The platform delivers text only for printable characters.
                string? text = character >= 0x20 && character != 0x7F ? character.ToString() : null;
                string actual = Type(routed, key, modifiers, text);
                checkedCount++;

                if (actual != expected)
                {
                    failures.Add($"{modifiers}+{key}: expected {Escape(expected)}, got {Escape(actual)}");
                }
            }
        }

        Assert.AreEqual((Enum.GetValues<Key>().Length - 1) * 8, checkedCount);
        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    private static string TypeWithSymbols(TerminalCore term, Key key, ModifierKeys modifiers, string? keySymbol, string? baseKeySymbol, bool altIsMeta = false)
    {
        var states = TerminalKeyMap.ToControlKeyStates(modifiers);
        string? output = term.SendKeyEvent(TerminalKeyMap.ToVirtualKey(key), 0, states, keyDown: true, new TerminalKeySymbols(keySymbol, baseKeySymbol), altIsMeta);
        return output ?? "<unhandled>";
    }

    /// <summary>
    /// With the platform's texts for the key, Alt and Ctrl follow the user's layout instead of US positions, and a key
    /// that types a third-level character or starts a dead key is left to the platform to type.
    /// </summary>
    [TestMethod]
    [DataRow(Key.A, ModifierKeys.Alt, "ф", "ф", false, "\u001bф", "Russian Alt+ф sends ESC ф")]
    [DataRow(Key.A, ModifierKeys.Control, "ф", "ф", false, "\u0001", "Russian Ctrl+ф falls back to the key's position, ^A")]
    [DataRow(Key.D1, ModifierKeys.Alt | ModifierKeys.Shift, "!", "!", false, "\u001b!", "Alt+Shift+1 sends ESC !")]
    [DataRow(Key.A, ModifierKeys.Control, "a", "a", false, "\u0001", "Ctrl+A")]
    [DataRow(Key.Q, ModifierKeys.None, "@", "q", false, "<unhandled>", "Windows AltGr+Q, reported without modifiers, types @")]
    [DataRow(Key.Q, ModifierKeys.Control | ModifierKeys.Alt, "@", "q", false, "<unhandled>", "left Ctrl+Alt+Q types @ while AltGr aliasing is on")]
    [DataRow(Key.Q, ModifierKeys.Control | ModifierKeys.Alt, "q", "q", false, "\u001b\u0011", "Ctrl+Alt+Q on a layout without a third level")]
    [DataRow(Key.O, ModifierKeys.Alt, "ø", "o", false, "<unhandled>", "Option+O types ø")]
    [DataRow(Key.O, ModifierKeys.Alt, "ø", "o", true, "\u001bo", "Option+O as Meta sends ESC o")]
    [DataRow(Key.E, ModifierKeys.Alt, null, "e", false, "<unhandled>", "Option+E starts a dead key")]
    [DataRow(Key.E, ModifierKeys.Alt, null, "e", true, "\u001be", "Option+E as Meta sends ESC e")]
    [DataRow(Key.Equal, ModifierKeys.Alt, null, "´", false, "<unhandled>", "Alt with a dead key sends nothing of its own")]
    [DataRow(Key.A, ModifierKeys.Alt, "a", "a", false, "\u001ba", "a Korean input method's Option+A is a plain a")]
    [DataRow(Key.A, ModifierKeys.None, "a", "a", false, "<unhandled>", "a plain letter arrives as text")]
    public void TheLayoutTextsDecideAltAndCtrl(Key key, ModifierKeys modifiers, string? keySymbol, string? baseKeySymbol, bool altIsMeta, string expected, string because)
    {
        var term = CreateTerminal();
        Assert.AreEqual(Escape(expected), Escape(TypeWithSymbols(term, key, modifiers, keySymbol, baseKeySymbol, altIsMeta)), because);
    }

    [TestMethod]
    public void WithoutAltGrAliasingCtrlAltIsAControlAltCombination()
    {
        var term = CreateTerminal();
        term.UpdateSettings(new TerminalSettings { AltGrAliasing = false });

        Assert.AreEqual(Escape("\u001b\u0011"), Escape(TypeWithSymbols(term, Key.Q, ModifierKeys.Control | ModifierKeys.Alt, "@", "q")));
    }

    [TestMethod]
    public void TheLayoutTextsApplyToTheirKeyOnly()
    {
        var term = CreateTerminal();
        TypeWithSymbols(term, Key.A, ModifierKeys.Alt, "ф", "ф");

        // The next key, reported without texts, is encoded with the US layout again.
        Assert.AreEqual(Escape("\u001bb"), Escape(Type(term, Key.B, ModifierKeys.Alt, null)));
    }

    [TestMethod]
    public void CursorKeysFollowApplicationMode()
    {
        var term = CreateTerminal();
        term.Write("\u001b[?1h");
        Assert.AreEqual(Escape("\u001bOA"), Escape(Type(term, Key.Up, ModifierKeys.None, null)));
        Assert.AreEqual(Escape("\u001bOH"), Escape(Type(term, Key.Home, ModifierKeys.None, null)));

        // Modified cursor keys stay CSI in application mode.
        Assert.AreEqual(Escape("\u001b[1;5A"), Escape(Type(term, Key.Up, ModifierKeys.Control, null)));

        term.Write("\u001b[?1l");
        Assert.AreEqual(Escape("\u001b[A"), Escape(Type(term, Key.Up, ModifierKeys.None, null)));
    }

    [TestMethod]
    public void BackarrowKeyModeSwapsBackspace()
    {
        var term = CreateTerminal();
        term.Write("\u001b[?67h");
        Assert.AreEqual(Escape("\b"), Escape(Type(term, Key.Backspace, ModifierKeys.None, null)));
        Assert.AreEqual(Escape("\u007f"), Escape(Type(term, Key.Backspace, ModifierKeys.Control, null)));
    }

    [TestMethod]
    public void LineFeedModeSendsCrLfForEnter()
    {
        var term = CreateTerminal();
        term.Write("\u001b[20h");
        Assert.AreEqual(Escape("\r\n"), Escape(Type(term, Key.Enter, ModifierKeys.None, null)));
    }

    [TestMethod]
    public void AutoRepeatOffDropsHeldKeysButNotPressesWithRelease()
    {
        var term = CreateTerminal();
        term.Write("\u001b[?8l");

        Assert.AreEqual("l", Type(term, Key.L, ModifierKeys.None, "l"));
        Assert.IsTrue(string.IsNullOrEmpty(Release(term, Key.L)), "a release sends nothing");
        Assert.AreEqual("l", Type(term, Key.L, ModifierKeys.None, "l"));

        // Held without a release, the next press is a repeat.
        Assert.AreEqual(string.Empty, Type(term, Key.L, ModifierKeys.None, "l"));
        Release(term, Key.L);

        Assert.AreEqual(Escape("\u007f"), Escape(Type(term, Key.Backspace, ModifierKeys.None, null)));
        Assert.AreEqual(string.Empty, Type(term, Key.Backspace, ModifierKeys.None, null));
        Release(term, Key.Backspace);
        Assert.AreEqual(Escape("\u007f"), Escape(Type(term, Key.Backspace, ModifierKeys.None, null)));
    }

    private static string? Release(TerminalCore term, Key key) =>
        term.SendKeyEvent(TerminalKeyMap.ToVirtualKey(key), 0, ControlKeyStates.None, keyDown: false);

    [TestMethod]
    public void TextWithoutKeyIsSentAsIs()
    {
        var term = CreateTerminal();
        Assert.AreEqual("한", term.SendCharEvent('한', 0, ControlKeyStates.None));
    }

    [TestMethod]
    [DataRow(Key.Enter)]
    [DataRow(Key.Left)]
    [DataRow(Key.Backspace)]
    [DataRow(Key.Escape)]
    public void InputMethodTextAfterAnEncodedKeyIsSentAsIs(Key key)
    {
        // The input method keeps the keys it composes with, so its commit follows the last key that reached the terminal.
        var term = CreateTerminal();
        Assert.AreNotEqual(string.Empty, Type(term, key, ModifierKeys.None, null));
        Assert.AreEqual("한", term.SendCharEvent('한', 0, ControlKeyStates.None));
    }

    private static string Escape(string text) =>
        text.Replace("\u001b", "ESC").Replace("\u007f", "DEL").Replace("\0", "NUL").Replace("\r", "CR").Replace("\n", "LF").Replace("\t", "TAB").Replace("\b", "BS");
}
