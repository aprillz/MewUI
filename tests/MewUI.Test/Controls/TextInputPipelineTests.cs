using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// What a TextInput subscriber can rely on: rewriting the event's text swaps what the control
/// inserts, typed text can be aimed at a range with the caret landing after it, and the document a
/// subscriber sees no longer shows a composition's preedit.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TextInputPipelineTests
{
    [TestMethod]
    public void ARewrittenEventTextIsWhatGetsInserted()
    {
        var textBox = CreateHost(out _, "ab");
        textBox.Select(1, 0);
        textBox.TextInput += static e => e.Text = "--" + e.Text;

        Type(textBox, "X");

        Assert.AreEqual("a--Xb", textBox.Text);
        Assert.AreEqual(4, textBox.SelectionStart, "the caret belongs after everything that was inserted");
    }

    [TestMethod]
    public void ARewrittenEventTextIsNormalizedLikeAConstructedOne()
    {
        var args = new TextInputEventArgs("X");

        args.Text = "a\r\nb";

        Assert.AreEqual("a\nb", args.Text);
    }

    [TestMethod]
    public void EnterTextOverARangeLandsTheCaretAndUndoesToWhereItWas()
    {
        var textBox = CreateHost(out _, "abcdef");
        textBox.Select(0, 0);

        textBox.EnterText(0, 1, "XY");

        Assert.AreEqual("XYbcdef", textBox.Text);
        Assert.AreEqual(2, textBox.SelectionStart, "typing semantics land the caret at the end of the insertion");

        textBox.Undo();
        Assert.AreEqual("abcdef", textBox.Text);
        Assert.AreEqual(0, textBox.SelectionStart, "undo returns to where the caret was");
        Assert.AreEqual(0, textBox.SelectionLength);

        textBox.Redo();
        Assert.AreEqual("XYbcdef", textBox.Text);
        Assert.AreEqual(2, textBox.SelectionStart, "redo lands the caret after the insertion again");
    }

    /// <summary>
    /// The result string arrives through TextInput while the preedit is still active. Every
    /// subscriber must see the document without the preedit, or one that edits the document itself
    /// leaves the preedit standing next to what it wrote.
    /// </summary>
    [TestMethod]
    public void ASubscriberSeesTheDocumentWithoutThePreedit()
    {
        var textBox = CreateHost(out _, "ab");
        textBox.Select(1, 0);
        var client = (ITextCompositionClient)textBox;
        client.HandleTextCompositionStart(new TextCompositionEventArgs());
        client.HandleTextCompositionUpdate(new TextCompositionEventArgs("ㄴ"));
        Assert.AreEqual("aㄴb", textBox.Text, "the preedit was expected inline while composing");

        string? seenDuringEvent = null;
        textBox.TextInput += e => seenDuringEvent = textBox.Text;
        Type(textBox, "나");

        Assert.AreEqual("ab", seenDuringEvent, "a subscriber saw the preedit still in the document");
        Assert.AreEqual("a나b", textBox.Text);
    }

    /// <summary>A claimed result must not leave the cancelled preedit behind either.</summary>
    [TestMethod]
    public void AClaimedResultStillRemovesThePreedit()
    {
        var textBox = CreateHost(out _, "ab");
        textBox.Select(1, 0);
        var client = (ITextCompositionClient)textBox;
        client.HandleTextCompositionStart(new TextCompositionEventArgs());
        client.HandleTextCompositionUpdate(new TextCompositionEventArgs("ㄴ"));

        textBox.TextInput += static e => e.Handled = true;
        Type(textBox, "나");

        Assert.AreEqual("ab", textBox.Text, "the claimer took the insertion, so only the preedit removal remains");
        Assert.IsFalse(client.IsComposing);
    }

    private static void Type(MultiLineTextBox textBox, string text)
        => ((ITextInputClient)textBox).HandleTextInput(new TextInputEventArgs(text));

    private static MultiLineTextBox CreateHost(out Window window, string text)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }
        var textBox = new MultiLineTextBox().Width(300).Height(200).Text(text);
        window = HeadlessWindow.Create(300, 200);
        window.Content = textBox;
        window.PerformLayout();
        textBox.Focus();
        return textBox;
    }
}
