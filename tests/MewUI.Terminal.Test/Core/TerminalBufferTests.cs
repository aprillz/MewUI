// Ported from Windows Terminal src/cascadia/UnitTests_TerminalCore/TerminalBufferTests.cpp (MIT).

using Aprillz.MewUI.Terminal.Core;
using Aprillz.MewUI.Terminal.Types;

namespace MewUI.Terminal.Test.Core;

[TestClass]
public sealed class TerminalBufferTests
{
    // Many tests here expect an 80x32 terminal.
    private const int TERMINAL_VIEW_WIDTH = 80;
    private const int TERMINAL_VIEW_HEIGHT = 32;
    private const int TERMINAL_HISTORY_LENGTH = 100;

    private TerminalCore _term = null!;

    [TestInitialize]
    public void MethodSetup()
    {
        _term = new TerminalCore(true);
        _term.Create(new TilSize(TERMINAL_VIEW_WIDTH, TERMINAL_VIEW_HEIGHT), TERMINAL_HISTORY_LENGTH, new Renderer());
    }

    [TestMethod]
    public void TestSimpleBufferWriting()
    {
        var termTb = _term.MainBuffer;
        var termSm = _term.StateMachine;
        var initialView = _term.GetViewport();

        Assert.AreEqual(0, initialView.Top);
        Assert.AreEqual(32, initialView.BottomExclusive);

        termSm.ProcessString("Hello World");

        var secondView = _term.GetViewport();

        Assert.AreEqual(0, secondView.Top);
        Assert.AreEqual(32, secondView.BottomExclusive);

        TerminalTestUtils.VerifyExpectedString(termTb, "Hello World", new TilPoint(0, 0));
    }

    [TestMethod]
    public void TestWrappingCharByChar()
    {
        var termTb = _term.MainBuffer;
        var termSm = _term.StateMachine;
        var initialView = _term.GetViewport();
        var cursor = termTb.Cursor;

        int charsToWrite = TerminalTestUtils.TEST_100_CHARS_STRING.Length;

        Assert.AreEqual(0, initialView.Top);
        Assert.AreEqual(32, initialView.BottomExclusive);

        for (int index = 0; index < charsToWrite; index++)
        {
            // The printable characters other than space.
            char character = (char)(33 + (index % 94));
            termSm.ProcessCharacter(character);
        }

        var secondView = _term.GetViewport();

        Assert.AreEqual(0, secondView.Top);
        Assert.AreEqual(32, secondView.BottomExclusive);

        Assert.AreEqual(charsToWrite % initialView.Width, cursor.Position.X);
        Assert.AreEqual(1, cursor.Position.Y);

        Assert.IsTrue(termTb.GetRowByOffset(0).WasWrapForced);
        Assert.IsFalse(termTb.GetRowByOffset(1).WasWrapForced);

        TerminalTestUtils.VerifyExpectedString(termTb, TerminalTestUtils.TEST_100_CHARS_STRING, new TilPoint(0, 0));
    }

    [TestMethod]
    public void TestWrappingALongString()
    {
        var termTb = _term.MainBuffer;
        var termSm = _term.StateMachine;
        var initialView = _term.GetViewport();
        var cursor = termTb.Cursor;

        int charsToWrite = TerminalTestUtils.TEST_100_CHARS_STRING.Length;
        Assert.AreEqual(100, charsToWrite);

        Assert.AreEqual(0, initialView.Top);
        Assert.AreEqual(32, initialView.BottomExclusive);

        termSm.ProcessString(TerminalTestUtils.TEST_100_CHARS_STRING);

        var secondView = _term.GetViewport();

        Assert.AreEqual(0, secondView.Top);
        Assert.AreEqual(32, secondView.BottomExclusive);

        Assert.AreEqual(charsToWrite % initialView.Width, cursor.Position.X);
        Assert.AreEqual(1, cursor.Position.Y);

        Assert.IsTrue(termTb.GetRowByOffset(0).WasWrapForced);
        Assert.IsFalse(termTb.GetRowByOffset(1).WasWrapForced);

        TerminalTestUtils.VerifyExpectedString(termTb, TerminalTestUtils.TEST_100_CHARS_STRING, new TilPoint(0, 0));
    }

