// Ported from Windows Terminal src/cascadia/UnitTests_TerminalCore/SelectionTest.cpp (MIT).

using Aprillz.MewUI.Terminal.Core;
using Aprillz.MewUI.Terminal.Types;

namespace MewUI.Terminal.Test.Core;

[TestClass]
public sealed class SelectionTests
{
    private const int SHORT_MAX = short.MaxValue;

    private static TerminalCore CreateTerminal(int width, int height, int scrollback)
    {
        var term = new TerminalCore(true);
        term.Create(new TilSize(width, height), scrollback, new Renderer());
        return term;
    }

    // The word delimiters and size of the original's MockTermSettings(0, 100, 100).
    private static void UseMockSettings(TerminalCore term)
    {
        term.UpdateSettings(new TerminalSettings { HistorySize = 0, InitialRows = 100, InitialCols = 100 });
    }

    // Validates a selection that spans only one row, the way the renderer reads it.
    private static void ValidateLinearSelection(TerminalCore term, TilPoint start, TilPoint end)
    {
        var selectionSpans = term.GetSelectionSpans();

        Assert.HasCount(1, selectionSpans);
        Assert.AreEqual(start, selectionSpans[0].Start, "start");
        Assert.AreEqual(end, selectionSpans[0].End, "end");
    }

    private static Aprillz.MewUI.Terminal.Buffer.TextBuffer GetTextBuffer(TerminalCore term)
    {
        return term.GetBufferAndViewport().Buffer;
    }

    [TestMethod]
    public void SelectUnit()
    {
        var term = CreateTerminal(100, 100, 0);

        // Simulate click at (x,y) = (5,10)
        term.SetSelectionAnchor(new TilPoint(5, 10));

        ValidateLinearSelection(term, new TilPoint(5, 10), new TilPoint(5, 10));
    }

    [TestMethod]
    public void SelectArea()
    {
        var term = CreateTerminal(100, 100, 0);

        // Simulate click at (x,y) = (5,10)
        int rowValue = 10;
        term.SetSelectionAnchor(new TilPoint(5, rowValue));

        // Simulate move to (x,y) = (15,20)
        term.SetSelectionEnd(new TilPoint(15, 20));

        ValidateLinearSelection(term, new TilPoint(5, rowValue), new TilPoint(15, 20));
    }

    [TestMethod]
    public void OverflowTests()
    {
        var maxCoord = new TilPoint(SHORT_MAX, SHORT_MAX);

        // SetSelectionAnchor and SetSelectionEnd clamp the coordinate to the viewport.
        static void ValidateSingleClickSelection(TilPoint maxCoordinate, int scrollback, TilPoint start, TilPoint end)
        {
            var term = CreateTerminal(10, 10, scrollback);

            term.SetSelectionAnchor(maxCoordinate);
            ValidateLinearSelection(term, start, end);
        }

        // A double click clamps the coordinate to the viewport, then selects the word.
        static void ValidateDoubleClickSelection(TilPoint maxCoordinate, int scrollback, TilPoint start, TilPoint end)
        {
            var term = CreateTerminal(10, 10, scrollback);

            term.MultiClickSelection(maxCoordinate, SelectionExpansion.Word);
            ValidateLinearSelection(term, start, end);
        }

        // A triple click clamps the coordinate to the viewport, then selects the line.
        static void ValidateTripleClickSelection(TilPoint maxCoordinate, int scrollback, TilPoint start, TilPoint end)
        {
            var term = CreateTerminal(10, 10, scrollback);

            term.MultiClickSelection(maxCoordinate, SelectionExpansion.Line);
            ValidateLinearSelection(term, start, end);
        }

        // Test with no scrollback.
        ValidateSingleClickSelection(maxCoord, 0, new TilPoint(10, 9), new TilPoint(10, 9));
        ValidateDoubleClickSelection(maxCoord, 0, new TilPoint(0, 9), new TilPoint(10, 9));
        ValidateTripleClickSelection(maxCoord, 0, new TilPoint(0, 9), new TilPoint(10, 9));

        // Test with max scrollback.
        const int EXPECTED_ROW = SHORT_MAX - 1;
        ValidateSingleClickSelection(maxCoord, SHORT_MAX, new TilPoint(10, EXPECTED_ROW), new TilPoint(10, EXPECTED_ROW));
        ValidateDoubleClickSelection(maxCoord, SHORT_MAX, new TilPoint(0, EXPECTED_ROW), new TilPoint(10, EXPECTED_ROW));
        ValidateTripleClickSelection(maxCoord, SHORT_MAX, new TilPoint(0, EXPECTED_ROW), new TilPoint(10, EXPECTED_ROW));
    }

