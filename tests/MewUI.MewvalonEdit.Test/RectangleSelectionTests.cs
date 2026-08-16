using Aprillz.MewUI;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Editing;
using Aprillz.MewUI.MewvalonEdit.Document;
using Aprillz.MewUI.MewvalonEdit.Folding;
using Aprillz.MewUI.Text;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// The rectangle is made of two x pixels, and every line gives up the columns those pixels land on.
/// These pin the re-ported write path: typing lands on every line, virtual space is padded into
/// existence, a block paste distributes its lines, and the whole edit is one undo step.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RectangleSelectionTests
{
    private static TextEditor CreateEditor(string text)
    {
        // Monospace, so a column's x is the same on every line and the pixel-to-column round trip
        // in the assertions is exact; the pixel model itself is what makes proportional fonts work.
        var editor = new TextEditor
        {
            Text = text,
            SkipViewportCull = true,
            FontFamily = "Consolas",
            FontSize = 13
        };
        editor.Measure(new Size(400, 300));
        editor.Arrange(new Rect(0, 0, 400, 300));
        return editor;
    }

    [TestMethod]
    public void EveryCoveredLineGivesUpTheSameColumns()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor("abcdef\nghijkl\nmnopqr");
        var selection = new RectangleSelection(
            editor.TextArea, new TextViewPosition(1, 3, 2), new TextViewPosition(3, 5, 4));

        var segments = selection.Segments.ToArray();
        Assert.HasCount(3, segments);
        for (int index = 0; index < segments.Length; index++)
        {
            Assert.AreEqual(2, segments[index].StartVisualColumn, $"line {index + 1} start column");
            Assert.AreEqual(4, segments[index].EndVisualColumn, $"line {index + 1} end column");
            Assert.AreEqual(2, segments[index].Length, $"line {index + 1} length");
        }
        Assert.AreEqual("cd\nij\nop", selection.GetText().ReplaceLineEndings("\n"));
    }

    [TestMethod]
    public void AShortLineExtendsIntoVirtualSpace()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor("abcdef\nab");
        var selection = new RectangleSelection(
            editor.TextArea, new TextViewPosition(1, 3, 2), new TextViewPosition(2, 3, 5));

        var segments = selection.Segments.ToArray();
        Assert.HasCount(2, segments);
        Assert.AreEqual(5, segments[0].EndVisualColumn);
        var document = editor.Document;
        var shortLine = document.GetLineByNumber(2);
        Assert.AreEqual(shortLine.Offset + shortLine.Length, segments[1].EndOffset,
            "The short line's segment must stop at its end while the column runs on virtually.");
        Assert.AreEqual(5, segments[1].EndVisualColumn);
    }

    [TestMethod]
    public void TypingLandsOnEveryLineAndUndoesAsOneStep()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor("abcdef\nghijkl\nmnopqr");
        var selection = new RectangleSelection(
            editor.TextArea, new TextViewPosition(1, 3, 2), new TextViewPosition(3, 5, 4));

        selection.ReplaceSelectionWithText("XY");

        Assert.AreEqual("abXYef\nghXYkl\nmnXYqr", editor.Text.ReplaceLineEndings("\n"));

        editor.Document.UndoStack.Undo();
        Assert.AreEqual("abcdef\nghijkl\nmnopqr", editor.Text.ReplaceLineEndings("\n"),
            "The whole rectangle edit must undo as a single step.");
    }

    [TestMethod]
    public void TypingIntoVirtualSpacePadsTheShortLine()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor("abcdef\nab");
        // A zero-width rectangle at visual column 4: the short line only has two columns, so the
        // missing two must be created as spaces before the typed text.
        var selection = new RectangleSelection(
            editor.TextArea, new TextViewPosition(1, 5, 4), new TextViewPosition(2, 3, 4));

        selection.ReplaceSelectionWithText("X");

        Assert.AreEqual("abcdXef\nab  X", editor.Text.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// The typed text lands where the rectangle stands whether or not the end-of-line marker is
    /// shown. The marker holds a column while it is drawn but moves along with what is typed, so
    /// counting it into the padding left the text one column short.
    /// </summary>
    [TestMethod]
    public void ShowingTheEndOfLineMarkerDoesNotShortenThePadding()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor("abcdef\nab\n");
        editor.Options.ShowEndOfLine = true;
        var selection = new RectangleSelection(
            editor.TextArea, new TextViewPosition(1, 5, 4), new TextViewPosition(2, 3, 4));

        selection.ReplaceSelectionWithText("X");

        Assert.AreEqual("abcdXef\nab  X\n", editor.Text.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// An empty line has no indentation to continue, so it is padded like a line that holds text.
    /// A line that is tabs only keeps filling with tabs.
    /// </summary>
    [TestMethod]
    public void AnEmptyLineIsPaddedWithSpacesAndATabLineWithTabs()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor("abcdefgh\n\n\t");
        Assert.IsFalse(editor.Options.ConvertTabsToSpaces, "The tab branch needs tabs to be kept.");
        // The tab line reaches column 8's x at its own column 5, the tab standing for four columns.
        var selection = new RectangleSelection(
            editor.TextArea, new TextViewPosition(1, 9, 8), new TextViewPosition(3, 2, 5));

        selection.ReplaceSelectionWithText("X");

        Assert.AreEqual("abcdefghX\n        X\n\t\tX", editor.Text.ReplaceLineEndings("\n"));
    }

    [TestMethod]
    public void AMultiLineReplacementDistributesItsLines()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor("abcdef\nghijkl\nmnopqr");
        var selection = new RectangleSelection(
            editor.TextArea, new TextViewPosition(1, 3, 2), new TextViewPosition(3, 5, 4));

        selection.ReplaceSelectionWithText("11\n22\n33");

        Assert.AreEqual("ab11ef\ngh22kl\nmn33qr", editor.Text.ReplaceLineEndings("\n"));
        Assert.IsTrue(editor.TextArea.Selection.IsEmpty, "A block paste ends the selection.");
    }

    /// <summary>
    /// A collapsed folding puts several document lines on one laid-out line. The rectangle covers
    /// that line once, so the text lands once and the hidden lines are left alone.
    /// </summary>
    [TestMethod]
    public void ACollapsedFoldingTakesTheInsertionOnce()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor("aaaa\nbbbb {\ncccc\ndddd\n} eeee\nffff");
        var manager = FoldingManager.Install(editor);
        new BraceFoldingStrategy().UpdateFoldings(manager, editor.Document);
        editor.Measure(new Size(400, 300));
        editor.Arrange(new Rect(0, 0, 400, 300));
        var folding = manager.AllFoldings.FirstOrDefault();
        Assert.IsNotNull(folding, "The brace strategy found no folding to collapse.");
        folding.IsFolded = true;
        editor.Measure(new Size(400, 300));
        editor.Arrange(new Rect(0, 0, 400, 300));

        var selection = new RectangleSelection(
            editor.TextArea, new TextViewPosition(1, 3, 2), new TextViewPosition(6, 3, 2));

        selection.ReplaceSelectionWithText("XY");

        Assert.AreEqual(
            "aaXYaa\nbbXYbb {\ncccc\ndddd\n} eeee\nffXYff",
            editor.Text.ReplaceLineEndings("\n"));
    }

    /// <summary>
    /// The caret steps over a collapsed folding rather than into it. Walking the document text alone
    /// mapped every hidden offset back onto the placeholder's first column, which left the caret
    /// unable to pass the folding at all.
    /// </summary>
    [TestMethod]
    public void TheCaretStepsOverACollapsedFolding()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor("a {\nhidden one\nhidden two\n} b");
        var manager = FoldingManager.Install(editor);
        new BraceFoldingStrategy().UpdateFoldings(manager, editor.Document);
        editor.Measure(new Size(400, 300));
        editor.Arrange(new Rect(0, 0, 400, 300));
        var folding = manager.AllFoldings.FirstOrDefault();
        Assert.IsNotNull(folding);
        folding.IsFolded = true;
        editor.Measure(new Size(400, 300));
        editor.Arrange(new Rect(0, 0, 400, 300));

        var visualLine = editor.TextArea.TextView.GetOrConstructVisualLine(
            editor.Document.GetLineByNumber(1));
        Assert.IsNotNull(visualLine);

        var stops = new List<int>();
        int column = 0;
        while (column >= 0 && stops.Count < 8)
        {
            stops.Add(column);
            column = visualLine.GetNextCaretPosition(
                column, LogicalDirection.Forward, CaretPositioningMode.Normal, allowVirtualSpace: false);
        }

        // "a { ... } b" lays out as "a ... b": the placeholder holds columns 2 to 4.
        CollectionAssert.AreEqual(
            new[] { 0, 1, 2, 5, 6, 7 },
            stops,
            $"The caret stopped at [{string.Join(", ", stops)}] instead of stepping over the placeholder.");
    }

    [TestMethod]
    public void RectangularPasteRefusesABlockTallerThanTheDocument()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor("abcdef\nghijkl");

        bool pasted = RectangleSelection.PerformRectangularPaste(
            editor.TextArea, new TextViewPosition(1, 1, 0), "1\n2\n3", selectInsertedText: false);

        Assert.IsFalse(pasted, "Three block lines cannot land on a two-line document.");
        Assert.AreEqual("abcdef\nghijkl", editor.Text.ReplaceLineEndings("\n"));
    }
}
