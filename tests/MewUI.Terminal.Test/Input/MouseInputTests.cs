// Ported from Windows Terminal src/terminal/adapter/ut_adapter/MouseInputTest.cpp (MIT).
// The WM_* message codes of the original are TerminalMouseEvent values here. The data rows carry them as ints, because
// the test methods are public and TerminalMouseEvent is internal.

using Aprillz.MewUI.Terminal.Input;
using Aprillz.MewUI.Terminal.Types;

namespace MewUI.Terminal.Test.Input;

[TestClass]
public sealed class MouseInputTests
{
    private const int SHORT_MAX = short.MaxValue;
    private const int MAX_DEFAULT_COORDINATE = 94;

    private static readonly TilPoint[] _testCoordinates =
    [
        new(0, 0),
        new(0, 1),
        new(1, 1),
        new(2, 2),
        new(94, 94), // 94+1+32 = 127
        new(95, 95), // 95+1+32 = 128, this is the ascii boundary
        new(96, 96),
        new(127, 127),
        new(128, 128),
        new(SHORT_MAX - 33, SHORT_MAX - 33),
        new(SHORT_MAX - 32, SHORT_MAX - 32),
    ];

    // The third char (the space) is replaced to test other buttons; the default is the left button down.
    private static readonly string[] _defaultTestOutput =
    [
        "\u001b[M !!",
        "\u001b[M !\"",
        "\u001b[M \"\"",
        "\u001b[M ##",
        "\u001b[M \u007f\u007f",
        "\u001b[M \u0080\u0080", // 95 - This and below will always fail for default (non utf8)
        "\u001b[M \u0081\u0081",
        "\u001b[M   ", // 127
        "\u001b[M ¡¡",
        "\u001b[M 翿翿", // FFDE
        "\u001b[M 耀耀", // This one will always fail for Default and UTF8
    ];

    // The button value fills {0}; the last char is changed to m for button up.
    private static readonly string[] _sgrTestOutput =
    [
        "\u001b[<{0};1;1M",
        "\u001b[<{0};1;2M",
        "\u001b[<{0};2;2M",
        "\u001b[<{0};3;3M",
        "\u001b[<{0};95;95M",
        "\u001b[<{0};96;96M", // 95 - This and below will always fail for default (non utf8)
        "\u001b[<{0};97;97M",
        "\u001b[<{0};128;128M", // 127
        "\u001b[<{0};129;129M",
        "\u001b[<{0};32735;32735M", // FFDE
        "\u001b[<{0};32736;32736M",
    ];

    // None, Shift, left Ctrl, right Alt, right Alt and left Ctrl.
    private static readonly int[] _modifierKeyStates = [0x0000, 0x0010, 0x0008, 0x0001, 0x0009];

    private static readonly TerminalMouseEvent[] _buttons =
    [
        TerminalMouseEvent.LeftButtonDown,
        TerminalMouseEvent.LeftButtonUp,
        TerminalMouseEvent.MiddleButtonDown,
        TerminalMouseEvent.MiddleButtonUp,
        TerminalMouseEvent.RightButtonDown,
        TerminalMouseEvent.RightButtonUp,
    ];

    private static readonly int[] _scrollDeltas = [-120, 120, -10000, 32736];

    public static IEnumerable<object[]> ButtonAndModifierData => BuildButtonData(false);

    public static IEnumerable<object[]> ButtonWithMoveAndModifierData => BuildButtonData(true);

    public static IEnumerable<object[]> ScrollDeltaAndModifierData
    {
        get
        {
            foreach (int scrollDelta in _scrollDeltas)
            {
                foreach (int modifierKeyState in _modifierKeyStates)
                {
                    yield return new object[] { scrollDelta, modifierKeyState };
                }
            }
        }
    }

    private static IEnumerable<object[]> BuildButtonData(bool includeMove)
    {
        var buttons = new List<TerminalMouseEvent>(_buttons);
        if (includeMove)
        {
            buttons.Add(TerminalMouseEvent.Move);
        }

        foreach (var button in buttons)
        {
            foreach (int modifierKeyState in _modifierKeyStates)
            {
                yield return new object[] { (int)button, modifierKeyState };
            }
        }
    }

    private static int UpdateFlag(int value, int flag, bool set)
    {
        return set ? value | flag : value & ~flag;
    }

