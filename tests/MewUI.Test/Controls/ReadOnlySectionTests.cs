using Aprillz.MewUI;
using Aprillz.MewUI.Text;
using Aprillz.MewUI.Text.Editing;

namespace MewUI.Test.Controls;

/// <summary>
/// A protected range survives an edit that spans it, and what survives comes back as one undo step
/// rather than one per surviving piece.
/// </summary>
[TestClass]
public sealed class ReadOnlySectionTests
{
    private const string DOCUMENT = "abcdefghij";

    [TestMethod]
    public void DeletionKeepsTheProtectedPart()
    {
        var (session, document) = CreateSession(protectedRange: new TextRange(3, 3));

        session.SetSelection(0, DOCUMENT.Length);
        session.ReplaceSelection(string.Empty);

        Assert.AreEqual("def", document.GetText(0, document.TextLength));
    }

    [TestMethod]
    public void PartialDeletionUndoesInOneStep()
    {
        var (session, document) = CreateSession(protectedRange: new TextRange(3, 3));

        session.SetSelection(0, DOCUMENT.Length);
        session.ReplaceSelection(string.Empty);
        session.Undo();

        Assert.AreEqual(DOCUMENT, document.GetText(0, document.TextLength));
        Assert.IsFalse(document.History.CanUndo, "The edit left more than one undo step behind.");
    }

    [TestMethod]
    public void InsertionIsDroppedWhereItIsNotAllowed()
    {
        var (session, document) = CreateSession(protectedRange: new TextRange(0, DOCUMENT.Length));

        session.SetSelection(2, 0);
        session.ReplaceSelection("XY");

        Assert.AreEqual(DOCUMENT, document.GetText(0, document.TextLength));
    }

    [TestMethod]
    public void TextCommittedReportsTypedTextOnce()
    {
        var (session, _) = CreateSession(protectedRange: null);
        var entered = new List<string>();
        session.TextCommitted += entered.Add;

        session.SetSelection(0, 0);
        session.EnterText("hi");

        CollectionAssert.AreEqual(new[] { "hi" }, entered);
    }

    private static (TextEditorSession Session, EditableTextDocument Document) CreateSession(TextRange? protectedRange)
    {
        var document = new EditableTextDocument(DOCUMENT);
        var session = new TextEditorSession(document);
        if (protectedRange is { } range)
        {
            session.EditableRegions = new BlockedRangeProvider(range);
        }
        return (session, document);
    }

    /// <summary>Everything outside the blocked range may be edited.</summary>
    private sealed class BlockedRangeProvider(TextRange blocked) : IEditableRegionProvider
    {
        public bool CanInsert(int offset) => offset <= blocked.Start || offset >= blocked.Start + blocked.Length;

        public void GetDeletableRanges(TextRange range, IList<TextRange> output)
        {
            int end = range.Start + range.Length;
            int blockedEnd = blocked.Start + blocked.Length;
            if (range.Start < blocked.Start)
            {
                output.Add(new TextRange(range.Start, Math.Min(end, blocked.Start) - range.Start));
            }
            if (end > blockedEnd)
            {
                int start = Math.Max(range.Start, blockedEnd);
                output.Add(new TextRange(start, end - start));
            }
        }
    }
}