    [TestMethod]
    public void DontSnapToOutputTest()
    {
        var termTb = _term.MainBuffer;
        var termSm = _term.StateMachine;
        var initialView = _term.GetViewport();

        Assert.AreEqual(0, initialView.Top);
        Assert.AreEqual(TERMINAL_VIEW_HEIGHT, initialView.BottomExclusive);
        Assert.AreEqual(0, _term.ScrollOffset);

        // One less, so the last line feed is not printed.
        for (int index = 0; index < TERMINAL_VIEW_HEIGHT + 8 - 1; index++)
        {
            termSm.ProcessString("x\n");
        }

        var secondView = _term.GetViewport();

        Assert.AreEqual(8, secondView.Top);
        Assert.AreEqual(TERMINAL_VIEW_HEIGHT + 8, secondView.BottomExclusive);
        Assert.AreEqual(0, _term.ScrollOffset);

        // Scroll up one line.
        _term.ScrollOffset = 1;

        var thirdView = _term.GetViewport();
        Assert.AreEqual(7, thirdView.Top);
        Assert.AreEqual(TERMINAL_VIEW_HEIGHT + 7, thirdView.BottomExclusive);
        Assert.AreEqual(1, _term.ScrollOffset);

        // Printing a few lines leaves the viewport where it was.
        for (int index = 0; index < 8; index++)
        {
            termSm.ProcessString("x\n");
        }

        var fourthView = _term.GetViewport();
        Assert.AreEqual(7, fourthView.Top);
        Assert.AreEqual(TERMINAL_VIEW_HEIGHT + 7, fourthView.BottomExclusive);
        Assert.AreEqual(1 + 8, _term.ScrollOffset);

        // Print until the next line feed would circle the buffer.
        int viewBottom;
        do
        {
            termSm.ProcessString("x\n");
            viewBottom = _term.MutableViewport.BottomInclusive;
        }
        while (viewBottom < termTb.Size.BottomInclusive);

        var fifthView = _term.GetViewport();
        Assert.AreEqual(7, fifthView.Top);
        Assert.AreEqual(TERMINAL_VIEW_HEIGHT + 7, fifthView.BottomExclusive);
        Assert.AreEqual(TERMINAL_HISTORY_LENGTH - 7, _term.ScrollOffset);

        // After circling, the view sticks to where the old rows now are.
        for (int index = 0; index < 3; index++)
        {
            termSm.ProcessString("x\n");
        }

        var sixthView = _term.GetViewport();
        Assert.AreEqual(4, sixthView.Top);
        Assert.AreEqual(TERMINAL_VIEW_HEIGHT + 4, sixthView.BottomExclusive);
        Assert.AreEqual(TERMINAL_HISTORY_LENGTH - 4, _term.ScrollOffset);

        // Eventually the view is stuck at the top of the buffer.
        for (int index = 0; index < 8; index++)
        {
            termSm.ProcessString("x\n");
        }

        var seventhView = _term.GetViewport();
        Assert.AreEqual(0, seventhView.Top);
        Assert.AreEqual(TERMINAL_VIEW_HEIGHT, seventhView.BottomExclusive);
        Assert.AreEqual(TERMINAL_HISTORY_LENGTH, _term.ScrollOffset);
    }

    private void SetTabStops(List<int> columns, bool replace)
    {
        var cursor = _term.MainBuffer.Cursor;
        var termSm = _term.StateMachine;

        if (replace)
        {
            termSm.ProcessString("\u001b[3g");
        }

        foreach (int column in columns)
        {
            cursor.SetXPosition(column);
            termSm.ProcessString("\u001bH");
        }
    }

    private List<int> GetTabStops()
    {
        var columns = new List<int>();
        var termSm = _term.StateMachine;
        int lastColumn = _term.GetViewport().RightInclusive;
        var cursor = _term.MainBuffer.Cursor;

        cursor.SetPosition(new TilPoint(0, 0));
        while (true)
        {
            termSm.ProcessCharacter('\t');
            int column = cursor.Position.X;
            if (column >= lastColumn)
            {
                break;
            }

            columns.Add(column);
        }

        return columns;
    }