    private static int AddModifierBits(int value, ControlKeyStates modifierKeyState)
    {
        value = UpdateFlag(value, 0x04, (modifierKeyState & ControlKeyStates.ShiftPressed) != 0);
        value = UpdateFlag(value, 0x08, (modifierKeyState & ControlKeyStates.AltPressed) != 0);
        value = UpdateFlag(value, 0x10, (modifierKeyState & ControlKeyStates.CtrlPressed) != 0);
        return value;
    }

    private static string BuildDefaultTestOutput(string template, TerminalMouseEvent button, ControlKeyStates modifierKeyState, int scrollDelta)
    {
        var chars = template.ToCharArray();
        chars[3] = GetDefaultCharFromButton(button, modifierKeyState, scrollDelta);
        return new string(chars);
    }

    private static string BuildSgrTestOutput(string template, TerminalMouseEvent button, ControlKeyStates modifierKeyState, int scrollDelta)
    {
        var chars = string.Format(template, GetSgrCharFromButton(button, modifierKeyState, scrollDelta)).ToCharArray();
        chars[chars.Length - 1] = IsButtonUp(button) ? 'm' : 'M';
        return new string(chars);
    }

    private static char GetDefaultCharFromButton(TerminalMouseEvent button, ControlKeyStates modifierKeyState, int scrollDelta)
    {
        int value;
        switch (button)
        {
            case TerminalMouseEvent.LeftButtonDoubleClick:
            case TerminalMouseEvent.LeftButtonDown:
                value = ' ';
                break;
            case TerminalMouseEvent.LeftButtonUp:
            case TerminalMouseEvent.MiddleButtonUp:
            case TerminalMouseEvent.RightButtonUp:
                value = '#';
                break;
            case TerminalMouseEvent.RightButtonDown:
            case TerminalMouseEvent.RightButtonDoubleClick:
                value = '"';
                break;
            case TerminalMouseEvent.MiddleButtonDown:
            case TerminalMouseEvent.MiddleButtonDoubleClick:
                value = '!';
                break;
            case TerminalMouseEvent.Wheel:
                value = '`' + (scrollDelta > 0 ? 0 : 1);
                break;
            case TerminalMouseEvent.HorizontalWheel:
                value = 'b' + (scrollDelta > 0 ? 1 : 0);
                break;
            default:
                value = 0;
                break;
        }

        return (char)AddModifierBits(value, modifierKeyState);
    }

    private static int GetSgrCharFromButton(TerminalMouseEvent button, ControlKeyStates modifierKeyState, int scrollDelta)
    {
        int result;
        switch (button)
        {
            case TerminalMouseEvent.LeftButtonDoubleClick:
            case TerminalMouseEvent.LeftButtonDown:
            case TerminalMouseEvent.LeftButtonUp:
                result = 0;
                break;
            case TerminalMouseEvent.MiddleButtonUp:
            case TerminalMouseEvent.MiddleButtonDown:
            case TerminalMouseEvent.MiddleButtonDoubleClick:
                result = 1;
                break;
            case TerminalMouseEvent.RightButtonUp:
            case TerminalMouseEvent.RightButtonDown:
            case TerminalMouseEvent.RightButtonDoubleClick:
                result = 2;
                break;
            case TerminalMouseEvent.Move:
                // Hover events, which are all encoded as moves, add 0x20.
                result = 3 + 0x20;
                break;
            case TerminalMouseEvent.Wheel:
                result = scrollDelta > 0 ? 64 : 65;
                break;
            case TerminalMouseEvent.HorizontalWheel:
                result = scrollDelta > 0 ? 67 : 66;
                break;
            default:
                result = 0;
                break;
        }

        return AddModifierBits(result, modifierKeyState);
    }

    private static bool IsButtonUp(TerminalMouseEvent button)
    {
        return button == TerminalMouseEvent.LeftButtonUp
            || button == TerminalMouseEvent.RightButtonUp
            || button == TerminalMouseEvent.MiddleButtonUp
            || button == TerminalMouseEvent.ExtraButtonUp;
    }

    private static string? HandleMouse(TerminalInput input, TilPoint coordinate, TerminalMouseEvent button, ControlKeyStates modifierKeyState, int scrollDelta)
    {
        return input.HandleMouse(coordinate.X, coordinate.Y, button, modifierKeyState, scrollDelta, new TerminalInput.MouseButtonState());
    }

