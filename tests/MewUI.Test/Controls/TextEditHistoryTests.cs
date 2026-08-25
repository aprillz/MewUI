using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text.Editing;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class TextEditHistoryTests
{
    /// <summary>
    /// An undo entry keeps the replaced text verbatim, so a password box must record none: the
    /// history would otherwise hold every value the box has held even after the caller clears it.
    /// </summary>
    [TestMethod]
    public void PasswordBoxRetainsNoUndoHistory()
    {
        var box = new PasswordBox();
        box.ReplaceSelection("secret");
        box.ReplaceSelection("more");

        Assert.IsFalse(box.CanUndo);
        box.Undo();
        Assert.AreEqual("secretmore", box.Password, "Undo must not roll the value back.");
    }

    [TestMethod]
    public void SizeLimitDropsTheOldestEdits()
    {
        var document = new EditableTextDocument();
        var session = new TextEditorSession(document);
        document.History.SizeLimit = 2;
        session.ReplaceSelection("a");
        session.ReplaceSelection("b");
        session.ReplaceSelection("c");

        session.Undo();
        session.Undo();

        Assert.AreEqual("a", document.ToString(), "Only the two most recent edits stay undoable.");
        Assert.IsFalse(session.CanUndo);
    }

    [TestMethod]
    public void UnrecordedDocumentEditClearsUndoHistory()
    {
        var document = new EditableTextDocument();
        var session = new TextEditorSession(document);
        session.ReplaceSelection("hello");
        Assert.IsTrue(session.CanUndo);

        document.Replace(0, 1, string.Empty);

        Assert.IsFalse(session.CanUndo);
        session.Undo();
        Assert.AreEqual("ello", document.ToString());
    }

    [TestMethod]
    public void UndoSurvivesDocumentSwapRoundTrip()
    {
        var box = new MultiLineTextBox();
        box.ReplaceSelection("first");
        var original = box.Document;
        Assert.IsTrue(box.CanUndo);

        box.Document = new EditableTextDocument("second");
        Assert.IsFalse(box.CanUndo);

        box.Document = original;
        Assert.IsTrue(box.CanUndo);
        box.Undo();
        Assert.AreEqual(string.Empty, box.Text);
    }

    [TestMethod]
    public void CompositionIntermediatesKeepHistory()
    {
        var document = new EditableTextDocument();
        var session = new TextEditorSession(document);
        session.ReplaceSelection("ab");
        Assert.IsTrue(session.CanUndo);

        session.BeginComposition();
        session.UpdateComposition("ㅅ");
        session.UpdateComposition("사");
        Assert.IsTrue(session.CanUndo);
        session.CommitComposition();

        Assert.AreEqual("ab사", document.ToString());
        session.Undo();
        Assert.AreEqual("ab", document.ToString());
        session.Undo();
        Assert.AreEqual(string.Empty, document.ToString());
    }

    /// <summary>
    /// Where the caret lands after a programmatic replace. Text arriving at the caret pushes it
    /// along, the way typing does, so a caller that inserts indentation leaves the caret behind it.
    /// </summary>
    [TestMethod]
    public void ReplaceRangeCarriesTheCaretAcrossTheEdit()
    {
        var document = new EditableTextDocument("hello world");
        var session = new TextEditorSession(document);

        session.SetCaret(6);
        session.ReplaceRange(6, 0, ">> ");
        Assert.AreEqual(9, session.CaretPosition, "An insertion at the caret pushes it along.");

        session.SetCaret(4);
        session.ReplaceRange(6, 0, "!");
        Assert.AreEqual(4, session.CaretPosition, "An edit after the caret leaves it alone.");

        session.SetCaret(8);
        session.ReplaceRange(0, 5, "bye");
        Assert.AreEqual(6, session.CaretPosition, "An edit before the caret shifts it by the delta.");

        session.SetCaret(2);
        session.ReplaceRange(0, 6, "abcdefgh");
        Assert.AreEqual(8, session.CaretPosition,
            "A caret inside the replaced range lands at the end of what replaced it.");
    }

    /// <summary>
    /// A routine that edits line by line, such as indenting a block, would otherwise cost one undo
    /// per line.
    /// </summary>
    [TestMethod]
    public void AGroupUndoesAsOneStep()
    {
        var document = new EditableTextDocument();
        var session = new TextEditorSession(document);
        session.ReplaceSelection("start");

        using (document.BeginUndoGroup())
        {
            session.ReplaceSelection("-a");
            session.ReplaceSelection("-b");
            session.ReplaceSelection("-c");
        }
        Assert.AreEqual("start-a-b-c", document.ToString());

        session.Undo();

        Assert.AreEqual("start", document.ToString());
        Assert.IsTrue(session.CanUndo, "The edit made before the group stayed its own step.");
    }

    [TestMethod]
    public void AGroupRedoesAsOneStep()
    {
        var document = new EditableTextDocument();
        var session = new TextEditorSession(document);
        using (document.BeginUndoGroup())
        {
            session.ReplaceSelection("a");
            session.ReplaceSelection("b");
        }
        session.Undo();

        session.Redo();

        Assert.AreEqual("ab", document.ToString());
        Assert.IsFalse(session.CanRedo);
    }

    /// <summary>The caret returns to where it stood when the group opened, not mid-group.</summary>
    [TestMethod]
    public void UndoingAGroupRestoresTheCaretFromBeforeIt()
    {
        var document = new EditableTextDocument();
        var session = new TextEditorSession(document);
        session.ReplaceSelection("abc");
        session.SetCaret(1);

        using (document.BeginUndoGroup())
        {
            session.ReplaceSelection("X");
            session.ReplaceSelection("Y");
        }
        session.Undo();

        Assert.AreEqual("abc", document.ToString());
        Assert.AreEqual(1, session.CaretPosition);
    }

    /// <summary>
    /// So a routine that groups its own edits stays correct when a caller groups it in turn.
    /// </summary>
    [TestMethod]
    public void NestingExtendsTheOutermostGroup()
    {
        var document = new EditableTextDocument();
        var session = new TextEditorSession(document);
        using (document.BeginUndoGroup())
        {
            session.ReplaceSelection("a");
            using (document.BeginUndoGroup())
            {
                session.ReplaceSelection("b");
            }
            session.ReplaceSelection("c");
        }

        session.Undo();

        Assert.AreEqual(string.Empty, document.ToString());
        Assert.IsFalse(session.CanUndo);
    }

    [TestMethod]
    public void EditsAfterAGroupAreTheirOwnSteps()
    {
        var document = new EditableTextDocument();
        var session = new TextEditorSession(document);
        using (document.BeginUndoGroup())
        {
            session.ReplaceSelection("a");
            session.ReplaceSelection("b");
        }
        session.ReplaceSelection("c");

        session.Undo();

        Assert.AreEqual("ab", document.ToString());
    }

    /// <summary>
    /// Half a group would undo to a state the document was never in, so the limit drops the rest of
    /// the group it cut through even though that keeps fewer edits than asked for.
    /// </summary>
    [TestMethod]
    public void TheSizeLimitNeverCutsAGroupInHalf()
    {
        var document = new EditableTextDocument();
        var session = new TextEditorSession(document);
        document.History.SizeLimit = 3;
        using (document.BeginUndoGroup())
        {
            session.ReplaceSelection("a");
            session.ReplaceSelection("b");
        }
        session.ReplaceSelection("c");
        session.ReplaceSelection("d");

        session.Undo();
        session.Undo();

        Assert.AreEqual("ab", document.ToString());
        Assert.IsFalse(session.CanUndo, "The group the limit cut through was dropped whole.");
    }

    /// <summary>The history belongs to the document, so a document with no editor undoes too.</summary>
    [TestMethod]
    public void ADocumentUndoesWithoutASession()
    {
        var document = new EditableTextDocument();
        var session = new TextEditorSession(document);
        session.ReplaceSelection("abc");

        Assert.IsTrue(document.CanUndo);
        Assert.IsTrue(document.Undo());
        Assert.AreEqual(string.Empty, document.ToString());
        Assert.IsFalse(document.Undo(), "There was nothing left to undo.");
        Assert.IsTrue(document.CanRedo);
        Assert.IsTrue(document.Redo());
        Assert.AreEqual("abc", document.ToString());
    }

    [TestMethod]
    public void ClearingTheHistoryLeavesTheTextAlone()
    {
        var document = new EditableTextDocument();
        var session = new TextEditorSession(document);
        session.ReplaceSelection("abc");

        document.ClearUndoHistory();

        Assert.IsFalse(document.CanUndo);
        Assert.AreEqual("abc", document.ToString());
    }

    [TestMethod]
    public void TheUndoSizeLimitIsReadableFromTheDocument()
    {
        var document = new EditableTextDocument();
        document.UndoSizeLimit = 1;
        var session = new TextEditorSession(document);
        session.ReplaceSelection("a");
        session.ReplaceSelection("b");

        Assert.AreEqual(1, document.UndoSizeLimit);
        session.Undo();
        Assert.AreEqual("a", document.ToString());
        Assert.IsFalse(session.CanUndo);
    }

    /// <summary>
    /// The restored positions belong to the edit, not to whichever session replayed it, so a second
    /// session over the same document must not be left pointing into text the replay moved.
    /// </summary>
    [TestMethod]
    public void EverySessionFollowsARestoredCaret()
    {
        var document = new EditableTextDocument();
        var first = new TextEditorSession(document);
        var second = new TextEditorSession(document);
        first.ReplaceSelection("abcdef");
        second.SetCaret(6);

        first.Undo();

        Assert.AreEqual(0, second.CaretPosition, "The second session kept a caret past the end of the text.");
    }

    /// <summary>
    /// An extension that counts undo steps, such as one tracking whether a file is modified, can
    /// only stay right if it sees every step. It intercepts these two; a third would go past it and
    /// leave its count pointing at a state the document is no longer in.
    /// </summary>
    [TestMethod]
    public void TheOnlyWaysToUndoAreTheControlAndTheDocument()
    {
        var entryPoints = typeof(TextBase).Assembly.GetTypes()
            .SelectMany(static type => type.GetMethods(
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Static |
                System.Reflection.BindingFlags.DeclaredOnly))
            .Where(static method => method.IsPublic && method.DeclaringType!.IsPublic)
            .Where(static method => method.Name is "Undo" or "Redo")
            .Select(static method => $"{method.DeclaringType!.Name}.{method.Name}")
            .Distinct()
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToList();

        CollectionAssert.AreEqual(
            new[]
            {
                "EditableTextDocument.Redo",
                "EditableTextDocument.Undo",
                "TextBase.Redo",
                "TextBase.Undo",
                "TextEditorSession.Redo",
                "TextEditorSession.Undo",
            },
            entryPoints,
            $"The set of public undo entry points changed: {string.Join(", ", entryPoints)}");
    }

    [TestMethod]
    public void SharedDocumentMergesHistoryAcrossSessions()
    {
        var document = new EditableTextDocument();
        var first = new TextEditorSession(document);
        var second = new TextEditorSession(document);
        first.ReplaceSelection("a");
        second.SetCaret(document.TextLength);
        second.ReplaceSelection("b");
        Assert.AreEqual("ab", document.ToString());

        first.Undo();
        Assert.AreEqual("a", document.ToString());
        first.Undo();
        Assert.AreEqual(string.Empty, document.ToString());
    }
}
