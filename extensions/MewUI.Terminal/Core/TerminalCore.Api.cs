// Ported from Windows Terminal src/cascadia/TerminalCore/TerminalApi.cpp (MIT).

using Aprillz.MewUI.Terminal.Adapter;
using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Parser;
using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Core;

using CursorType = Aprillz.MewUI.Terminal.Buffer.CursorType;

internal sealed partial class TerminalCore
{
    public void ReturnResponse(ReadOnlySpan<char> response)
    {
        if (WriteInput != null && !response.IsEmpty)
        {
            WriteInput(response.ToString());
        }
    }

    public bool IsConPTY => false;

    public StateMachine GetStateMachine() => StateMachine;

    public BufferState GetBufferAndViewport() => new(ActiveBuffer, GetMutableViewport().ToExclusive(), !InAltBuffer);

    public void SetViewportPosition(TilPoint position)
    {
        // The alternate buffer has no scrollback, so its viewport never moves.
        if (!InAltBuffer)
        {
            var bufferSize = MainBuffer.Size.Dimensions;
            var viewSize = GetMutableViewport().Dimensions;

            position = new TilPoint(
                Math.Clamp(position.X, 0, bufferSize.Width - viewSize.Width),
                Math.Clamp(position.Y, 0, bufferSize.Height - viewSize.Height));

            int viewportDelta = position.Y - GetMutableViewport().Origin.Y;
            _mutableViewport = Viewport.FromDimensions(position, viewSize);

            PreserveUserScrollOffset(viewportDelta);
            NotifyScrollEvent();
        }
    }

    public bool IsVtInputEnabled => false;

    public void SetSystemMode(ITerminalApi.Mode mode, bool enabled)
    {
        AssertLocked();
        var flag = SystemModeFlag(mode);
        if (enabled)
        {
            _systemMode |= flag;
        }
        else
        {
            _systemMode &= ~flag;
        }
    }

    public bool GetSystemMode(ITerminalApi.Mode mode)
    {
        AssertLocked();
        return (_systemMode & SystemModeFlag(mode)) != 0;
    }

    private static int SystemModeFlag(ITerminalApi.Mode mode) => 1 << (int)mode;

    public void ReturnAnswerback() => ReturnResponse(_answerbackMessage);

    public void WarningBell() => WarningBellRequested?.Invoke();

    public void SetWindowTitle(ReadOnlySpan<char> title)
    {
        AssertLocked();

        if (!_suppressApplicationTitle)
        {
            // An empty title goes back to the starting title.
            _title = title.IsEmpty ? null : title.ToString();
            TitleChanged?.Invoke(GetConsoleTitle());
        }
    }

    public CursorType GetUserDefaultCursorStyle()
    {
        AssertLocked();
        return _defaultCursorShape;
    }

    public bool ResizeWindow(int width, int height)
    {
        AssertLocked();

        if (width <= 0 || height <= 0 || width > short.MaxValue || height > short.MaxValue)
        {
            return false;
        }

        var currentDimensions = GetMutableViewport().Dimensions;
        if (width == currentDimensions.Width && height == currentDimensions.Height)
        {
            return false;
        }

        // The host decides whether the window can change size; the terminal follows when it does.
        var handler = WindowSizeChangeRequested;
        if (handler != null)
        {
            handler(width, height);
            return true;
        }

        return false;
    }

    public void CopyToClipboard(string content)
    {
        // Only a focused terminal may write the clipboard, so a background tab cannot overwrite it.
        if (_clipboardOperationsAllowed && _focused)
        {
            CopyToClipboardRequested?.Invoke(content);
        }
    }

    public void SetTaskbarProgress(Adapter.TaskbarState state, int progress)
    {
        AssertLocked();
        _taskbarState = (int)state;

        switch (state)
        {
            case Adapter.TaskbarState.Clear:
                _taskbarProgress = 0;
                break;
            case Adapter.TaskbarState.Set:
                _taskbarProgress = progress;
                break;
            case Adapter.TaskbarState.Indeterminate:
                // The progress is kept for when the state changes back.
                break;
            case Adapter.TaskbarState.Error:
            case Adapter.TaskbarState.Paused:
                // Without a progress value, the bar still shows a minimum amount so the state is visible.
                if (progress == 0)
                {
                    if (_taskbarProgress == 0)
                    {
                        _taskbarProgress = TASKBAR_MIN_PROGRESS;
                    }
                }
                else
                {
                    _taskbarProgress = progress;
                }

                break;
        }

        TaskbarProgressChanged?.Invoke();
    }

