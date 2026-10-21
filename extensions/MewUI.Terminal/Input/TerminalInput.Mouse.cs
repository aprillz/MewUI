// Ported from Windows Terminal src/terminal/input/mouseInput.cpp (MIT).

using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Input;

/// <summary>A mouse event: a button going down or up, a move, or a wheel turn.</summary>
internal enum TerminalMouseEvent
{
    Move,
    LeftButtonDown,
    LeftButtonUp,
    LeftButtonDoubleClick,
    MiddleButtonDown,
    MiddleButtonUp,
    MiddleButtonDoubleClick,
    RightButtonDown,
    RightButtonUp,
    RightButtonDoubleClick,
    ExtraButtonUp,
    Wheel,
    HorizontalWheel,
}

internal sealed partial class TerminalInput
{
    /// <summary>The buttons held during a move.</summary>
    public readonly record struct MouseButtonState(bool IsLeftButtonDown, bool IsMiddleButtonDown, bool IsRightButtonDown);

    // The wheel delta of one notch.
    public const int WHEEL_DELTA = 120;

    // The default X10 encoding puts each coordinate in one byte from 32, so it cannot report past column 94 (223 - 32 - 1).
    private const int MAX_DEFAULT_COORDINATE = 94;

    /// <summary>The last reported position and button, which keep moves inside one cell from being reported twice.</summary>
    private readonly record struct MouseInputState(TilPoint LastPos, TerminalMouseEvent LastButton, int AccumulatedDelta)
    {
        public static MouseInputState Initial => new(new TilPoint(-1, -1), TerminalMouseEvent.Move, 0);
    }

    private MouseInputState _mouseInputState = MouseInputState.Initial;

    private static bool IsButtonMessage(TerminalMouseEvent button) => button switch
    {
        TerminalMouseEvent.LeftButtonDoubleClick or TerminalMouseEvent.LeftButtonDown or TerminalMouseEvent.LeftButtonUp
            or TerminalMouseEvent.MiddleButtonUp or TerminalMouseEvent.RightButtonUp or TerminalMouseEvent.RightButtonDown
            or TerminalMouseEvent.RightButtonDoubleClick or TerminalMouseEvent.MiddleButtonDown or TerminalMouseEvent.MiddleButtonDoubleClick
            or TerminalMouseEvent.Wheel or TerminalMouseEvent.HorizontalWheel => true,
        _ => false,
    };

    private static bool IsHoverMessage(TerminalMouseEvent button) => button == TerminalMouseEvent.Move;

    private static bool IsWheelMessage(TerminalMouseEvent button) => button == TerminalMouseEvent.Wheel || button == TerminalMouseEvent.HorizontalWheel;

    private static bool IsButtonUp(TerminalMouseEvent button) => button switch
    {
        TerminalMouseEvent.LeftButtonUp or TerminalMouseEvent.RightButtonUp or TerminalMouseEvent.MiddleButtonUp or TerminalMouseEvent.ExtraButtonUp => true,
        _ => false,
    };

    /// <summary>The button a move is reported with: the first one held, or a left button up for none.</summary>
    private static TerminalMouseEvent GetPressedButton(MouseButtonState state)
    {
        if (state.IsLeftButtonDown)
        {
            return TerminalMouseEvent.LeftButtonDown;
        }
        else if (state.IsMiddleButtonDown)
        {
            return TerminalMouseEvent.MiddleButtonDown;
        }
        else if (state.IsRightButtonDown)
        {
            return TerminalMouseEvent.RightButtonDown;
        }
        else
        {
            return TerminalMouseEvent.LeftButtonUp;
        }
    }

    /// <summary>The X10 button byte: button number, motion and modifier bits, offset by 32.</summary>
    private static char ButtonToXEncoding(TerminalMouseEvent button, bool isHover, ControlKeyStates modifierKeyState, int delta)
    {
        int xvalue = button switch
        {
            TerminalMouseEvent.LeftButtonDoubleClick or TerminalMouseEvent.LeftButtonDown => 0,

            // X10 reports any release as button 3.
            TerminalMouseEvent.LeftButtonUp or TerminalMouseEvent.MiddleButtonUp or TerminalMouseEvent.RightButtonUp => 3,
            TerminalMouseEvent.RightButtonDown or TerminalMouseEvent.RightButtonDoubleClick => 2,
            TerminalMouseEvent.MiddleButtonDown or TerminalMouseEvent.MiddleButtonDoubleClick => 1,
            TerminalMouseEvent.Wheel => delta > 0 ? 0x40 : 0x41,
            TerminalMouseEvent.HorizontalWheel => delta > 0 ? 0x43 : 0x42,
            _ => 0,
        };

        return (char)(' ' + AddModifierBits(xvalue, isHover, modifierKeyState));
    }

