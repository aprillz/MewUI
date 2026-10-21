using Aprillz.MewUI.Terminal.Input;

namespace Aprillz.MewUI.Terminal;

/// <summary>Converts MewUI keys and modifiers to the key codes and modifier state <see cref="TerminalInput"/> encodes.</summary>
internal static class TerminalKeyMap
{
    /// <summary>The virtual key of <paramref name="key"/>, or 0 for a key the terminal does not encode.</summary>
    public static int ToVirtualKey(Key key)
    {
        if (key >= Key.A && key <= Key.Z)
        {
            return VirtualKey.KEY_A + (key - Key.A);
        }

        if (key >= Key.D0 && key <= Key.D9)
        {
            return VirtualKey.KEY_0 + (key - Key.D0);
        }

        if (key >= Key.F1 && key <= Key.F24)
        {
            return VirtualKey.F1 + (key - Key.F1);
        }

        if (key >= Key.NumPad0 && key <= Key.NumPad9)
        {
            return VirtualKey.NUMPAD0 + (key - Key.NumPad0);
        }

        return key switch
        {
            Key.Backspace => VirtualKey.BACK,
            Key.Tab => VirtualKey.TAB,
            Key.Enter => VirtualKey.RETURN,
            Key.Escape => VirtualKey.ESCAPE,
            Key.Space => VirtualKey.SPACE,
            Key.Left => VirtualKey.LEFT,
            Key.Up => VirtualKey.UP,
            Key.Right => VirtualKey.RIGHT,
            Key.Down => VirtualKey.DOWN,
            Key.Insert => VirtualKey.INSERT,
            Key.Delete => VirtualKey.DELETE,
            Key.Home => VirtualKey.HOME,
            Key.End => VirtualKey.END,
            Key.PageUp => VirtualKey.PRIOR,
            Key.PageDown => VirtualKey.NEXT,
            Key.Add => VirtualKey.ADD,
            Key.Subtract => VirtualKey.SUBTRACT,
            Key.Multiply => VirtualKey.MULTIPLY,
            Key.Divide => VirtualKey.DIVIDE,
            Key.Decimal => VirtualKey.DECIMAL,
            Key.Backquote => VirtualKey.OEM_3,
            Key.Minus => VirtualKey.OEM_MINUS,
            Key.Equal => VirtualKey.OEM_PLUS,
            Key.BracketLeft => VirtualKey.OEM_4,
            Key.BracketRight => VirtualKey.OEM_6,
            Key.Backslash => VirtualKey.OEM_5,
            Key.Semicolon => VirtualKey.OEM_1,
            Key.Quote => VirtualKey.OEM_7,
            Key.Comma => VirtualKey.OEM_COMMA,
            Key.Period => VirtualKey.OEM_PERIOD,
            Key.Slash => VirtualKey.OEM_2,
            Key.IntlBackslash => VirtualKey.OEM_102,
            Key.ContextMenu => VirtualKey.APPS,
            _ => 0,
        };
    }

    /// <summary>The modifier state of <paramref name="modifiers"/>; the left keys stand for either side.</summary>
    public static ControlKeyStates ToControlKeyStates(ModifierKeys modifiers)
    {
        var states = ControlKeyStates.None;
        if ((modifiers & ModifierKeys.Shift) != 0)
        {
            states |= ControlKeyStates.ShiftPressed;
        }

        if ((modifiers & ModifierKeys.Control) != 0)
        {
            states |= ControlKeyStates.LeftCtrlPressed;
        }

        if ((modifiers & ModifierKeys.Alt) != 0)
        {
            states |= ControlKeyStates.LeftAltPressed;
        }

        return states;
    }
}