    // Every test coordinate with the default (X10) encoding, which cannot report past column 94.
    private static void VerifyDefaultEncoding(TerminalInput input, TerminalMouseEvent button, ControlKeyStates modifierKeyState, int scrollDelta, int maxCoordinate)
    {
        for (int index = 0; index < _testCoordinates.Length; index++)
        {
            var coordinate = _testCoordinates[index];

            string? expected = null;
            if (coordinate.X <= maxCoordinate && coordinate.Y <= maxCoordinate)
            {
                expected = BuildDefaultTestOutput(_defaultTestOutput[index], button, modifierKeyState, scrollDelta);
            }

            Assert.AreEqual(expected, HandleMouse(input, coordinate, button, modifierKeyState, scrollDelta), $"(x,y)=({coordinate.X},{coordinate.Y}), button={button}");
        }
    }

    // Every test coordinate with the SGR encoding, which takes any coordinate.
    private static void VerifySgrEncoding(TerminalInput input, TerminalMouseEvent button, ControlKeyStates modifierKeyState, int scrollDelta, bool expectOutput)
    {
        for (int index = 0; index < _testCoordinates.Length; index++)
        {
            var coordinate = _testCoordinates[index];

            string? expected = null;
            if (expectOutput)
            {
                expected = BuildSgrTestOutput(_sgrTestOutput[index], button, modifierKeyState, scrollDelta);
            }

            Assert.AreEqual(expected, HandleMouse(input, coordinate, button, modifierKeyState, scrollDelta), $"(x,y)=({coordinate.X},{coordinate.Y}), button={button}");
        }
    }

    [TestMethod]
    [DynamicData(nameof(ButtonAndModifierData))]
    public void DefaultModeTests(int buttonValue, int modifierKeyStateValue)
    {
        var button = (TerminalMouseEvent)buttonValue;
        var modifierKeyState = (ControlKeyStates)modifierKeyStateValue;
        var input = new TerminalInput();

        Assert.IsNull(HandleMouse(input, new TilPoint(0, 0), button, modifierKeyState, 0));

        input.SetInputMode(TerminalInput.Mode.DefaultMouseTracking, true);
        VerifyDefaultEncoding(input, button, modifierKeyState, 0, MAX_DEFAULT_COORDINATE);

        input.SetInputMode(TerminalInput.Mode.ButtonEventMouseTracking, true);
        VerifyDefaultEncoding(input, button, modifierKeyState, 0, MAX_DEFAULT_COORDINATE);

        input.SetInputMode(TerminalInput.Mode.AnyEventMouseTracking, true);
        VerifyDefaultEncoding(input, button, modifierKeyState, 0, MAX_DEFAULT_COORDINATE);
    }

    [TestMethod]
    [DynamicData(nameof(ButtonAndModifierData))]
    public void Utf8ModeTests(int buttonValue, int modifierKeyStateValue)
    {
        var button = (TerminalMouseEvent)buttonValue;
        var modifierKeyState = (ControlKeyStates)modifierKeyStateValue;
        var input = new TerminalInput();

        Assert.IsNull(HandleMouse(input, new TilPoint(0, 0), button, modifierKeyState, 0));

        input.SetInputMode(TerminalInput.Mode.Utf8MouseEncoding, true);

        const int MAX_COORDINATE = SHORT_MAX - 33;

        input.SetInputMode(TerminalInput.Mode.DefaultMouseTracking, true);
        VerifyDefaultEncoding(input, button, modifierKeyState, 0, MAX_COORDINATE);

        input.SetInputMode(TerminalInput.Mode.ButtonEventMouseTracking, true);
        VerifyDefaultEncoding(input, button, modifierKeyState, 0, MAX_COORDINATE);

        input.SetInputMode(TerminalInput.Mode.AnyEventMouseTracking, true);
        VerifyDefaultEncoding(input, button, modifierKeyState, 0, MAX_COORDINATE);
    }

    [TestMethod]
    [DynamicData(nameof(ButtonWithMoveAndModifierData))]
    public void SgrModeTests(int buttonValue, int modifierKeyStateValue)
    {
        var button = (TerminalMouseEvent)buttonValue;
        var modifierKeyState = (ControlKeyStates)modifierKeyStateValue;
        var input = new TerminalInput();

        Assert.IsNull(HandleMouse(input, new TilPoint(0, 0), button, modifierKeyState, 0));

        input.SetInputMode(TerminalInput.Mode.SgrMouseEncoding, true);

        // Moves are only reported in any-event tracking.
        bool expectOutput = button != TerminalMouseEvent.Move;

        input.SetInputMode(TerminalInput.Mode.DefaultMouseTracking, true);
        VerifySgrEncoding(input, button, modifierKeyState, 0, expectOutput);

        input.SetInputMode(TerminalInput.Mode.ButtonEventMouseTracking, true);
        VerifySgrEncoding(input, button, modifierKeyState, 0, expectOutput);

        input.SetInputMode(TerminalInput.Mode.AnyEventMouseTracking, true);
        VerifySgrEncoding(input, button, modifierKeyState, 0, true);
    }

