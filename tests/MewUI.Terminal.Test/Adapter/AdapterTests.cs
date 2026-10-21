// Ported from Windows Terminal src/terminal/adapter/ut_adapter/adapterTest.cpp (MIT).
// Not ported, because the extension deliberately leaves the feature out: SoftFontSizeDetection (DRCS soft fonts),
// MacroDefinitions and MacroInvokes (DECDMAC and DECINVM).
// Ported in part: DeviceStatus_MacroSpaceReportTest and DeviceStatus_MemoryChecksumReportTest (the reports are fixed
// here, so the cases that define macros are left out), RequestPrivateModeTests (mode 9001, win32-input-mode, is left
// out) and TogglingC1ParserMode (the code page checks are left out, only the C1 toggling is checked).
// This file holds the setup and the cursor, mode, margin and title tests; the other tests are in AdapterTests.*.cs.

using Aprillz.MewUI.Terminal.Adapter;
using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Input;
using Aprillz.MewUI.Terminal.Parser;
using Aprillz.MewUI.Terminal.Types;

namespace MewUI.Terminal.Test.Adapter;

[TestClass]
public sealed partial class AdapterTests
{
    private const string CSI = "\u001b[";
    private const string DCS = "\u001bP";
    private const string OSC = "\u001b]";
    private const string ST = "\u001b\\";

    private TerminalInput _terminalInput = null!;
    private TestGetSet _testGetSet = null!;
    private AdaptDispatch _dispatch = null!;
    private StateMachine _stateMachine = null!;

    [TestInitialize]
    public void Setup()
    {
        _testGetSet = new TestGetSet();
        _terminalInput = new TerminalInput();
        _dispatch = new AdaptDispatch(_testGetSet, _testGetSet.Renderer, _testGetSet.RenderSettings, _terminalInput);
        _stateMachine = new StateMachine(new OutputStateMachineEngine(_dispatch));
        _testGetSet.Machine = _stateMachine;
    }

    [TestMethod]
    [DataRow((int)CursorDirection.Up)]
    [DataRow((int)CursorDirection.Down)]
    [DataRow((int)CursorDirection.Right)]
    [DataRow((int)CursorDirection.Left)]
    [DataRow((int)CursorDirection.NextLine)]
    [DataRow((int)CursorDirection.PrevLine)]
    public void CursorMovementTest(int directionValue)
    {
        var direction = (CursorDirection)directionValue;
        Action<int> moveFunc = direction switch
        {
            CursorDirection.Up => _dispatch.CursorUp,
            CursorDirection.Down => _dispatch.CursorDown,
            CursorDirection.Right => _dispatch.CursorForward,
            CursorDirection.Left => _dispatch.CursorBackward,
            CursorDirection.NextLine => _dispatch.CursorNextLine,
            _ => _dispatch.CursorPrevLine,
        };

        // Test 1: the cursor does not move when placed in the corner of the viewport.
        _testGetSet.PrepData(direction);
        moveFunc(1);
        _testGetSet.ValidateExpectedCursorPos();

        // Test 1b: next/prev line move to the left of the line when the cursor cannot move lower/higher.
        bool doTest1b = false;
        switch (direction)
        {
            case CursorDirection.NextLine:
                _testGetSet.PrepData(CursorX.Right, CursorY.Bottom);
                doTest1b = true;
                break;
            case CursorDirection.PrevLine:
                _testGetSet.PrepData(CursorX.Right, CursorY.Top);
                doTest1b = true;
                break;
        }

        if (doTest1b)
        {
            _testGetSet.ExpectedCursorPos = _testGetSet.ExpectedCursorPos with { X = 0 };
            moveFunc(1);
            _testGetSet.ValidateExpectedCursorPos();
        }

        // Test 2: the cursor moves 1 in the right direction from the middle of the viewport.
        _testGetSet.PrepData(CursorX.XCenter, CursorY.YCenter);
        var expected = _testGetSet.ExpectedCursorPos;
        switch (direction)
        {
            case CursorDirection.Up:
                expected = expected with { Y = expected.Y - 1 };
                break;
            case CursorDirection.Down:
                expected = expected with { Y = expected.Y + 1 };
                break;
            case CursorDirection.Right:
                expected = expected with { X = expected.X + 1 };
                break;
            case CursorDirection.Left:
                expected = expected with { X = expected.X - 1 };
                break;
            case CursorDirection.NextLine:
                expected = new TilPoint(0, expected.Y + 1);
                break;
            case CursorDirection.PrevLine:
                expected = new TilPoint(0, expected.Y - 1);
                break;
        }

        _testGetSet.ExpectedCursorPos = expected;
        moveFunc(1);
        _testGetSet.ValidateExpectedCursorPos();

        // Test 3: the cursor stops at the viewport edge when moved beyond it.
        _testGetSet.PrepData(CursorX.XCenter, CursorY.YCenter);
        expected = _testGetSet.ExpectedCursorPos;
        switch (direction)
        {
            case CursorDirection.Up:
                expected = expected with { Y = _testGetSet.Viewport.Top };
                break;
            case CursorDirection.Down:
                expected = expected with { Y = _testGetSet.Viewport.Bottom - 1 };
                break;
            case CursorDirection.Right:
                expected = expected with { X = _testGetSet.ScreenBuffer.Size.Dimensions.Width - 1 };
                break;
            case CursorDirection.Left:
                expected = expected with { X = 0 };
                break;
            case CursorDirection.NextLine:
                expected = new TilPoint(0, _testGetSet.Viewport.Bottom - 1);
                break;
            case CursorDirection.PrevLine:
                expected = new TilPoint(0, _testGetSet.Viewport.Top);
                break;
        }

        _testGetSet.ExpectedCursorPos = expected;
        moveFunc(100);
        _testGetSet.ValidateExpectedCursorPos();
    }