    [TestMethod]
    public void TestResetClearTabStops()
    {
        var termSm = _term.StateMachine;

        // Default tabs are every 8 columns.
        CollectionAssert.AreEqual(new[] { 8, 16, 24, 32, 40, 48, 56, 64, 72 }, GetTabStops());

        termSm.ProcessString("\u001b[3g");
        Assert.IsEmpty(GetTabStops());

        // RIS resets tabs to the defaults.
        termSm.ProcessString("\u001bc");
        CollectionAssert.AreEqual(new[] { 8, 16, 24, 32, 40, 48, 56, 64, 72 }, GetTabStops());
    }

    [TestMethod]
    public void TestAddTabStop()
    {
        var termSm = _term.StateMachine;
        var cursor = _term.MainBuffer.Cursor;

        termSm.ProcessString("\u001b[3g");
        var expectedStops = new List<int>();
        CollectionAssert.AreEqual(expectedStops, GetTabStops());

        // Add to an empty list.
        cursor.SetXPosition(12);
        termSm.ProcessString("\u001bH");
        expectedStops.Add(12);
        CollectionAssert.AreEqual(expectedStops, GetTabStops());

        // Add at the head.
        cursor.SetXPosition(4);
        termSm.ProcessString("\u001bH");
        expectedStops.Insert(0, 4);
        CollectionAssert.AreEqual(expectedStops, GetTabStops());

        // Add at the tail.
        cursor.SetXPosition(30);
        termSm.ProcessString("\u001bH");
        expectedStops.Add(30);
        CollectionAssert.AreEqual(expectedStops, GetTabStops());

        // Add in the middle.
        cursor.SetXPosition(24);
        termSm.ProcessString("\u001bH");
        expectedStops.Add(24);
        expectedStops.Sort();
        CollectionAssert.AreEqual(expectedStops, GetTabStops());

        // Add a duplicate.
        cursor.SetXPosition(24);
        termSm.ProcessString("\u001bH");
        CollectionAssert.AreEqual(expectedStops, GetTabStops());
    }

    [TestMethod]
    public void TestClearTabStop()
    {
        var termSm = _term.StateMachine;
        var cursor = _term.MainBuffer.Cursor;

        const string CLEAR_TAB_STOPS = "\u001b[3g";
        const string CLEAR_TAB_STOP = "\u001b[0g";
        const string ADD_TAB_STOP = "\u001bH";

        // Start with all tabs cleared.
        termSm.ProcessString(CLEAR_TAB_STOPS);
        Assert.IsEmpty(GetTabStops());

        // Clear in an empty list.
        cursor.SetXPosition(0);
        termSm.ProcessString(CLEAR_TAB_STOP);
        Assert.IsEmpty(GetTabStops(), "List should remain empty");

        // Add one stop and clear it.
        cursor.SetXPosition(0);
        termSm.ProcessString(ADD_TAB_STOP);
        termSm.ProcessString(CLEAR_TAB_STOP);
        Assert.IsEmpty(GetTabStops());

        // Add one stop and clear columns that have none.
        cursor.SetXPosition(1);
        termSm.ProcessString(ADD_TAB_STOP);

        cursor.SetXPosition(2);
        termSm.ProcessString(CLEAR_TAB_STOP);
        Assert.IsNotEmpty(GetTabStops());

        cursor.SetXPosition(0);
        termSm.ProcessString(CLEAR_TAB_STOP);
        Assert.IsNotEmpty(GetTabStops());

        termSm.ProcessString(CLEAR_TAB_STOPS);

        // Clear the head of many.
        var inputData = new List<int> { 3, 5, 6, 10, 15, 17 };
        SetTabStops(inputData, false);
        cursor.SetXPosition(inputData[0]);
        termSm.ProcessString(CLEAR_TAB_STOP);
        inputData.RemoveAt(0);
        CollectionAssert.AreEqual(inputData, GetTabStops());
        termSm.ProcessString(CLEAR_TAB_STOPS);

        // Clear the middle of many.
        inputData = new List<int> { 3, 5, 6, 10, 15, 17 };
        SetTabStops(inputData, false);
        cursor.SetXPosition(inputData[1]);
        termSm.ProcessString(CLEAR_TAB_STOP);
        inputData.RemoveAt(1);
        CollectionAssert.AreEqual(inputData, GetTabStops());
        termSm.ProcessString(CLEAR_TAB_STOPS);

        // Clear the tail of many.
        inputData = new List<int> { 3, 5, 6, 10, 15, 17 };
        SetTabStops(inputData, false);
        cursor.SetXPosition(inputData[^1]);
        termSm.ProcessString(CLEAR_TAB_STOP);
        inputData.RemoveAt(inputData.Count - 1);
        CollectionAssert.AreEqual(inputData, GetTabStops());
        termSm.ProcessString(CLEAR_TAB_STOPS);

        // Clear a column that has no stop.
        inputData = new List<int> { 3, 5, 6, 10, 15, 17 };
        SetTabStops(inputData, false);
        cursor.SetXPosition(0);
        termSm.ProcessString(CLEAR_TAB_STOP);
        CollectionAssert.AreEqual(inputData, GetTabStops());
        termSm.ProcessString(CLEAR_TAB_STOPS);
    }

