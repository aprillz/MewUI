using Aprillz.MewUI;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewvalonEdit;
using Aprillz.MewUI.MewvalonEdit.Editing;

namespace MewUI.MewvalonEdit.Test;

/// <summary>
/// A rectangle with no width is a column of carets. Backspace and Delete take one character from
/// every line it crosses, and a line too short to give up that character is left alone.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RectangleColumnDeleteTests
{
    private const string TEXT = "abcdef\ngh\nmnopqr\nij\nstuvwxyz";

    [TestMethod]
    public void BackspaceTakesTheCharacterBeforeEveryCaret()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor();
        SelectColumn(editor, column: 3);

        Press(editor, Key.Backspace);

        // Column 3 stands between the second and third character, so every line gives up its second.
        Assert.AreEqual("acdef\ng\nmopqr\ni\nsuvwxyz", Text(editor));
        Assert.IsInstanceOfType<RectangleSelection>(editor.TextArea.Selection,
            "The column of carets must survive the delete.");
    }

    [TestMethod]
    public void DeleteTakesTheCharacterAfterEveryCaret()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor();
        SelectColumn(editor, column: 3);

        Press(editor, Key.Delete);

        Assert.AreEqual("abdef\ngh\nmnpqr\nij\nstvwxyz", Text(editor));
    }

    [TestMethod]
    public void BackspaceAtTheFirstColumnJoinsNoLines()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor();
        SelectColumn(editor, column: 1);

        Press(editor, Key.Backspace);

        Assert.AreEqual(TEXT, Text(editor), "A column of carets at the line start has nothing before it to take.");
    }

    [TestMethod]
    public void DeleteFromVirtualSpaceTakesOnlyFromTheLinesThatReachIt()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor();
        // Column 3 is the end of "gh" and of "ij", and the middle of "mnopqr". The rectangle widens
        // into virtual space rather than pulling the next line up, so only the long line gives up a
        // character.
        Select(editor, 2, 3, 4, 3);

        Press(editor, Key.Delete);

        Assert.AreEqual("abcdef\ngh\nmnpqr\nij\nstuvwxyz", Text(editor));
    }

    [TestMethod]
    public void BackspaceFromVirtualSpaceTakesOnlyFromTheLinesThatReachIt()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor();
        // Column 5 stands past the end of "gh" and "ij" and inside the other three.
        SelectColumn(editor, column: 5);

        Press(editor, Key.Backspace);

        Assert.AreEqual("abcef\ngh\nmnoqr\nij\nstuwxyz", Text(editor));
    }

    [TestMethod]
    public void AColumnBeyondEveryLineDeletesNothing()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor();
        SelectColumn(editor, column: 20);

        Press(editor, Key.Delete);

        Assert.AreEqual(TEXT, Text(editor), "Delete past the end of every line has nothing to take.");
    }

    [TestMethod]
    public void AWidthedRectangleStillClearsItsColumn()
    {
        if (!OperatingSystem.IsWindows()) { Assert.Inconclusive("GDI backend is Windows-only."); return; }

        var editor = CreateEditor();
        Select(editor, 1, 3, 5, 5);

        Press(editor, Key.Backspace);

        Assert.AreEqual("abef\ngh\nmnqr\nij\nstwxyz", Text(editor));
    }

    // The caret rides the rectangle's moving corner in the running editor, and that is the corner a
    // delete key widens from.
    private static void SelectColumn(TextEditor editor, int column)
        => Select(editor, 1, column, 5, column);

    private static void Select(TextEditor editor, int startLine, int startColumn, int endLine, int endColumn)
    {
        editor.TextArea.Selection = new RectangleSelection(
            editor.TextArea,
            new TextViewPosition(startLine, startColumn, startColumn - 1),
            new TextViewPosition(endLine, endColumn, endColumn - 1));
        editor.TextArea.Caret.Position = new TextViewPosition(endLine, endColumn, endColumn - 1);
    }

    private static string Text(TextEditor editor) => editor.Text.ReplaceLineEndings("\n");

    private static TextEditor CreateEditor()
    {
        var editor = new TextEditor
        {
            Text = TEXT,
            SkipViewportCull = true,
            FontFamily = "Consolas",
            FontSize = 13
        };
        editor.Measure(new Size(400, 300));
        editor.Arrange(new Rect(0, 0, 400, 300));
        return editor;
    }

    private static void Press(TextEditor editor, Key key)
        => editor.Surface.RaiseKeyDown(new KeyEventArgs(key, platformKey: 0, ModifierKeys.None));
}
