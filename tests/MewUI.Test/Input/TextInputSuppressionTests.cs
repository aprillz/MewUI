using Aprillz.MewUI.Input;

namespace MewUI.Test.Input;

/// <summary>
/// A handled KeyDown must not also arrive as the keystroke's text on platforms that deliver the two on
/// separate messages. The rule is per keystroke, not per key: any character is dropped, every char message
/// of that keystroke is dropped, and the next keystroke starts clean.
/// </summary>
[TestClass]
public sealed class TextInputSuppressionTests
{
    [TestMethod]
    public void AHandledKeyDownDropsTheKeystrokeText()
    {
        var suppression = new TextInputSuppression();
        suppression.BeginKeyDown();
        suppression.SuppressKeystrokeText();

        Assert.IsTrue(suppression.IsSuppressed);
    }

    [TestMethod]
    public void AnUnhandledKeyDownLetsTextThrough()
    {
        var suppression = new TextInputSuppression();
        suppression.BeginKeyDown();

        Assert.IsFalse(suppression.IsSuppressed);
    }

    [TestMethod]
    public void SuppressionCoversEveryCharMessageOfTheKeystroke()
    {
        var suppression = new TextInputSuppression();
        suppression.BeginKeyDown();
        suppression.SuppressKeystrokeText();

        Assert.IsTrue(suppression.IsSuppressed, "first char message, e.g. a high surrogate");
        Assert.IsTrue(suppression.IsSuppressed, "second char message, e.g. the low surrogate");
    }

    [TestMethod]
    public void TheNextKeyDownDropsAStaleFlag()
    {
        var suppression = new TextInputSuppression();
        suppression.BeginKeyDown();
        suppression.SuppressKeystrokeText();
        suppression.BeginKeyDown();

        Assert.IsFalse(suppression.IsSuppressed, "A keystroke that produced no char must not eat a later one.");
    }
}