    [TestMethod]
    public void SelectFromOutofBounds()
    {
        // Clamping the selection anchors to be valid lets all selection expansion functions operate as if they were
        // performed at the boundary.
        var term = CreateTerminal(10, 10, 0);

        var viewport = term.GetViewport();
        int leftBoundary = viewport.Left;
        int rightExclusiveBoundary = viewport.RightExclusive;
        int topBoundary = viewport.Top;
        int bottomBoundary = viewport.BottomInclusive;

        // Case 1: click past right, (x,y) = (20,5), clamps to the right boundary.
        term.SetSelectionAnchor(new TilPoint(20, 5));
        ValidateLinearSelection(term, new TilPoint(rightExclusiveBoundary, 5), new TilPoint(rightExclusiveBoundary, 5));

        // Case 2: click past left, (x,y) = (-20,5), clamps to the left boundary.
        term.SetSelectionAnchor(new TilPoint(-20, 5));
        ValidateLinearSelection(term, new TilPoint(leftBoundary, 5), new TilPoint(leftBoundary, 5));

        // Case 3: click past top, (x,y) = (5,-20), clamps to the top boundary.
        term.SetSelectionAnchor(new TilPoint(5, -20));
        ValidateLinearSelection(term, new TilPoint(5, topBoundary), new TilPoint(5, topBoundary));

        // Case 4: click past bottom, (x,y) = (5,20), clamps to the bottom boundary.
        term.SetSelectionAnchor(new TilPoint(5, 20));
        ValidateLinearSelection(term, new TilPoint(5, bottomBoundary), new TilPoint(5, bottomBoundary));
    }

    [TestMethod]
    public void SelectToOutOfBounds()
    {
        var term = CreateTerminal(10, 10, 0);

        var viewport = term.GetViewport();
        const int LEFT_BOUNDARY = 0;
        int rightExclusiveBoundary = viewport.RightExclusive;

        // Simulate click at (x,y) = (5,5)
        term.SetSelectionAnchor(new TilPoint(5, 5));

        // Case 1: move out of the right boundary.
        term.SetSelectionEnd(new TilPoint(20, 5));
        ValidateLinearSelection(term, new TilPoint(5, 5), new TilPoint(rightExclusiveBoundary, 5));

        // Case 2: move out of the left boundary.
        term.SetSelectionEnd(new TilPoint(-20, 5));
        ValidateLinearSelection(term, new TilPoint(LEFT_BOUNDARY, 5), new TilPoint(5, 5));

        // Case 3: move out of the top boundary.
        term.SetSelectionEnd(new TilPoint(5, -20));
        ValidateLinearSelection(term, new TilPoint(5, 0), new TilPoint(5, 5));

        // Case 4: move out of the bottom boundary.
        term.SetSelectionEnd(new TilPoint(5, 20));
        ValidateLinearSelection(term, new TilPoint(5, 5), new TilPoint(5, 9));
    }

    [TestMethod]
    public void SelectBoxArea()
    {
        var term = CreateTerminal(100, 100, 0);

        // Simulate Alt+click at (x,y) = (5,10)
        int rowValue = 10;
        term.SetSelectionAnchor(new TilPoint(5, rowValue));
        term.SetBlockSelection(true);

        // Simulate move to (x,y) = (15,20)
        term.SetSelectionEnd(new TilPoint(15, 20));

        var selectionSpans = term.GetSelectionSpans();

        Assert.HasCount(11, selectionSpans);

        foreach (var span in selectionSpans)
        {
            Assert.AreEqual(new TilPoint(5, rowValue), span.Start);
            Assert.AreEqual(new TilPoint(15, rowValue), span.End);
            rowValue++;
        }
    }