    [TestMethod]
    [DynamicData(nameof(ScrollDeltaAndModifierData))]
    public void ScrollWheelTests(int scrollDelta, int modifierKeyStateValue)
    {
        var button = TerminalMouseEvent.Wheel;
        var modifierKeyState = (ControlKeyStates)modifierKeyStateValue;
        var input = new TerminalInput();

        Assert.IsNull(HandleMouse(input, new TilPoint(0, 0), button, modifierKeyState, scrollDelta));

        // Default tracking, default encoding.
        input.SetInputMode(TerminalInput.Mode.DefaultMouseTracking, true);
        VerifyDefaultEncoding(input, button, modifierKeyState, scrollDelta, MAX_DEFAULT_COORDINATE);

        // Default tracking, UTF-8 encoding.
        input.SetInputMode(TerminalInput.Mode.Utf8MouseEncoding, true);
        VerifyDefaultEncoding(input, button, modifierKeyState, scrollDelta, SHORT_MAX - 33);

        // Default tracking, SGR encoding.
        input.SetInputMode(TerminalInput.Mode.SgrMouseEncoding, true);
        VerifySgrEncoding(input, button, modifierKeyState, scrollDelta, true);
    }

    [TestMethod]
    public void AlternateScrollModeTests()
    {
        var input = new TerminalInput();
        var origin = new TilPoint(0, 0);
        var noModifierKeys = ControlKeyStates.None;

        // Enable alternate scroll mode in the alt screen buffer.
        input.UseAlternateScreenBuffer();
        input.SetInputMode(TerminalInput.Mode.AlternateScroll, true);

        Assert.AreEqual("\u001b[A", HandleMouse(input, origin, TerminalMouseEvent.Wheel, noModifierKeys, TerminalInput.WHEEL_DELTA), "mouse wheel scrolling up");
        Assert.AreEqual("\u001b[B", HandleMouse(input, origin, TerminalMouseEvent.Wheel, noModifierKeys, -TerminalInput.WHEEL_DELTA), "mouse wheel scrolling down");
        Assert.AreEqual("\u001b[C", HandleMouse(input, origin, TerminalMouseEvent.HorizontalWheel, noModifierKeys, TerminalInput.WHEEL_DELTA), "mouse wheel scrolling right");
        Assert.AreEqual("\u001b[D", HandleMouse(input, origin, TerminalMouseEvent.HorizontalWheel, noModifierKeys, -TerminalInput.WHEEL_DELTA), "mouse wheel scrolling left");

        // Enable cursor keys mode.
        input.SetInputMode(TerminalInput.Mode.CursorKey, true);

        Assert.AreEqual("\u001bOA", HandleMouse(input, origin, TerminalMouseEvent.Wheel, noModifierKeys, TerminalInput.WHEEL_DELTA), "mouse wheel scrolling up");
        Assert.AreEqual("\u001bOB", HandleMouse(input, origin, TerminalMouseEvent.Wheel, noModifierKeys, -TerminalInput.WHEEL_DELTA), "mouse wheel scrolling down");
        Assert.AreEqual("\u001bOC", HandleMouse(input, origin, TerminalMouseEvent.HorizontalWheel, noModifierKeys, TerminalInput.WHEEL_DELTA), "mouse wheel scrolling right");
        Assert.AreEqual("\u001bOD", HandleMouse(input, origin, TerminalMouseEvent.HorizontalWheel, noModifierKeys, -TerminalInput.WHEEL_DELTA), "mouse wheel scrolling left");

        // No effect when scroll mode is disabled.
        input.UseAlternateScreenBuffer();
        input.SetInputMode(TerminalInput.Mode.AlternateScroll, false);
        Assert.IsNull(HandleMouse(input, origin, TerminalMouseEvent.Wheel, noModifierKeys, TerminalInput.WHEEL_DELTA));

        // No effect when using the main buffer.
        input.UseMainScreenBuffer();
        input.SetInputMode(TerminalInput.Mode.AlternateScroll, true);
        Assert.IsNull(HandleMouse(input, origin, TerminalMouseEvent.Wheel, noModifierKeys, TerminalInput.WHEEL_DELTA));
    }
}
