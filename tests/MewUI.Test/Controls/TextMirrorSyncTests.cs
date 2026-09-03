using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// The document is the truth for a text control's text; the mirror property only follows it when
/// something observes it. Assigning through the setter must therefore judge against the document,
/// never against a mirror value that may have gone stale.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TextMirrorSyncTests
{
    private static void Attach(TextBase control)
    {
        var window = HeadlessWindow.Create(400, 300);
        window.Content = control;
        window.PerformLayout();
    }

    private static void SetText(TextBase control, string value)
    {
        switch (control)
        {
            case TextBox textBox: textBox.Text = value; break;
            case MultiLineTextBox multiLine: multiLine.Text = value; break;
            case PasswordBox passwordBox: passwordBox.Password = value; break;
            default: throw new NotSupportedException(control.GetType().Name);
        }
    }

    private static string GetText(TextBase control) => control switch
    {
        TextBox textBox => textBox.Text,
        MultiLineTextBox multiLine => multiLine.Text,
        PasswordBox passwordBox => passwordBox.Password,
        _ => throw new NotSupportedException(control.GetType().Name),
    };

    private static IEnumerable<object[]> Controls()
    {
        yield return [new Func<TextBase>(() => new TextBox())];
        yield return [new Func<TextBase>(() => new MultiLineTextBox())];
        yield return [new Func<TextBase>(() => new PasswordBox())];
    }

    [TestMethod]
    [DynamicData(nameof(Controls), DynamicDataSourceType.Method)]
    public void ClearingAgainAfterAppendLands(Func<TextBase> create)
    {
        var control = create();
        SetText(control, "initial");
        Attach(control);

        SetText(control, "");
        Assert.AreEqual("", GetText(control), "the first clear lands");

        control.AppendText("typed");
        Assert.AreEqual("typed", GetText(control));

        // No binding and no subscriber, so the mirror still holds "" from the first clear.
        SetText(control, "");
        Assert.AreEqual("", GetText(control), "clearing again must not be dropped as a no-op");
    }

    [TestMethod]
    [DynamicData(nameof(Controls), DynamicDataSourceType.Method)]
    public void AssigningTheStaleMirrorValueUpdatesTheDocument(Func<TextBase> create)
    {
        var control = create();
        Attach(control);

        SetText(control, "alpha");
        control.AppendText("-beta");
        Assert.AreEqual("alpha-beta", GetText(control));

        SetText(control, "alpha");
        Assert.AreEqual("alpha", GetText(control), "the mirror still says alpha, but the document does not");
    }

    // PasswordBox keeps no undo history by design (its history size limit is 0), so it has nothing to preserve.
    private static IEnumerable<object[]> ControlsWithUndo()
    {
        yield return [new Func<TextBase>(() => new TextBox())];
        yield return [new Func<TextBase>(() => new MultiLineTextBox())];
    }

    [TestMethod]
    [DynamicData(nameof(ControlsWithUndo), DynamicDataSourceType.Method)]
    public void AssigningTheCurrentTextKeepsUndoHistory(Func<TextBase> create)
    {
        var control = create();
        Attach(control);

        control.AppendText("edit");
        Assert.IsTrue(control.CanUndo, "an edit through the session is undoable");

        SetText(control, "edit");
        Assert.IsTrue(control.CanUndo, "assigning what the document already holds is not a reset");

        SetText(control, "other");
        Assert.IsFalse(control.CanUndo, "a real assignment still drops the history");
    }

    // A direct write to a bound property replaces the binding (MewObject.SetValue); the setter keeps
    // that contract. Only document edits commit through the binding.
    [TestMethod]
    public void SetterReplacesTheBindingLikeAnyDirectWrite()
    {
        var source = new ObservableValue<string>("a");
        var textBox = new TextBox();
        textBox.SetBinding(TextBox.TextProperty, source);
        Attach(textBox);

        textBox.AppendText("b");
        Assert.AreEqual("ab", source.Value, "a document edit commits through the binding");
        Assert.IsTrue(textBox.HasPropertyBinding(TextBox.TextProperty.Id));

        textBox.Text = "";
        Assert.AreEqual("", textBox.Text, "the assignment lands in the document");
        Assert.IsFalse(textBox.HasPropertyBinding(TextBox.TextProperty.Id), "a direct write replaces the binding");
        Assert.AreEqual("ab", source.Value, "the replaced binding no longer forwards to the source");
    }

    [TestMethod]
    public void SetterRaisesTextChangedOnce()
    {
        var textBox = new TextBox();
        Attach(textBox);
        int raised = 0;
        textBox.TextChanged += _ => raised++;

        textBox.Text = "one";
        Assert.AreEqual(1, raised);

        textBox.Text = "one";
        Assert.AreEqual(1, raised, "assigning the same text is not a change");
    }
}