    [TestMethod]
    public void SelectAreaAfterScroll()
    {
        const int SCROLLBACK_LINES = 100;
        var term = CreateTerminal(120, 30, SCROLLBACK_LINES);

        // Simulate a content-initiated scroll down by 15 lines.
        const int CONTENT_SCROLL_LINES = 15;
        term.SetViewportPosition(new TilPoint(0, CONTENT_SCROLL_LINES));

        // Simulate click at (x,y) = (5,10)
        int rowValue = 10;
        term.SetSelectionAnchor(new TilPoint(5, rowValue));

        // Simulate move to (x,y) = (15,20)
        term.SetSelectionEnd(new TilPoint(15, 20));

        ValidateLinearSelection(term, new TilPoint(5, CONTENT_SCROLL_LINES + rowValue), new TilPoint(15, CONTENT_SCROLL_LINES + 20));

        // Simulate a user-initiated scroll up to line 10 (not up by 10 lines).
        const int USER_SCROLL_VIEWPORT_TOP = 10;
        term.UserScrollViewport(USER_SCROLL_VIEWPORT_TOP);

        // Simulate click at (x,y) = (5,10)
        term.SetSelectionAnchor(new TilPoint(5, rowValue));

        // Simulate move to (x,y) = (15,20)
        term.SetSelectionEnd(new TilPoint(15, 20));

        ValidateLinearSelection(term, new TilPoint(5, USER_SCROLL_VIEWPORT_TOP + rowValue), new TilPoint(15, USER_SCROLL_VIEWPORT_TOP + 20));
    }

    [TestMethod]
    public void SelectWideGlyph_Trailing()
    {
        var term = CreateTerminal(100, 100, 0);

        // The burrito emoji, encoded in UTF-16 as the buffer needs it.
        const string BURRITO = "\uD83C\uDF2F";

        // Insert wide glyph at position (4,10)
        GetTextBuffer(term).Cursor.SetPosition(new TilPoint(4, 10));
        term.Write(BURRITO);

        // Simulate click at (x,y) = (5,10)
        term.SetSelectionAnchor(new TilPoint(5, 10));

        // The selection expands one to the left to get the leading half of the wide glyph.
        ValidateLinearSelection(term, new TilPoint(4, 10), new TilPoint(6, 10));
    }

    [TestMethod]
    public void SelectWideGlyph_Leading()
    {
        var term = CreateTerminal(100, 100, 0);

        const string BURRITO = "\uD83C\uDF2F";

        // Insert wide glyph at position (4,10)
        GetTextBuffer(term).Cursor.SetPosition(new TilPoint(4, 10));
        term.Write(BURRITO);

        // Simulate click at (x,y) = (4,10)
        term.SetSelectionAnchor(new TilPoint(4, 10));

        // The selection clamps to the left side of the glyph and stays degenerate.
        ValidateLinearSelection(term, new TilPoint(4, 10), new TilPoint(4, 10));
    }

    [TestMethod]
    public void SelectWideGlyphsInBoxSelection()
    {
        var term = CreateTerminal(100, 100, 0);

        const string BURRITO = "\uD83C\uDF2F";

        // Insert wide glyph at position (4,10)
        GetTextBuffer(term).Cursor.SetPosition(new TilPoint(4, 10));
        term.Write(BURRITO);

        // Insert wide glyph at position (7,11)
        GetTextBuffer(term).Cursor.SetPosition(new TilPoint(7, 11));
        term.Write(BURRITO);

        // The boundaries of the selection cut through the middle of the burritos, but the selection expands to
        // encompass each burrito entirely. A is the anchor (5,8), B the end (8,12), and the burritos are at (4,10) and (7,11).

        // Simulate Alt+click at (x,y) = (5,8)
        term.SetSelectionAnchor(new TilPoint(5, 8));
        term.SetBlockSelection(true);

        // Simulate move to (x,y) = (8,12)
        term.SetSelectionEnd(new TilPoint(8, 12));

        var selectionSpans = term.GetSelectionSpans();

        Assert.HasCount(5, selectionSpans);

        int rowValue = 8;
        foreach (var span in selectionSpans)
        {
            if (rowValue == 10)
            {
                Assert.AreEqual(new TilPoint(4, rowValue), span.Start);
                Assert.AreEqual(new TilPoint(8, rowValue), span.End);
            }
            else if (rowValue == 11)
            {
                Assert.AreEqual(new TilPoint(5, rowValue), span.Start);
                Assert.AreEqual(new TilPoint(9, rowValue), span.End);
            }
            else
            {
                Assert.AreEqual(new TilPoint(5, rowValue), span.Start);
                Assert.AreEqual(new TilPoint(8, rowValue), span.End);
            }

            rowValue++;
        }
    }

    [TestMethod]
    public void DoubleClick_GeneralCase()
    {
        var term = CreateTerminal(100, 100, 0);
        UseMockSettings(term);

        // Insert text at position (4,10)
        const string TEXT = "doubleClickMe";
        GetTextBuffer(term).Cursor.SetPosition(new TilPoint(4, 10));
        term.Write(TEXT);

        // Simulate double click at (x,y) = (5,10)
        term.MultiClickSelection(new TilPoint(5, 10), SelectionExpansion.Word);

        ValidateLinearSelection(term, new TilPoint(4, 10), new TilPoint(4 + TEXT.Length, 10));
    }