    [TestMethod]
    public void TestGetForwardTab()
    {
        var termSm = _term.StateMachine;
        var initialView = _term.GetViewport();
        var cursor = _term.MainBuffer.Cursor;

        const string NEXT_FORWARD_TAB = "\u001b[I";

        var inputData = new List<int> { 3, 5, 6, 10, 15, 17 };
        SetTabStops(inputData, true);

        var screenBufferSize = initialView.Dimensions;

        cursor.SetXPosition(0);
        var expected = cursor.Position with { X = inputData[0] };
        termSm.ProcessString(NEXT_FORWARD_TAB);
        Assert.AreEqual(expected, cursor.Position, "Cursor advanced to first tab stop from sample list.");

        cursor.SetXPosition(6);
        expected = cursor.Position with { X = inputData[3] };
        termSm.ProcessString(NEXT_FORWARD_TAB);
        Assert.AreEqual(expected, cursor.Position, "Cursor advanced to middle tab stop from sample list.");

        cursor.SetXPosition(30);
        expected = cursor.Position with { X = screenBufferSize.Width - 1 };
        termSm.ProcessString(NEXT_FORWARD_TAB);
        Assert.AreEqual(expected, cursor.Position, "Cursor advanced to end of screen buffer.");

        cursor.SetXPosition(screenBufferSize.Width - 1);
        expected = cursor.Position;
        termSm.ProcessString(NEXT_FORWARD_TAB);
        Assert.AreEqual(expected, cursor.Position, "Cursor remains in rightmost column.");
    }

    [TestMethod]
    public void TestGetReverseTab()
    {
        var termSm = _term.StateMachine;
        var cursor = _term.MainBuffer.Cursor;

        const string NEXT_REVERSE_TAB = "\u001b[Z";

        var inputData = new List<int> { 3, 5, 6, 10, 15, 17 };
        SetTabStops(inputData, true);

        cursor.SetXPosition(1);
        var expected = cursor.Position with { X = 0 };
        termSm.ProcessString(NEXT_REVERSE_TAB);
        Assert.AreEqual(expected, cursor.Position, "Cursor adjusted to beginning of the buffer when it started before sample list.");

        cursor.SetXPosition(6);
        expected = cursor.Position with { X = inputData[1] };
        termSm.ProcessString(NEXT_REVERSE_TAB);
        Assert.AreEqual(expected, cursor.Position, "Cursor adjusted back one tab spot from middle of sample list.");

        cursor.SetXPosition(30);
        expected = cursor.Position with { X = inputData[^1] };
        termSm.ProcessString(NEXT_REVERSE_TAB);
        Assert.AreEqual(expected, cursor.Position, "Cursor adjusted to last item in the sample list from position beyond end.");
    }

