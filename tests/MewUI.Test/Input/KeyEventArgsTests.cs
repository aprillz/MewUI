using Aprillz.MewUI;

namespace MewUI.Test.Input;

/// <summary>The layout text of a key event is optional and set by the platform when it raises the event.</summary>
[TestClass]
public sealed class KeyEventArgsTests
{
    [TestMethod]
    public void TheLayoutTextIsAbsentUnlessThePlatformSetsIt()
    {
        var args = new KeyEventArgs(Key.A, platformKey: 0, ModifierKeys.Control);

        Assert.IsNull(args.KeySymbol);
        Assert.IsNull(args.BaseKeySymbol);
    }

    [TestMethod]
    public void TheLayoutTextIsSetWithTheEvent()
    {
        // A German layout's AltGr+Q types @ where the key alone types q.
        var args = new KeyEventArgs(Key.Q, platformKey: 0, ModifierKeys.Control | ModifierKeys.Alt)
        {
            KeySymbol = "@",
            BaseKeySymbol = "q",
        };

        Assert.AreEqual("@", args.KeySymbol);
        Assert.AreEqual("q", args.BaseKeySymbol);
        Assert.AreEqual(Key.Q, args.Key);
        Assert.AreEqual(ModifierKeys.Control | ModifierKeys.Alt, args.Modifiers);
    }
}