    [TestMethod]
    public void DoubleClick_Delimiter()
    {
        var term = CreateTerminal(100, 100, 0);
        UseMockSettings(term);

        // Simulate click at (x,y) = (5,10)
        term.MultiClickSelection(new TilPoint(5, 10), SelectionExpansion.Word);

        ValidateLinearSelection(term, new TilPoint(0, 10), new TilPoint(term.GetViewport().RightExclusive, 10));
    }

    [TestMethod]
    public void DoubleClick_DelimiterClass()
    {
        var term = CreateTerminal(100, 100, 0);
        UseMockSettings(term);

        // Insert text at position (4,10)
        const string TEXT = "C:\\Terminal>";
        GetTextBuffer(term).Cursor.SetPosition(new TilPoint(4, 10));
        term.Write(TEXT);

        // Simulate click at (x,y) = (15,10), over the '>' char.
        term.MultiClickSelection(new TilPoint(15, 10), SelectionExpansion.Word);

        // "Terminal" is in class 2, ":" and ">" are in class 1, and the white space to the right of the ">" is in
        // class 0. Double-clicking the ">" only highlights that cell.
        ValidateLinearSelection(term, new TilPoint(15, 10), new TilPoint(16, 10));
    }

    [TestMethod]
    public void DoubleClickDrag_Right()
    {
        var term = CreateTerminal(100, 100, 0);
        UseMockSettings(term);

        // Insert text at position (4,10)
        const string TEXT = "doubleClickMe dragThroughHere";
        GetTextBuffer(term).Cursor.SetPosition(new TilPoint(4, 10));
        term.Write(TEXT);

        // Simulate double click at (x,y) = (5,10)
        term.MultiClickSelection(new TilPoint(5, 10), SelectionExpansion.Word);

        // Simulate move to (x,y) = (21,10), inside "dragThroughHere".
        term.SetSelectionEnd(new TilPoint(21, 10));

        ValidateLinearSelection(term, new TilPoint(4, 10), new TilPoint(33, 10));
    }

    [TestMethod]
    public void DoubleClickDrag_Left()
    {
        var term = CreateTerminal(100, 100, 0);
        UseMockSettings(term);

        // Insert text at position (4,10)
        const string TEXT = "doubleClickMe dragThroughHere";
        GetTextBuffer(term).Cursor.SetPosition(new TilPoint(4, 10));
        term.Write(TEXT);

        // Simulate double click at (x,y) = (21,10)
        term.MultiClickSelection(new TilPoint(21, 10), SelectionExpansion.Word);

        // Simulate move to (x,y) = (5,10), inside "doubleClickMe".
        term.SetSelectionEnd(new TilPoint(5, 10));

        ValidateLinearSelection(term, new TilPoint(4, 10), new TilPoint(33, 10));
    }

    [TestMethod]
    public void TripleClick_GeneralCase()
    {
        var term = CreateTerminal(100, 100, 0);

        // Simulate click at (x,y) = (5,10)
        term.MultiClickSelection(new TilPoint(5, 10), SelectionExpansion.Line);

        ValidateLinearSelection(term, new TilPoint(0, 10), new TilPoint(term.GetViewport().RightExclusive, 10));
    }

    [TestMethod]
    public void TripleClick_WrappedLine()
    {
        var term = CreateTerminal(10, 5, 0);
        term.Write("ABCDEFGHIJKLMNOPQRSTUVWXYZ");

        // Simulate click at (x,y) = (3,1)
        term.MultiClickSelection(new TilPoint(3, 1), SelectionExpansion.Line);

        ValidateLinearSelection(term, new TilPoint(0, 0), new TilPoint(term.GetViewport().RightExclusive, 2));
    }

    [TestMethod]
    public void TripleClickDrag_Horizontal()
    {
        var term = CreateTerminal(100, 100, 0);

        // Simulate click at (x,y) = (5,10)
        term.MultiClickSelection(new TilPoint(5, 10), SelectionExpansion.Line);

        // Simulate move to (x,y) = (7,10)
        term.SetSelectionEnd(new TilPoint(7, 10));

        ValidateLinearSelection(term, new TilPoint(0, 10), new TilPoint(term.GetViewport().RightExclusive, 10));
    }