    public void SetWorkingDirectory(ReadOnlySpan<char> uri)
    {
        AssertLocked();
        _workingDirectory = uri.ToString();
    }

    public void PlayMidiNote(int noteNumber, int velocity, TimeSpan duration) => PlayMidiNoteRequested?.Invoke(noteNumber, velocity, duration);

    public void UseAlternateScreenBuffer(TextAttribute attributes)
    {
        AssertLocked();

        var mainBuffer = MainBuffer;
        _altBufferSize = _mutableViewport.Dimensions;

        int cursorSize = mainBuffer.Cursor.Size;

        ClearSelection();

        _altBuffer = new TextBuffer(_altBufferSize, attributes, cursorSize, true, mainBuffer.Renderer);
        mainBuffer.SetAsActiveBuffer(false);

        // The cursor carries over, with its position relative to the viewport.
        var myCursor = mainBuffer.Cursor;
        var targetCursor = _altBuffer.Cursor;
        targetCursor.SetStyle(myCursor.Size, myCursor.Type);
        targetCursor.SetIsVisible(myCursor.IsVisible);
        targetCursor.SetIsBlinking(myCursor.IsBlinking);
        var targetCursorPos = myCursor.Position;
        targetCursor.SetPosition(targetCursorPos with { Y = targetCursorPos.Y - _mutableViewport.Top });

        UpdateUrlDetection();

        _terminalInput.UseAlternateScreenBuffer();

        // The scroll bar shows no scrollback in the alternate buffer.
        NotifyScrollEvent();

        ActiveBuffer.TriggerRedrawAll();
    }

    public void UseMainScreenBuffer()
    {
        var altBuffer = _altBuffer;
        _altBuffer = null;
        if (altBuffer == null)
        {
            return;
        }

        ClearSelection();

        var mainBuffer = MainBuffer;
        mainBuffer.SetAsActiveBuffer(true);

        // A resize that happened in the alternate buffer is applied to the main buffer now.
        if (_deferredResize.HasValue)
        {
            UserResize(_deferredResize.Value);
            _deferredResize = null;
            mainBuffer = MainBuffer;
        }

        var altCursor = altBuffer.Cursor;
        var mainCursor = mainBuffer.Cursor;
        mainCursor.SetStyle(altCursor.Size, altCursor.Type);
        mainCursor.SetIsVisible(altCursor.IsVisible);
        mainCursor.SetIsBlinking(altCursor.IsBlinking);
        var targetCursorPos = altCursor.Position;
        mainCursor.SetPosition(targetCursorPos with { Y = targetCursorPos.Y + _mutableViewport.Top });

        UpdateUrlDetection();

        _terminalInput.UseMainScreenBuffer();

        NotifyScrollEvent();

        ActiveBuffer.TriggerRedrawAll();
    }

    public void ShowWindow(bool showOrHide) => ShowWindowRequested?.Invoke(showOrHide);

    public void InvokeCompletions(ReadOnlySpan<char> menuJson, int replaceLength) => CompletionsChanged?.Invoke(menuJson.ToString(), replaceLength);

    public void SearchMissingCommand(ReadOnlySpan<char> command)
    {
        var handler = SearchMissingCommandRequested;
        if (handler != null)
        {
            int bufferRow = ActiveBuffer.Cursor.Position.Y;
            handler(command.ToString(), bufferRow);
        }
    }

    public void ShowNotification(ReadOnlySpan<char> title, ReadOnlySpan<char> body) => ShowNotificationRequested?.Invoke(title.ToString(), body.ToString());

    public void NotifyBufferRotation(int delta)
    {
        // The selection moves up with the text, and goes away if its end scrolled out of the buffer.
        if (_selection.Active)
        {
            ref var selection = ref WriteSelection();
            if (selection.End.Y < delta)
            {
                selection.Active = false;
            }
            else
            {
                bool pivotWasStart = selection.Start == selection.Pivot;
                selection.Start = selection.Start with { Y = Math.Max(selection.Start.Y - delta, 0) };
                selection.End = selection.End with { Y = Math.Max(selection.End.Y - delta, 0) };
                selection.Pivot = pivotWasStart ? selection.Start : selection.End;
            }
        }

        // The pattern ranges are for rows that moved; they are recomputed on the next update.
        _patterns = new List<(TilPoint, TilPoint, int)>();

        int oldScrollOffset = _scrollOffset;
        PreserveUserScrollOffset(delta);
        if (_scrollOffset != oldScrollOffset || AlwaysNotifyOnBufferRotation)
        {
            NotifyScrollEvent();
        }
    }

    public void NotifyShellIntegrationMark() => NotifyScrollEvent();
}