    /// <summary>The SGR (1006) button number, which reports releases by the final character instead.</summary>
    private static int ButtonToSgrEncoding(TerminalMouseEvent button, bool isHover, ControlKeyStates modifierKeyState, int delta)
    {
        int xvalue = button switch
        {
            TerminalMouseEvent.LeftButtonDoubleClick or TerminalMouseEvent.LeftButtonDown or TerminalMouseEvent.LeftButtonUp => 0,
            TerminalMouseEvent.RightButtonUp or TerminalMouseEvent.RightButtonDown or TerminalMouseEvent.RightButtonDoubleClick => 2,
            TerminalMouseEvent.MiddleButtonUp or TerminalMouseEvent.MiddleButtonDown or TerminalMouseEvent.MiddleButtonDoubleClick => 1,
            TerminalMouseEvent.Move => 3,
            TerminalMouseEvent.Wheel => delta > 0 ? 0x40 : 0x41,
            TerminalMouseEvent.HorizontalWheel => delta > 0 ? 0x43 : 0x42,
            _ => 0,
        };

        return AddModifierBits(xvalue, isHover, modifierKeyState);
    }

    private static int AddModifierBits(int xvalue, bool isHover, ControlKeyStates modifierKeyState)
    {
        if (isHover)
        {
            xvalue += 0x20;
        }

        xvalue = (modifierKeyState & ControlKeyStates.ShiftPressed) != 0 ? xvalue | 0x04 : xvalue & ~0x04;
        xvalue = (modifierKeyState & ControlKeyStates.AltPressed) != 0 ? xvalue | 0x08 : xvalue & ~0x08;
        xvalue = (modifierKeyState & ControlKeyStates.CtrlPressed) != 0 ? xvalue | 0x10 : xvalue & ~0x10;
        return xvalue;
    }

    private static char EncodeDefaultCoordinate(int coordinateValue) => (char)(coordinateValue + 32);

    public bool IsTrackingMouseInput => (_inputMode & MOUSE_TRACKING_MODES) != 0;

    /// <summary>
    /// Encodes a mouse event at a 0-based viewport cell. Wheel turns accumulate until a full notch. Returns null when the
    /// event is not for the application.
    /// </summary>
    public string? HandleMouse(int column, int row, TerminalMouseEvent button, ControlKeyStates modifierKeyState, int delta, MouseButtonState state)
    {
        // A wheel turn in the other direction starts over.
        if (Math.Sign(delta) != Math.Sign(_mouseInputState.AccumulatedDelta))
        {
            _mouseInputState = _mouseInputState with { AccumulatedDelta = 0 };
        }

        if (IsWheelMessage(button))
        {
            int accumulated = _mouseInputState.AccumulatedDelta + delta;
            _mouseInputState = _mouseInputState with { AccumulatedDelta = accumulated };
            if (Math.Abs(accumulated) < WHEEL_DELTA)
            {
                // Less than a notch is still the application's, so the view does not scroll.
                if (IsTrackingMouseInput || ShouldSendAlternateScroll(button, delta))
                {
                    return string.Empty;
                }

                return null;
            }

            _mouseInputState = _mouseInputState with { AccumulatedDelta = 0 };
        }

        if (IsTrackingMouseInput)
        {
            bool isHover = IsHoverMessage(button);
            bool isButton = IsButtonMessage(button);

            bool sameCoord = column == _mouseInputState.LastPos.X && row == _mouseInputState.LastPos.Y && _mouseInputState.LastButton == button;

            // A move reports the button held, so the encodings can tell a drag from a hover.
            var realButton = isHover ? GetPressedButton(state) : button;
            bool physicalButtonPressed = realButton != TerminalMouseEvent.LeftButtonUp;

            if (isButton
                || (isHover && GetInputMode(Mode.ButtonEventMouseTracking) && !sameCoord && physicalButtonPressed)
                || (isHover && GetInputMode(Mode.AnyEventMouseTracking) && !sameCoord))
            {
                if (GetInputMode(Mode.ButtonEventMouseTracking) || GetInputMode(Mode.AnyEventMouseTracking))
                {
                    _mouseInputState = _mouseInputState with { LastPos = new TilPoint(column, row), LastButton = button };
                }

                if (GetInputMode(Mode.Utf8MouseEncoding))
                {
                    return GenerateUtf8Sequence(column, row, realButton, isHover, modifierKeyState, delta);
                }
                else if (GetInputMode(Mode.SgrMouseEncoding))
                {
                    return GenerateSgrSequence(column, row, physicalButtonPressed ? realButton : button, IsButtonUp(button), isHover, modifierKeyState, delta);
                }
                else
                {
                    return GenerateDefaultSequence(column, row, realButton, isHover, modifierKeyState, delta);
                }
            }
        }

        if (ShouldSendAlternateScroll(button, delta))
        {
            return MakeAlternateScrollOutput(button, delta);
        }

        return null;
    }