    [TestMethod]
    public void CursorPositionTest()
    {
        // Test 1: place the cursor inside the viewport, from the top left to the middle.
        _testGetSet.PrepData(CursorX.Left, CursorY.Top);

        int column = (_testGetSet.Viewport.Right - _testGetSet.Viewport.Left) / 2;
        int row = (_testGetSet.Viewport.Bottom - _testGetSet.Viewport.Top) / 2;

        // The X coordinate is unaffected by the viewport.
        _testGetSet.ExpectedCursorPos = new TilPoint(column - 1, _testGetSet.Viewport.Top + (row - 1));

        _dispatch.CursorPosition(row, column);
        _testGetSet.ValidateExpectedCursorPos();

        // Test 2: move to 0, 0 (1, 1 in VT terms).
        _testGetSet.PrepData(CursorX.Right, CursorY.Bottom);
        _testGetSet.ExpectedCursorPos = new TilPoint(0, _testGetSet.Viewport.Top);

        _dispatch.CursorPosition(1, 1);
        _testGetSet.ValidateExpectedCursorPos();

        // Test 3: moving beyond the rectangle (down/right too far) is bounded back in.
        _testGetSet.PrepData(CursorX.Left, CursorY.Top);

        column = _testGetSet.ScreenBuffer.Size.Dimensions.Width * 2;
        row = (_testGetSet.Viewport.Bottom - _testGetSet.Viewport.Top) * 2;

        _testGetSet.ExpectedCursorPos = new TilPoint(_testGetSet.ScreenBuffer.Size.Dimensions.Width - 1, _testGetSet.Viewport.Bottom - 1);

        _dispatch.CursorPosition(row, column);
        _testGetSet.ValidateExpectedCursorPos();
    }

    [TestMethod]
    [DataRow((int)AbsolutePosition.CursorHorizontal)]
    [DataRow((int)AbsolutePosition.VerticalLine)]
    public void CursorSingleDimensionMoveTest(int directionValue)
    {
        var direction = (AbsolutePosition)directionValue;
        _testGetSet.PrepData();

        int rangeEnd;
        int rangeStart;
        Action<int> moveFunc;
        Func<TilPoint, int, TilPoint> setExpected;

        if (direction == AbsolutePosition.CursorHorizontal)
        {
            rangeEnd = _testGetSet.ScreenBuffer.Size.Dimensions.Width;
            rangeStart = 0;
            moveFunc = _dispatch.CursorHorizontalPositionAbsolute;
            setExpected = (position, coordinate) => position with { X = coordinate };
        }
        else
        {
            rangeEnd = _testGetSet.Viewport.Bottom;
            rangeStart = _testGetSet.Viewport.Top;
            moveFunc = _dispatch.VerticalLinePositionAbsolute;
            setExpected = (position, coordinate) => position with { Y = coordinate };
        }

        // Test 1: place the cursor inside the viewport, from the top left to the middle.
        _testGetSet.PrepData(CursorX.Left, CursorY.Top);
        int value = (rangeEnd - rangeStart) / 2;
        _testGetSet.ExpectedCursorPos = setExpected(_testGetSet.ExpectedCursorPos, rangeStart + (value - 1));
        moveFunc(value);
        _testGetSet.ValidateExpectedCursorPos();

        // Test 2: move to 0 (1 in VT terms).
        _testGetSet.PrepData(CursorX.Right, CursorY.Bottom);
        _testGetSet.ExpectedCursorPos = setExpected(_testGetSet.ExpectedCursorPos, rangeStart);
        moveFunc(1);
        _testGetSet.ValidateExpectedCursorPos();

        // Test 3: moving beyond the rectangle is bounded back in.
        _testGetSet.PrepData(CursorX.Left, CursorY.Top);
        value = (rangeEnd - rangeStart) * 2;
        _testGetSet.ExpectedCursorPos = setExpected(_testGetSet.ExpectedCursorPos, rangeEnd - 1);
        moveFunc(value);
        _testGetSet.ValidateExpectedCursorPos();
    }

