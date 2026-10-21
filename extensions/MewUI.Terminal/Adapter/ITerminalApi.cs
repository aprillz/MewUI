// Ported from Windows Terminal src/terminal/adapter/ITerminalApi.hpp (MIT).

using Aprillz.MewUI.Terminal.Buffer;
using Aprillz.MewUI.Terminal.Parser;
using Aprillz.MewUI.Terminal.Types;

namespace Aprillz.MewUI.Terminal.Adapter;

using CursorType = Aprillz.MewUI.Terminal.Buffer.CursorType;

/// <summary>The buffer the dispatcher writes to, the part of it shown, and whether it is the main buffer.</summary>
internal readonly record struct BufferState(TextBuffer Buffer, TilRect Viewport, bool IsMainBuffer);

/// <summary>What the dispatcher needs from the terminal that hosts it.</summary>
internal interface ITerminalApi
{
    enum Mode
    {
        AutoWrap,
        LineFeed,
        BracketedPaste,
    }

    void ReturnResponse(ReadOnlySpan<char> response);

    bool IsConPTY { get; }

    StateMachine GetStateMachine();

    BufferState GetBufferAndViewport();

    void SetViewportPosition(TilPoint position);

    bool IsVtInputEnabled { get; }

    void SetSystemMode(Mode mode, bool enabled);

    bool GetSystemMode(Mode mode);

    void ReturnAnswerback();

    void WarningBell();

    void SetWindowTitle(ReadOnlySpan<char> title);

    void UseAlternateScreenBuffer(TextAttribute attributes);

    void UseMainScreenBuffer();

    CursorType GetUserDefaultCursorStyle();

    void ShowWindow(bool showOrHide);

    void CopyToClipboard(string content);

    void SetTaskbarProgress(TaskbarState state, int progress);

    void SetWorkingDirectory(ReadOnlySpan<char> uri);

    void PlayMidiNote(int noteNumber, int velocity, TimeSpan duration);

    bool ResizeWindow(int width, int height);

    void NotifyBufferRotation(int delta);

    void NotifyShellIntegrationMark();

    void InvokeCompletions(ReadOnlySpan<char> menuJson, int replaceLength);

    void SearchMissingCommand(ReadOnlySpan<char> command);

    void ShowNotification(ReadOnlySpan<char> title, ReadOnlySpan<char> body);
}
