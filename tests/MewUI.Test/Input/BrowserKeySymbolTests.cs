#if NET10_0_OR_GREATER
using Aprillz.MewUI;
using Aprillz.MewUI.Platform.Browser;

namespace MewUI.Test.Input;

/// <summary>
/// The browser reports the text a key types (<c>KeyboardEvent.key</c>) but not the text without Alt; the base text is
/// then the key position's US character, as xterm.js does.
/// </summary>
[TestClass]
public sealed class BrowserKeySymbolTests
{
    [TestMethod]
    [DataRow("KeyA", "a", ModifierKeys.None, "a", "a")]
    [DataRow("KeyA", "A", ModifierKeys.Shift, "A", "A")]
    [DataRow("KeyA", "a", ModifierKeys.Control, "a", "a")]
    [DataRow("KeyO", "ø", ModifierKeys.Alt, "ø", "o")]
    [DataRow("Digit1", "!", ModifierKeys.Alt | ModifierKeys.Shift, "!", "!")]
    [DataRow("KeyA", "ф", ModifierKeys.None, "ф", "ф")]
    [DataRow("KeyQ", "@", ModifierKeys.None, "@", "@")]
    [DataRow("Enter", "Enter", ModifierKeys.None, null, null)]
    [DataRow("KeyE", "Dead", ModifierKeys.Alt, null, "e")]
    [DataRow("ArrowLeft", "ArrowLeft", ModifierKeys.None, null, null)]
    public void TheKeyTextsComeFromTheEvent(string code, string? key, ModifierKeys modifiers, string? expectedSymbol, string? expectedBase)
    {
        var (keySymbol, baseKeySymbol) = BrowserKeyMap.Symbols(code, key, modifiers);

        Assert.AreEqual(expectedSymbol, keySymbol);
        Assert.AreEqual(expectedBase, baseKeySymbol);
    }

    [TestMethod]
    [DataRow("Slash", false, "/")]
    [DataRow("Slash", true, "?")]
    [DataRow("Quote", true, "\"")]
    [DataRow("Backslash", false, "\\")]
    [DataRow("Digit2", true, "@")]
    [DataRow("F1", false, null)]
    public void APositionHasItsUsText(string code, bool shift, string? expected)
        => Assert.AreEqual(expected, BrowserKeyMap.UsText(code, shift));
}
#endif