    [TestMethod]
    public void CursorSaveRestoreTest()
    {
        // Test 1: restoring with no saved data moves to the top left corner, the default position.
        _testGetSet.PrepData(CursorX.Left, CursorY.Top);
        var expectedPosition = _testGetSet.ExpectedCursorPos;

        _testGetSet.PrepData(CursorX.XCenter, CursorY.YCenter);
        _testGetSet.ExpectedCursorPos = expectedPosition;

        // Attributes are restored to the defaults.
        _testGetSet.ExpectedAttribute = default;

        _dispatch.CursorRestoreState();
        _testGetSet.ValidateExpectedCursorPos();
        _testGetSet.ValidateExpectedAttributes();

        // Test 2: save in the center, move to a corner, restore: the cursor comes back to the center.
        _testGetSet.PrepData(CursorX.XCenter, CursorY.YCenter);
        _dispatch.CursorSaveState();
        _testGetSet.ValidateExpectedCursorPos();

        expectedPosition = _testGetSet.ExpectedCursorPos;

        _testGetSet.PrepData(CursorX.Left, CursorY.Bottom);
        _testGetSet.ExpectedCursorPos = expectedPosition;

        _dispatch.CursorRestoreState();
        _testGetSet.ValidateExpectedCursorPos();
        _testGetSet.ValidateExpectedAttributes();
    }

    [TestMethod]
    [DataRow(true, true)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(false, false)]
    public void CursorHideShowTest(bool startingVisible, bool endingVisible)
    {
        _testGetSet.PrepData();
        _testGetSet.ScreenBuffer.Cursor.SetIsVisible(startingVisible);
        if (endingVisible)
        {
            _dispatch.SetMode(ModeParams.DECTCEM_TextCursorEnableMode);
        }
        else
        {
            _dispatch.ResetMode(ModeParams.DECTCEM_TextCursorEnableMode);
        }

        Assert.AreEqual(endingVisible, _testGetSet.ScreenBuffer.Cursor.IsVisible);
    }

    [TestMethod]
    public void CursorKeysModeTest()
    {
        _terminalInput.SetInputMode(TerminalInput.Mode.CursorKey, true);

        // Test 1: application mode = false.
        _dispatch.ResetMode(ModeParams.DECCKM_CursorKeysMode);
        Assert.IsFalse(_terminalInput.GetInputMode(TerminalInput.Mode.CursorKey));

        // Test 2: application mode = true.
        _dispatch.SetMode(ModeParams.DECCKM_CursorKeysMode);
        Assert.IsTrue(_terminalInput.GetInputMode(TerminalInput.Mode.CursorKey));
    }

    [TestMethod]
    public void KeypadModeTest()
    {
        _terminalInput.SetInputMode(TerminalInput.Mode.Keypad, true);

        // Test 1: application mode = false.
        _dispatch.SetKeypadMode(false);
        Assert.IsFalse(_terminalInput.GetInputMode(TerminalInput.Mode.Keypad));

        // Test 2: application mode = true.
        _dispatch.SetKeypadMode(true);
        Assert.IsTrue(_terminalInput.GetInputMode(TerminalInput.Mode.Keypad));
    }

    [TestMethod]
    public void AnsiModeTest()
    {
        _stateMachine.SetParserMode(StateMachine.Mode.Ansi, false);

        // Test 1: ansi mode = true.
        _dispatch.SetAnsiMode(true);
        Assert.IsTrue(_stateMachine.GetParserMode(StateMachine.Mode.Ansi));

        // Test 2: ansi mode = false.
        _dispatch.SetAnsiMode(false);
        Assert.IsFalse(_stateMachine.GetParserMode(StateMachine.Mode.Ansi));
    }

