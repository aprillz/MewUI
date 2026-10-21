using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Terminal.Core;
using Aprillz.MewUI.Terminal.Input;
using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal;

public sealed partial class TerminalView
{
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled || _composition != null)
        {
            return;
        }

        // The terminal's own copy and paste keys are left to the input map.
        foreach (var gesture in _commandGestures)
        {
            if (gesture.Matches(e))
            {
                return;
            }
        }

        var states = TerminalKeyMap.ToControlKeyStates(e.Modifiers);
        int virtualKey = TerminalKeyMap.ToVirtualKey(e.Key);

        if (HandleViewKey(e, virtualKey, states))
        {
            e.Handled = true;
            return;
        }

        if (virtualKey == 0)
        {
            return;
        }

        string? output;
        using (_terminal.LockForWriting())
        {
            output = _terminal.SendKeyEvent(virtualKey, 0, states, keyDown: true, new TerminalKeySymbols(e.KeySymbol, e.BaseKeySymbol), OptionIsMeta);

            // Typing ends a selection, unless the key is part of a shortcut that left it alone.
            if (output != null && _terminal.IsSelectionActive)
            {
                _terminal.ClearSelection();
            }
        }

        // A key that types a character is left unhandled; the character arrives as text input.
        if (output != null)
        {
            e.Handled = true;
            SendInput(output);
            QueueUpdate();
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        int virtualKey = TerminalKeyMap.ToVirtualKey(e.Key);
        if (e.Handled || virtualKey == 0)
        {
            return;
        }

        // The release tells the encoder the next press of the same key is not a repeat (DECARM).
        string? output;
        using (_terminal.LockForWriting())
        {
            output = _terminal.SendKeyEvent(virtualKey, 0, TerminalKeyMap.ToControlKeyStates(e.Modifiers), keyDown: false,
                new TerminalKeySymbols(e.KeySymbol, e.BaseKeySymbol), OptionIsMeta);
        }

        SendInput(output);
    }

    /// <summary>Keys the view handles itself: scrolling, and moving or clearing the selection.</summary>
    private bool HandleViewKey(KeyEventArgs e, int virtualKey, ControlKeyStates states)
    {
        bool ctrlShift = e.ControlKey && e.ShiftKey && !e.AltKey;
        if (ctrlShift)
        {
            switch (e.Key)
            {
                case Key.Up:
                    ScrollLines(-1);
                    return true;
                case Key.Down:
                    ScrollLines(1);
                    return true;
                case Key.PageUp:
                    ScrollLines(-Math.Max(1, _rows - 1));
                    return true;
                case Key.PageDown:
                    ScrollLines(Math.Max(1, _rows - 1));
                    return true;
                case Key.Home:
                    ScrollLines(-int.MaxValue / 2);
                    return true;
                case Key.End:
                    ScrollToBottom();
                    return true;
            }
        }

        using (_terminal.LockForWriting())
        {
            if (!_terminal.IsSelectionActive)
            {
                return false;
            }

            if (e.Key == Key.Escape)
            {
                _terminal.ClearSelection();
                RefreshScreen();
                return true;
            }

            var update = _terminal.ConvertKeyEventToUpdateSelectionParams(states, virtualKey);
            if (update.HasValue)
            {
                _terminal.UpdateSelection(update.Value.Direction, update.Value.Mode, states);
                RefreshScreen();
                QueueUpdate();
                return true;
            }
        }

        return false;
    }

    void ITextInputClient.HandleTextInput(TextInputEventArgs e)
    {
        string text = e.Text;
        if (text.Length == 0)
        {
            return;
        }

        e.Handled = true;

        string? output;
        using (_terminal.LockForWriting())
        {
            _terminal.ClearSelection();
            if (text.Length == 1)
            {
                // Text input reports Enter as a line feed; the key it came from decides what is sent.
                char character = text[0] == '\n' ? '\r' : text[0];
                output = _terminal.SendCharEvent(character, 0, ControlKeyStates.None);
            }
            else
            {
                // Longer text comes from an input method and is sent as typed.
                _terminal.TrySnapOnInput();
                output = text.Replace('\n', '\r');
            }
        }

        SendInput(output);
        RefreshScreen();
        QueueUpdate();
    }

    bool ITextCompositionClient.IsComposing => _composition != null;

    int ITextCompositionClient.CompositionStartIndex => 0;

    void ITextCompositionClient.HandleTextCompositionStart(TextCompositionEventArgs e)
    {
        _composition = string.Empty;
        e.Handled = true;
    }

    void ITextCompositionClient.HandleTextCompositionUpdate(TextCompositionEventArgs e)
    {
        _composition = e.Text;
        ArrangeCursor();
        _surface.CursorVisual.InvalidateVisual();
        e.Handled = true;
    }

    void ITextCompositionClient.HandleTextCompositionEnd(TextCompositionEventArgs e)
    {
        _composition = null;
        ArrangeCursor();
        _surface.CursorVisual.InvalidateVisual();
        e.Handled = true;
    }

    Rect ITextCompositionClient.GetCharRectInWindow(int charIndex)
    {
        int row = Math.Max(0, _cursorCell.Y);
        return _surface.CellRect(_cursorCell.X, row, 1);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        var cell = CellAt(e);
        var button = e.Button switch
        {
            MouseButton.Left => TerminalMouseEvent.LeftButtonDown,
            MouseButton.Middle => TerminalMouseEvent.MiddleButtonDown,
            MouseButton.Right => TerminalMouseEvent.RightButtonDown,
            _ => TerminalMouseEvent.Move,
        };
        if (button == TerminalMouseEvent.Move)
        {
            return;
        }

        if (SendMouse(e, cell, button))
        {
            _trackingButtonDown = true;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.Button == MouseButton.Left)
        {
            OnSelectionMouseDown(e, cell);
        }
        else if (e.Button == MouseButton.Right)
        {
            // As in Windows Terminal: right click copies a selection, or pastes when there is none.
            if (Copy())
            {
                ClearSelection();
            }
            else
            {
                Paste();
            }
        }

        e.Handled = true;
    }

    private void OnSelectionMouseDown(MouseEventArgs e, TilPoint cell)
    {
        string? hyperlink = null;
        using (_terminal.LockForWriting())
        {
            if (e.ControlKey)
            {
                hyperlink = _terminal.GetHyperlinkAtViewportPosition(cell);
            }

            if (string.IsNullOrEmpty(hyperlink))
            {
                if (e.ClickCount >= 3)
                {
                    _terminal.MultiClickSelection(cell, SelectionExpansion.Line);
                }
                else if (e.ClickCount == 2)
                {
                    _terminal.MultiClickSelection(cell, SelectionExpansion.Word);
                }
                else if (e.ShiftKey && _terminal.IsSelectionActive)
                {
                    _terminal.SetSelectionEnd(cell, SelectionExpansion.Char);
                }
                else
                {
                    _terminal.SetSelectionAnchor(cell);
                }

                _terminal.SetBlockSelection(e.AltKey);
            }
        }

        if (!string.IsNullOrEmpty(hyperlink))
        {
            HyperlinkActivated?.Invoke(hyperlink);
            return;
        }

        _selecting = true;
        CaptureMouse();
        RefreshScreen();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var cell = CellAt(e);

        if (_selecting)
        {
            using (_terminal.LockForWriting())
            {
                _terminal.SetSelectionEnd(cell);
            }

            RefreshScreen();
            return;
        }

        SendMouse(e, cell, TerminalMouseEvent.Move);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var cell = CellAt(e);

        if (_selecting && e.Button == MouseButton.Left)
        {
            _selecting = false;
            ReleaseMouseCapture();

            // A click without a drag leaves an empty selection, which is no selection.
            using (_terminal.LockForWriting())
            {
                if (_terminal.IsSelectionActive && _terminal.GetSelectionAnchor() == _terminal.GetSelectionEnd())
                {
                    _terminal.ClearSelection();
                }
            }

            RefreshScreen();
            e.Handled = true;
            return;
        }

        var button = e.Button switch
        {
            MouseButton.Left => TerminalMouseEvent.LeftButtonUp,
            MouseButton.Middle => TerminalMouseEvent.MiddleButtonUp,
            MouseButton.Right => TerminalMouseEvent.RightButtonUp,
            _ => TerminalMouseEvent.Move,
        };
        if (button != TerminalMouseEvent.Move && SendMouse(e, cell, button))
        {
            e.Handled = true;
        }

        if (_trackingButtonDown && !e.LeftButton && !e.MiddleButton && !e.RightButton)
        {
            _trackingButtonDown = false;
            ReleaseMouseCapture();
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        var cell = CellAt(e);

        // The input encoder counts in the wheel units of 120 per notch; X is positive to the left.
        bool vertical = e.Delta.Y != 0;
        int delta = (int)Math.Round((vertical ? e.Delta.Y : -e.Delta.X) * TerminalInput.WHEEL_DELTA);
        var button = vertical ? TerminalMouseEvent.Wheel : TerminalMouseEvent.HorizontalWheel;
        if (delta != 0 && SendMouse(e, cell, button, delta))
        {
            e.Handled = true;
            return;
        }

        // Ctrl+wheel changes the font size a point per notch, as Windows Terminal does.
        if (vertical && e.ControlKey && !e.ShiftKey)
        {
            FontSize = Math.Clamp(FontSize + (e.Delta.Y > 0 ? 1 : -1), MIN_FONT_SIZE, MAX_FONT_SIZE);
            e.Handled = true;
            return;
        }

        if (vertical)
        {
            int lines = (int)Math.Round(-e.Delta.Y * WHEEL_SCROLL_LINES);
            if (lines == 0)
            {
                lines = e.Delta.Y > 0 ? -1 : 1;
            }

            ScrollLines(lines);
            e.Handled = true;
        }
    }

    /// <summary>Sends a mouse event to the application when it tracks the mouse (and Shift is not held); true when sent.</summary>
    private bool SendMouse(MouseEventArgs e, TilPoint cell, TerminalMouseEvent button, int delta = 0)
    {
        if (e.ShiftKey && button != TerminalMouseEvent.Wheel && button != TerminalMouseEvent.HorizontalWheel)
        {
            return false;
        }

        string? output;
        using (_terminal.LockForWriting())
        {
            bool tracking = _terminal.IsTrackingMouseInput;
            if (!tracking && !_terminal.ShouldSendAlternateScroll(button, delta))
            {
                return false;
            }

            var states = TerminalKeyMap.ToControlKeyStates(e.Modifiers);
            var buttons = new TerminalInput.MouseButtonState(e.LeftButton, e.MiddleButton, e.RightButton);
            output = _terminal.SendMouseEvent(cell, button, states, delta, buttons);
        }

        if (output == null)
        {
            return false;
        }

        SendInput(output);
        return true;
    }

    /// <summary>The viewport cell under the mouse, possibly outside the grid while dragging.</summary>
    private TilPoint CellAt(MouseEventArgs e)
    {
        var position = e.GetPosition(_surface);
        double offsetX = position.X - (_surface.TextArea.X - _surface.Bounds.X);
        double offsetY = position.Y - (_surface.TextArea.Y - _surface.Bounds.Y);
        return new TilPoint((int)Math.Floor(offsetX / CellWidth), (int)Math.Floor(offsetY / CellHeight));
    }

    private void CaptureMouse()
    {
        if (FindVisualRoot() is Window window)
        {
            window.CaptureMouse(this);
        }
    }

    private void ReleaseMouseCapture()
    {
        if (FindVisualRoot() is Window window)
        {
            window.ReleaseMouseCapture();
        }
    }
}