    [TestMethod]
    public void TestURLPatternDetection()
    {
        const string BEFORE_STR = "<Before>";
        const string URL_STR = "https://www.contoso.com";
        const string AFTER_STR = "<After>";
        int urlStartX = BEFORE_STR.Length;
        int urlEndX = BEFORE_STR.Length + URL_STR.Length - 1;

        // Detection is off until settings turn it on.
        _term.DetectUrls = true;

        var termSm = _term.StateMachine;
        termSm.ProcessString(BEFORE_STR + URL_STR + AFTER_STR);
        _term.UpdatePatternsUnderLock();

        string result = _term.GetHyperlinkAtBufferPosition(new TilPoint(urlStartX - 1, 0));
        Assert.AreEqual(string.Empty, result, "URL is not detected before the actual URL.");

        result = _term.GetHyperlinkAtBufferPosition(new TilPoint(urlStartX, 0));
        Assert.AreEqual(URL_STR, result, "A URL is detected at the start position.");

        result = _term.GetHyperlinkAtBufferPosition(new TilPoint(urlEndX, 0));
        Assert.AreEqual(URL_STR, result, "A URL is detected at the end position.");

        result = _term.GetHyperlinkAtBufferPosition(new TilPoint(urlEndX + 1, 0));
        Assert.AreEqual(string.Empty, result, "URL is not detected after the actual URL.");

        // A URL longer than the terminal is wide wraps onto the next row.
        const string WRAP_BEFORE = "WRAP>";
        const string LONG_URL = "https://www.contoso.com/this-is-a-very-long-path/that-will-wrap-across-multiple-rows-in-the-terminal-buffer";
        int wrapUrlStartX = WRAP_BEFORE.Length;
        Assert.IsGreaterThan(TERMINAL_VIEW_WIDTH, WRAP_BEFORE.Length + LONG_URL.Length, "URL must exceed terminal width to wrap");

        termSm.ProcessString("\r\n\r\n");
        termSm.ProcessString(WRAP_BEFORE + LONG_URL);
        _term.UpdatePatternsUnderLock();

        int cursorRow = _term.MainBuffer.Cursor.Position.Y;
        int wrapRow0 = cursorRow - 1;
        int wrapRow1 = cursorRow;

        result = _term.GetHyperlinkAtBufferPosition(new TilPoint(wrapUrlStartX, wrapRow0));
        Assert.AreEqual(LONG_URL, result, "Full wrapped URL is returned from first row.");

        result = _term.GetHyperlinkAtBufferPosition(new TilPoint(0, wrapRow1));
        Assert.AreEqual(LONG_URL, result, "Full wrapped URL is returned from second row.");

        result = _term.GetHyperlinkAtBufferPosition(new TilPoint(wrapUrlStartX - 1, wrapRow0));
        Assert.AreEqual(string.Empty, result, "URL is not detected before the wrapped URL.");

        // URLs are still found after output pushes earlier ones into the scrollback.
        for (int index = 0; index < TERMINAL_VIEW_HEIGHT + 8; index++)
        {
            termSm.ProcessString("filler\r\n");
        }

        const string SCROLL_URL = "https://www.example.com/scrolled";
        termSm.ProcessString(SCROLL_URL);
        _term.UpdatePatternsUnderLock();

        int scrollUrlRow = _term.MainBuffer.Cursor.Position.Y;
        result = _term.GetHyperlinkAtBufferPosition(new TilPoint(0, scrollUrlRow));
        Assert.AreEqual(SCROLL_URL, result, "Correct URL is returned after scrolling.");

        // The interval API reports rows relative to the viewport.
        const string VP_URL = "https://www.example.com/viewport";
        termSm.ProcessString("\r\n");
        termSm.ProcessString(VP_URL);
        _term.UpdatePatternsUnderLock();

        int vpUrlRow = _term.MainBuffer.Cursor.Position.Y;
        int visStart = _term.GetScrollOffset();
        int viewportRow = vpUrlRow - visStart;

        var interval = _term.GetHyperlinkIntervalFromViewportPosition(new TilPoint(0, viewportRow));
        Assert.IsTrue(interval.HasValue, "Interval is found via viewport position.");
        Assert.AreEqual(viewportRow, interval.Value.Start.Y, "Interval start row is viewport-relative.");
        Assert.AreEqual(0, interval.Value.Start.X, "Interval starts at column 0.");
    }
}