    [TestMethod]
    public void AllowBlinkingTest()
    {
        _testGetSet.PrepData();

        // Test 1: enable blinking = true.
        _testGetSet.ScreenBuffer.Cursor.SetIsBlinking(false);
        _dispatch.SetMode(ModeParams.ATT610_StartCursorBlink);
        Assert.IsTrue(_testGetSet.ScreenBuffer.Cursor.IsBlinking);

        // Test 2: enable blinking = false.
        _testGetSet.ScreenBuffer.Cursor.SetIsBlinking(true);
        _dispatch.ResetMode(ModeParams.ATT610_StartCursorBlink);
        Assert.IsFalse(_testGetSet.ScreenBuffer.Cursor.IsBlinking);
    }

    [TestMethod]
    public void ScrollMarginsTest()
    {
        _testGetSet.ScreenBuffer = new TextBuffer(new TilSize(100, 600), default, 0, false, _testGetSet.Renderer);
        _testGetSet.Viewport.Right = 8;
        _testGetSet.Viewport.Bottom = 8;
        int screenHeight = _testGetSet.Viewport.Bottom - _testGetSet.Viewport.Top;
        var unset = (0, 0);

        // Test 1: having both values is valid.
        _dispatch.SetTopBottomScrollingMargins(2, 6);
        var margins = DispatchInspector.VerticalMargins(_dispatch);
        Assert.AreEqual(2, margins.Top + 1);
        Assert.AreEqual(6, margins.Bottom + 1);

        // Test 2: having only the top is valid.
        _dispatch.SetTopBottomScrollingMargins(7, 0);
        margins = DispatchInspector.VerticalMargins(_dispatch);
        Assert.AreEqual(7, margins.Top + 1);
        Assert.AreEqual(screenHeight, margins.Bottom + 1);

        // Test 3: having only the bottom is valid.
        _dispatch.SetTopBottomScrollingMargins(0, 7);
        margins = DispatchInspector.VerticalMargins(_dispatch);
        Assert.AreEqual(1, margins.Top + 1);
        Assert.AreEqual(7, margins.Bottom + 1);

        // Test 4: having no values is valid.
        _dispatch.SetTopBottomScrollingMargins(0, 0);
        Assert.AreEqual(unset, DispatchInspector.VerticalMargins(_dispatch));

        // Test 5: having both values but bad bounds has no effect.
        DispatchInspector.ClearVerticalMargins(_dispatch);
        _dispatch.SetTopBottomScrollingMargins(7, 3);
        Assert.AreEqual(unset, DispatchInspector.VerticalMargins(_dispatch));

        // Test 6: setting the margins to (0, height) clears them.
        _dispatch.SetTopBottomScrollingMargins(2, 6);
        _dispatch.SetTopBottomScrollingMargins(0, screenHeight);
        Assert.AreEqual(unset, DispatchInspector.VerticalMargins(_dispatch));

        // Test 7: setting the margins to (1, height) clears them.
        _dispatch.SetTopBottomScrollingMargins(2, 6);
        _dispatch.SetTopBottomScrollingMargins(1, screenHeight);
        Assert.AreEqual(unset, DispatchInspector.VerticalMargins(_dispatch));

        // Test 8: setting the margins to (1, 0) clears them.
        _dispatch.SetTopBottomScrollingMargins(2, 6);
        _dispatch.SetTopBottomScrollingMargins(1, 0);
        Assert.AreEqual(unset, DispatchInspector.VerticalMargins(_dispatch));

        // Test 9: top and bottom margins that are the same have no effect.
        DispatchInspector.ClearVerticalMargins(_dispatch);
        _dispatch.SetTopBottomScrollingMargins(4, 4);
        Assert.AreEqual(unset, DispatchInspector.VerticalMargins(_dispatch));

        // Test 10: a top margin out of bounds has no effect.
        DispatchInspector.ClearVerticalMargins(_dispatch);
        _dispatch.SetTopBottomScrollingMargins(screenHeight + 1, screenHeight + 10);
        Assert.AreEqual(unset, DispatchInspector.VerticalMargins(_dispatch));

        // Test 11: a bottom margin out of bounds has no effect.
        DispatchInspector.ClearVerticalMargins(_dispatch);
        _dispatch.SetTopBottomScrollingMargins(1, screenHeight + 1);
        Assert.AreEqual(unset, DispatchInspector.VerticalMargins(_dispatch));
    }

