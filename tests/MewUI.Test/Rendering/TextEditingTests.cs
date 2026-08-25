using Aprillz.MewUI.Text;
using Aprillz.MewUI.Text.Editing;
using System.Text;

namespace MewUI.Test.Rendering;

[TestClass]
public sealed class TextEditingTests
{
    [TestMethod]
    public void Document_NormalizesLinesAndMaintainsReadOnlyDocumentMapping()
    {
        var document = new EditableTextDocument("one\r\ntwo\rthree\n");

        Assert.AreEqual("one\ntwo\nthree\n", document.ToString());
        Assert.AreEqual(4, document.LineCount);
        Assert.AreEqual(new TextLocation(1, 2), document.GetLocation(6));
        Assert.AreEqual(10, document.GetOffset(2, 2));
        Assert.AreEqual(1, document.GetLineByNumber(1).Delimiter.Length);

        long version = document.Version;
        document.Replace(4, 3, "second");

        Assert.AreEqual(version + 1, document.Version);
        Assert.AreEqual("one\nsecond\nthree\n", document.ToString());
        Assert.AreEqual(4, document.LineCount);
    }

    [TestMethod]
    public void Document_IncrementalLineIndexMatchesStringModelAcrossMixedEdits()
    {
        const int Iterations = 1_000;
        var random = new Random(0x4D6577);
        var model = new StringBuilder("one\ntwo\nthree\n");
        var document = new EditableTextDocument(model.ToString());
        string[] insertions = ["x", "\n", "left\nright", "", "😀", "e\u0301"];

        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            int offset = random.Next(model.Length + 1);
            int removeLength = random.Next(Math.Min(8, model.Length - offset) + 1);
            string inserted = insertions[random.Next(insertions.Length)];

            document.Replace(offset, removeLength, inserted);
            model.Remove(offset, removeLength).Insert(offset, inserted);

            string expected = model.ToString();
            Assert.AreEqual(expected, document.ToString(), $"Text diverged at edit {iteration}.");
            AssertDocumentLines(document, expected, iteration);
        }
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public void Document_TypingNearStartDoesNotRebuildEveryFollowingLine()
    {
        string text = string.Join('\n', Enumerable.Range(0, 100_000).Select(static index => $"line {index}"));
        var document = new EditableTextDocument(text);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        for (int index = 0; index < 100; index++)
        {
            document.Insert(1 + index, "x");
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.AreEqual(100_000, document.LineCount);
        Assert.IsLessThan(2L * 1024 * 1024, allocated,
            $"Incremental typing allocated {allocated:N0} bytes, indicating a full line-index rebuild.");
    }

    [TestMethod]
    public void Session_SelectionUndoAndRedoRestoreTextAndCaretState()
    {
        var document = new EditableTextDocument("alpha beta");
        var editor = new TextEditorSession(document);
        editor.SetSelection(6, 4);

        editor.ReplaceSelection("B");

        Assert.AreEqual("alpha B", document.ToString());
        Assert.AreEqual(7, editor.CaretPosition);
        Assert.IsTrue(editor.CanUndo);

        editor.Undo();
        Assert.AreEqual("alpha beta", document.ToString());
        Assert.AreEqual(new TextRange(6, 4), editor.Selection);

        editor.Redo();
        Assert.AreEqual("alpha B", document.ToString());
        Assert.AreEqual(new TextRange(7, 0), editor.Selection);
    }

    [TestMethod]
    public void Session_LogicalMovementAndDeletionDoNotSplitTextElements()
    {
        var document = new EditableTextDocument("A😀e\u0301한");
        var editor = new TextEditorSession(document);
        editor.SetCaret(document.TextLength);

        editor.MoveLogical(LogicalDirection.Backward, false);
        Assert.AreEqual(5, editor.CaretPosition);
        editor.MoveLogical(LogicalDirection.Backward, false);
        Assert.AreEqual(3, editor.CaretPosition);
        editor.MoveLogical(LogicalDirection.Backward, false);
        Assert.AreEqual(1, editor.CaretPosition);

        editor.Delete();
        Assert.AreEqual("Ae\u0301한", document.ToString(), "Delete split the surrogate pair.");
        editor.Delete();
        Assert.AreEqual("A한", document.ToString(), "Delete split the combining text element.");
    }

    [TestMethod]
    [Timeout(30_000, CooperativeCancellation = true)]
    public void Session_LogicalMovementParsesOnlyTheActiveLine()
    {
        string text = string.Join('\n', Enumerable.Range(0, 100_000).Select(static index => $"line {index}"));
        var document = new EditableTextDocument(text);
        var editor = new TextEditorSession(document);
        editor.SetCaret(document.TextLength);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();

        editor.MoveLogical(LogicalDirection.Backward, extendSelection: false);

        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.AreEqual(document.TextLength - 1, editor.CaretPosition);
        Assert.IsLessThan(64 * 1024L, allocated,
            $"One caret move allocated {allocated:N0} bytes, indicating a whole-document grapheme snapshot.");
    }

    [TestMethod]
    public void Session_LogicalMovementCrossesLineDelimiterOneElementAtATime()
    {
        var editor = new TextEditorSession(new EditableTextDocument("a\nb"));
        editor.SetCaret(1);

        editor.MoveLogical(LogicalDirection.Forward, extendSelection: false);
        Assert.AreEqual(2, editor.CaretPosition);

        editor.MoveLogical(LogicalDirection.Backward, extendSelection: false);
        Assert.AreEqual(1, editor.CaretPosition);
    }

    [TestMethod]
    public void Session_CompositionIsOneUndoableTransactionAndCanBeCancelled()
    {
        var document = new EditableTextDocument("before target after");
        var editor = new TextEditorSession(document);
        editor.SetSelection(7, 6);

        editor.BeginComposition();
        editor.UpdateComposition("ㅎ");
        editor.UpdateComposition("한");
        editor.CommitComposition();

        Assert.AreEqual("before 한 after", document.ToString());
        editor.Undo();
        Assert.AreEqual("before target after", document.ToString());
        Assert.AreEqual(new TextRange(7, 6), editor.Selection);

        editor.BeginComposition();
        editor.UpdateComposition("temporary");
        editor.CancelComposition();
        Assert.AreEqual("before target after", document.ToString());
    }

    [TestMethod]
    public void Session_SelectWordUsesDocumentWordBoundaries()
    {
        var document = new EditableTextDocument("alpha beta_value gamma");
        var editor = new TextEditorSession(document);

        editor.SelectWordAt(9);

        Assert.AreEqual(new TextRange(6, 10), editor.Selection);
        Assert.AreEqual("beta_value", document.GetText(editor.Selection.Start, editor.Selection.Length));
    }

    private static void AssertDocumentLines(EditableTextDocument document, string expected, int iteration)
    {
        string[] lines = expected.Split('\n');
        Assert.AreEqual(lines.Length, document.LineCount, $"Line count diverged at edit {iteration}.");
        int offset = 0;
        for (int lineNumber = 0; lineNumber < lines.Length; lineNumber++)
        {
            var line = document.GetLineByNumber(lineNumber);
            Assert.AreEqual(lineNumber, line.LineNumber);
            Assert.AreEqual(offset, line.Offset, $"Line offset diverged at edit {iteration}, line {lineNumber}.");
            Assert.AreEqual(lines[lineNumber].Length, line.Length);
            int delimiterLength = lineNumber + 1 < lines.Length ? 1 : 0;
            Assert.AreEqual(delimiterLength, line.Delimiter.Length);
            offset += line.Length + delimiterLength;
        }

        int probe = expected.Length == 0 ? 0 : Math.Min(expected.Length, iteration % (expected.Length + 1));
        var containing = document.GetLineByOffset(probe);
        Assert.AreEqual(probe, Math.Clamp(probe, containing.Offset, containing.Offset + containing.TotalLength));
    }
}
