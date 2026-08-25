using Aprillz.MewUI;
using Aprillz.MewUI.Input;

namespace MewUI.Test.Input;

/// <summary>
/// A handled KeyDown must not also arrive as committed text on platforms that deliver the two on
/// separate messages. Space is the printable case: Ctrl+Space still generates a space character,
/// so a Ctrl+Space shortcut would otherwise type a space into the focused editor.
/// </summary>
[TestClass]
public sealed class TextInputSuppressionTests
{
    [TestMethod]
    public void AHandledSpaceKeyDownSwallowsTheFollowingSpaceChar()
    {
        var suppression = new TextInputSuppression();
        suppression.ResetPerKeyDown();
        suppression.SuppressNextFromHandledKeyDown(Key.Space);

        Assert.IsTrue(suppression.TryConsumeChar(' '));
        Assert.IsFalse(suppression.TryConsumeChar(' '), "Only the one char of that keystroke is consumed.");
    }

    [TestMethod]
    public void AnUnhandledKeyDownLetsTheSpaceThrough()
    {
        var suppression = new TextInputSuppression();
        suppression.ResetPerKeyDown();

        Assert.IsFalse(suppression.TryConsumeChar(' '));
    }

    [TestMethod]
    public void TheNextKeyDownDropsAStaleFlag()
    {
        var suppression = new TextInputSuppression();
        suppression.SuppressNextFromHandledKeyDown(Key.Space);
        suppression.ResetPerKeyDown();

        Assert.IsFalse(suppression.TryConsumeChar(' '), "A keystroke that produced no char must not eat a later one.");
    }

    [TestMethod]
    public void CommittedTextSuppressionCoversSpace()
    {
        var handled = new KeyEventArgs(Key.Space, platformKey: 0, ModifierKeys.Control) { Handled = true };

        Assert.IsTrue(TextInputSuppression.ShouldSuppressCommittedText(handled, " "));
        Assert.IsFalse(TextInputSuppression.ShouldSuppressCommittedText(handled, "a"));
    }
}