    [TestMethod]
    public void LineFeedTest()
    {
        _testGetSet.PrepData();
        var cursor = _testGetSet.ScreenBuffer.Cursor;

        // Test 1: line feed without carriage return.
        cursor.SetPosition(new TilPoint(10, 0));
        _dispatch.LineFeed(LineFeedType.WithoutReturn);
        Assert.AreEqual(new TilPoint(10, 1), cursor.Position);

        // Test 2: line feed with carriage return.
        cursor.SetPosition(new TilPoint(10, 0));
        _dispatch.LineFeed(LineFeedType.WithReturn);
        Assert.AreEqual(new TilPoint(0, 1), cursor.Position);

        // Test 3: line feed depends on the mode, and the mode is reset.
        _testGetSet.SetSystemMode(ITerminalApi.Mode.LineFeed, false);
        cursor.SetPosition(new TilPoint(10, 0));
        _dispatch.LineFeed(LineFeedType.DependsOnMode);
        Assert.AreEqual(new TilPoint(10, 1), cursor.Position);

        // Test 4: line feed depends on the mode, and the mode is set.
        _testGetSet.SetSystemMode(ITerminalApi.Mode.LineFeed, true);
        cursor.SetPosition(new TilPoint(10, 0));
        _dispatch.LineFeed(LineFeedType.DependsOnMode);
        Assert.AreEqual(new TilPoint(0, 1), cursor.Position);
    }

    [TestMethod]
    public void SetConsoleTitleTest()
    {
        // Test 1: set the title to a non-empty one.
        _testGetSet.SetWindowTitleResult = true;
        _testGetSet.ExpectedWindowTitle = "Foo bar";
        _dispatch.SetWindowTitle(_testGetSet.ExpectedWindowTitle);

        // Test 2: set the title to an empty one.
        _testGetSet.SetWindowTitleResult = false;
        _testGetSet.ExpectedWindowTitle = string.Empty;
        _dispatch.SetWindowTitle(default);
    }

    [TestMethod]
    public void TestMouseModes()
    {
        // Test 1: default mouse mode.
        _terminalInput.SetInputMode(TerminalInput.Mode.DefaultMouseTracking, false);
        _dispatch.SetMode(ModeParams.VT200_MOUSE_MODE);
        Assert.IsTrue(_terminalInput.GetInputMode(TerminalInput.Mode.DefaultMouseTracking));
        _dispatch.ResetMode(ModeParams.VT200_MOUSE_MODE);
        Assert.IsFalse(_terminalInput.GetInputMode(TerminalInput.Mode.DefaultMouseTracking));

        // Test 2: UTF-8 extended mouse mode.
        _terminalInput.SetInputMode(TerminalInput.Mode.Utf8MouseEncoding, false);
        _dispatch.SetMode(ModeParams.UTF8_EXTENDED_MODE);
        Assert.IsTrue(_terminalInput.GetInputMode(TerminalInput.Mode.Utf8MouseEncoding));
        _dispatch.ResetMode(ModeParams.UTF8_EXTENDED_MODE);
        Assert.IsFalse(_terminalInput.GetInputMode(TerminalInput.Mode.Utf8MouseEncoding));

        // Test 3: SGR extended mouse mode.
        _terminalInput.SetInputMode(TerminalInput.Mode.SgrMouseEncoding, false);
        _dispatch.SetMode(ModeParams.SGR_EXTENDED_MODE);
        Assert.IsTrue(_terminalInput.GetInputMode(TerminalInput.Mode.SgrMouseEncoding));
        _dispatch.ResetMode(ModeParams.SGR_EXTENDED_MODE);
        Assert.IsFalse(_terminalInput.GetInputMode(TerminalInput.Mode.SgrMouseEncoding));

        // Test 4: button-event mouse mode.
        _terminalInput.SetInputMode(TerminalInput.Mode.ButtonEventMouseTracking, false);
        _dispatch.SetMode(ModeParams.BUTTON_EVENT_MOUSE_MODE);
        Assert.IsTrue(_terminalInput.GetInputMode(TerminalInput.Mode.ButtonEventMouseTracking));
        _dispatch.ResetMode(ModeParams.BUTTON_EVENT_MOUSE_MODE);
        Assert.IsFalse(_terminalInput.GetInputMode(TerminalInput.Mode.ButtonEventMouseTracking));

        // Test 5: any-event mouse mode.
        _terminalInput.SetInputMode(TerminalInput.Mode.AnyEventMouseTracking, false);
        _dispatch.SetMode(ModeParams.ANY_EVENT_MOUSE_MODE);
        Assert.IsTrue(_terminalInput.GetInputMode(TerminalInput.Mode.AnyEventMouseTracking));
        _dispatch.ResetMode(ModeParams.ANY_EVENT_MOUSE_MODE);
        Assert.IsFalse(_terminalInput.GetInputMode(TerminalInput.Mode.AnyEventMouseTracking));

        // Test 6: alternate scroll mouse mode.
        _terminalInput.SetInputMode(TerminalInput.Mode.AlternateScroll, false);
        _dispatch.SetMode(ModeParams.ALTERNATE_SCROLL);
        Assert.IsTrue(_terminalInput.GetInputMode(TerminalInput.Mode.AlternateScroll));
        _dispatch.ResetMode(ModeParams.ALTERNATE_SCROLL);
        Assert.IsFalse(_terminalInput.GetInputMode(TerminalInput.Mode.AlternateScroll));
    }