    [TestMethod]
    public void TripleClickDrag_Vertical()
    {
        var term = CreateTerminal(100, 100, 0);

        // Simulate click at (x,y) = (5,10)
        term.MultiClickSelection(new TilPoint(5, 10), SelectionExpansion.Line);

        // Simulate move to (x,y) = (5,11)
        term.SetSelectionEnd(new TilPoint(5, 11));

        ValidateLinearSelection(term, new TilPoint(0, 10), new TilPoint(term.GetViewport().RightExclusive, 11));
    }

    [TestMethod]
    public void ShiftClick()
    {
        var term = CreateTerminal(100, 100, 0);
        UseMockSettings(term);

        // Insert text at position (4,10)
        const string TEXT = "doubleClickMe dragThroughHere anotherWord";
        GetTextBuffer(term).Cursor.SetPosition(new TilPoint(4, 10));
        term.Write(TEXT);

        // Step 1: create a selection on "doubleClickMe" with a double click at (x,y) = (5,10).
        term.MultiClickSelection(new TilPoint(5, 10), SelectionExpansion.Word);
        ValidateLinearSelection(term, new TilPoint(4, 10), new TilPoint(17, 10));

        // Step 2: Shift+Click at (21,10), in "dragThroughHere", selects "doubleClickMe drag".
        term.SetSelectionEnd(new TilPoint(21, 10), SelectionExpansion.Char);
        ValidateLinearSelection(term, new TilPoint(4, 10), new TilPoint(22, 10));

        // Step 3: Shift+DoubleClick at (21,10) selects "doubleClickMe dragThroughHere".
        term.SetSelectionEnd(new TilPoint(21, 10), SelectionExpansion.Word);
        ValidateLinearSelection(term, new TilPoint(4, 10), new TilPoint(33, 10));

        // Step 4: Shift+TripleClick at (21,10) selects through the end of the line (the boundary).
        term.SetSelectionEnd(new TilPoint(21, 10), SelectionExpansion.Line);
        ValidateLinearSelection(term, new TilPoint(4, 10), new TilPoint(100, 10));

        // Step 5: Shift+DoubleClick at (21,10) again; the end is exclusive, so it points after "dragThroughHere".
        term.SetSelectionEnd(new TilPoint(21, 10), SelectionExpansion.Word);
        ValidateLinearSelection(term, new TilPoint(4, 10), new TilPoint(33, 10));

        // Step 6: drag past "dragThroughHere" to (35,10). A double click precedes, so it stays in word expansion mode.
        term.SetSelectionEnd(new TilPoint(35, 10));
        ValidateLinearSelection(term, new TilPoint(4, 10), new TilPoint(45, 10));

        // Step 7: drag back to "dragThroughHere" at (21,10), still in word expansion mode.
        term.SetSelectionEnd(new TilPoint(21, 10));
        ValidateLinearSelection(term, new TilPoint(4, 10), new TilPoint(33, 10));

        // Step 8: drag within "dragThroughHere" to (25,10); the whole word stays selected.
        term.SetSelectionEnd(new TilPoint(25, 10));
        ValidateLinearSelection(term, new TilPoint(4, 10), new TilPoint(33, 10));
    }

    [TestMethod]
    public void Pivot()
    {
        var term = CreateTerminal(100, 100, 0);

        // Step 1: create a selection from (10,10) to (20,10) inclusive.
        term.SelectNewRegion(new TilPoint(10, 10), new TilPoint(20, 10));
        ValidateLinearSelection(term, new TilPoint(10, 10), new TilPoint(21, 10));

        // Step 2: drag to (5,10). The pivot is (10,10); the end is generally exclusive, but behind the pivot it is inclusive.
        term.SetSelectionEnd(new TilPoint(5, 10));
        ValidateLinearSelection(term, new TilPoint(5, 10), new TilPoint(10, 10));

        // Step 3: drag back to (20,10). The pivot is still (10,10).
        term.SetSelectionEnd(new TilPoint(20, 10));
        ValidateLinearSelection(term, new TilPoint(10, 10), new TilPoint(20, 10));

        // Step 4: Shift+Click at (5,10). The pivot is still (10,10).
        term.SetSelectionEnd(new TilPoint(5, 10), SelectionExpansion.Char);
        ValidateLinearSelection(term, new TilPoint(5, 10), new TilPoint(10, 10));

        // Step 5: Shift+Click back at (20,10). The pivot is still (10,10); Shift+Click makes the end inclusive.
        term.SetSelectionEnd(new TilPoint(20, 10), SelectionExpansion.Char);
        ValidateLinearSelection(term, new TilPoint(10, 10), new TilPoint(21, 10));
    }
}