    private string? GenerateDefaultSequence(int column, int row, TerminalMouseEvent button, bool isHover, ControlKeyStates modifierKeyState, int delta)
    {
        // Positions past what one byte holds cannot be reported.
        if (column <= MAX_DEFAULT_COORDINATE && row <= MAX_DEFAULT_COORDINATE)
        {
            char encodedX = EncodeDefaultCoordinate(column + 1);
            char encodedY = EncodeDefaultCoordinate(row + 1);
            char encodedButton = ButtonToXEncoding(button, isHover, modifierKeyState, delta);
            return $"{_csi}M{encodedButton}{encodedX}{encodedY}";
        }

        return null;
    }

    private string? GenerateUtf8Sequence(int column, int row, TerminalMouseEvent button, bool isHover, ControlKeyStates modifierKeyState, int delta)
    {
        // The UTF-8 encoding (1005) takes coordinates as characters, so they can go as far as a short reaches.
        if (column <= short.MaxValue - 33 && row <= short.MaxValue - 33)
        {
            char encodedX = EncodeDefaultCoordinate(column + 1);
            char encodedY = EncodeDefaultCoordinate(row + 1);
            char encodedButton = ButtonToXEncoding(button, isHover, modifierKeyState, delta);
            return $"{_csi}M{encodedButton}{encodedX}{encodedY}";
        }

        return null;
    }

    private string GenerateSgrSequence(int column, int row, TerminalMouseEvent button, bool isRelease, bool isHover, ControlKeyStates modifierKeyState, int delta)
    {
        int xbutton = ButtonToSgrEncoding(button, isHover, modifierKeyState, delta);
        return $"{_csi}<{xbutton};{column + 1};{row + 1}{(isRelease ? 'm' : 'M')}";
    }

    /// <summary>Whether a wheel turn becomes cursor keys: in the alternate buffer with mode 1007 on.</summary>
    public bool ShouldSendAlternateScroll(TerminalMouseEvent button, int delta)
    {
        bool wasMouseWheel = (button == TerminalMouseEvent.Wheel || button == TerminalMouseEvent.HorizontalWheel) && delta != 0;
        return _inAlternateBuffer && GetInputMode(Mode.AlternateScroll) && wasMouseWheel;
    }

    private string? MakeAlternateScrollOutput(TerminalMouseEvent button, int delta)
    {
        int virtualKey;
        if (button == TerminalMouseEvent.Wheel)
        {
            virtualKey = delta > 0 ? VirtualKey.UP : VirtualKey.DOWN;
        }
        else if (button == TerminalMouseEvent.HorizontalWheel)
        {
            virtualKey = delta > 0 ? VirtualKey.RIGHT : VirtualKey.LEFT;
        }
        else
        {
            return null;
        }

        var key = new SanitizedKeyEvent { VirtualKey = virtualKey };
        var enc = new EncodingHelper();
        EncodeRegular(ref enc, key);
        var seq = new System.Text.StringBuilder();
        FormatEncodingHelper(ref enc, key, seq);
        return seq.ToString();
    }
}