    [TestMethod]
    public void SetColorTableValue()
    {
        _testGetSet.PrepData();

        uint testColor = TextColor.Rgb(1, 2, 3);
        for (int index = 0; index < 256; index++)
        {
            _dispatch.SetColorTableEntry(index, testColor);
            Assert.AreEqual(testColor, _testGetSet.RenderSettings.GetColorTableEntry(index), "Color table entry " + index);
        }
    }

    [TestMethod]
    public void TogglingC1ParserMode()
    {
        _stateMachine.SetParserMode(StateMachine.Mode.AcceptC1, false);

        // 1. Accept C1 controls.
        _dispatch.AcceptC1Controls(true);
        Assert.IsTrue(_stateMachine.GetParserMode(StateMachine.Mode.AcceptC1));

        // 2. Do not accept C1 controls.
        _dispatch.AcceptC1Controls(false);
        Assert.IsFalse(_stateMachine.GetParserMode(StateMachine.Mode.AcceptC1));

        // 3. The ISO-2022 coding system turns C1 parsing on.
        _dispatch.DesignateCodingSystem(DispatchTypes.CODING_SYSTEM_ISO2022);
        Assert.IsTrue(_stateMachine.GetParserMode(StateMachine.Mode.AcceptC1));

        // 4. The UTF-8 coding system turns C1 parsing off.
        _dispatch.DesignateCodingSystem(DispatchTypes.CODING_SYSTEM_UTF8);
        Assert.IsFalse(_stateMachine.GetParserMode(StateMachine.Mode.AcceptC1));
    }

    [TestMethod]
    public void AssignUserPreferenceCharsets()
    {
        var termOutput = DispatchInspector.TermOutput(_dispatch);
        termOutput.SoftReset();

        AssertAssigned(termOutput, CharsetSize.Size94, "%5");
        AssertAssigned(termOutput, CharsetSize.Size94, "\"?");
        AssertAssigned(termOutput, CharsetSize.Size94, "\"4");
        AssertAssigned(termOutput, CharsetSize.Size94, "%0");
        AssertAssigned(termOutput, CharsetSize.Size94, "&4");
        AssertAssigned(termOutput, CharsetSize.Size96, "A");
        AssertAssigned(termOutput, CharsetSize.Size96, "B");
        AssertAssigned(termOutput, CharsetSize.Size96, "F");
        AssertAssigned(termOutput, CharsetSize.Size96, "H");
        AssertAssigned(termOutput, CharsetSize.Size96, "L");
        AssertAssigned(termOutput, CharsetSize.Size96, "M");
    }

    private void AssertAssigned(TerminalOutput termOutput, CharsetSize charsetSize, string charsetId)
    {
        var handler = _dispatch.AssignUserPreferenceCharset(charsetSize)!;
        foreach (char character in charsetId)
        {
            handler(character);
        }

        // The string terminator.
        handler('\u001b');

        Assert.AreEqual(VTID.FromString(charsetId), termOutput.UserPreferenceCharsetId, "Charset " + charsetId);
        Assert.AreEqual(charsetSize == CharsetSize.Size96 ? 96 : 94, termOutput.UserPreferenceCharsetSize, "Charset " + charsetId);
    }
}
