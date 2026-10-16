using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Platform.Browser;

internal sealed class BrowserWindowBackend : IWindowBackend
{
    private readonly BrowserPlatformHost _host;
    private bool _disposed;
    private bool _shown;
    private bool _mouseCaptured;

    // Stand-in line height for a text control that has not been laid out yet.
    private const double DEFAULT_CARET_HEIGHT_DIP = 16;

    // Cap on either side of the caret for the text mirrored into the page's IME field. A paragraph
    // is the natural unit for reconversion; the cap keeps a single long one off the DOM.
    private const int MIRROR_MAX_CHARS = 1024;

    // Windows counts a double click inside 500ms and a few pixels.
    private const double MULTI_CLICK_WINDOW_MS = 500;
    private const double MULTI_CLICK_SLOP_DIP = 4;

    private double _lastPressMs = double.NegativeInfinity;
    private Point _lastPressPoint;
    private MouseButton _lastPressButton;
    private int _clickCount;

    private readonly BrowserWindowSurface _surface = new();

    internal BrowserWindowBackend(BrowserPlatformHost host, Window window)
    {
        _host = host;
        Window = window;
    }

    internal Window Window { get; }

    /// <summary>Set by invalidation, cleared once the frame is drawn.</summary>
    internal bool NeedsRender { get; private set; } = true;
    internal uint Dpi => (uint)Math.Max(96, Math.Round(_surface.DpiScale * 96));
    public nint Handle => _disposed ? 0 : 1;

    public void CreateSurface()
    {
        ThrowIfDisposed();
        Window.SetDpi(Dpi);
    }

    public void PresentSurface()
    {
        ThrowIfDisposed();
        _shown = true;
        NeedsRender = true;
        RenderNow();
    }

    public void Hide() => _shown = false;
    public void Close() => Application.Shutdown();
    public void SetResizable(bool resizable) { }
    public void Invalidate(bool erase)
    {
        // Coalesce invalidations; the host loop decides when the next frame runs.
        NeedsRender = true;
        _host.RequestFrame();
    }
    public void SetTitle(string title) { }
    public void SetIcon(IconSource? icon) { }
    public void SetClientSize(double widthDip, double heightDip) { }
    public Point GetPosition() => default;
    public void SetPosition(double leftDip, double topDip) { }
    public void SetPositionPx(int leftPx, int topPx) { }
    public void CaptureMouse() => _mouseCaptured = true;
    public void ReleaseMouseCapture() => _mouseCaptured = false;
    public Point ClientToScreen(Point clientPointDip) => clientPointDip;
    public Point ScreenToClient(Point screenPointPx) => screenPointPx;
    public void EnsureTheme(bool isDark) { }
    public void CenterOnOwner() { }
    public void Activate() => Window.SetIsActive(true);
    public void SetOwner(nint ownerHandle) { }
    public void SetEnabled(bool enabled) { }
    public void SetOpacity(double opacity) { }
    public void SetAllowsTransparency(bool allowsTransparency) { }
    public void SetCursor(CursorType cursorType) { }
    public void SetImeMode(Input.ImeMode mode) { }
    // The page cancels its own composition on the hidden field; the host clears what the control
    // is showing so a discarded pre-edit does not stay on screen.
    public void CancelImeComposition() => CompositionEnd(string.Empty);

    internal void CompositionStart()
    {
        if (!_shown || _disposed) return;
        var args = new TextCompositionEventArgs();
        Window.RaisePreviewTextCompositionStart(args);
        if (!args.Handled && Window.FocusManager.FocusedElement is ITextCompositionClient client)
        {
            client.HandleTextCompositionStart(args);
        }
    }

    internal void CompositionUpdate(string text)
    {
        if (!_shown || _disposed) return;
        var args = new TextCompositionEventArgs(text);
        Window.RaisePreviewTextCompositionUpdate(args);
        if (!args.Handled && Window.FocusManager.FocusedElement is ITextCompositionClient client)
        {
            client.HandleTextCompositionUpdate(args);
        }
    }

    internal void CompositionEnd(string text)
    {
        if (!_shown || _disposed) return;
        var args = new TextCompositionEventArgs(text);
        Window.RaisePreviewTextCompositionEnd(args);
        if (!args.Handled && Window.FocusManager.FocusedElement is ITextCompositionClient client)
        {
            client.HandleTextCompositionEnd(args);
        }
    }

    internal bool PointerMove(double x, double y, double screenX, double screenY, int buttons, ModifierKeys modifiers, PointerType pointerType)
    {
        if (!_shown || _disposed) return false;
        WindowInputRouter.MouseMove(
            Window,
            new Point(x, y),
            new Point(screenX, screenY),
            leftDown: (buttons & 1) != 0,
            rightDown: (buttons & 2) != 0,
            middleDown: (buttons & 4) != 0,
            modifiers,
            pointerType);
        return _mouseCaptured;
    }

    internal bool PointerButton(double x, double y, double screenX, double screenY, int button, int buttons,
        bool isDown, double timeStampMs, ModifierKeys modifiers, PointerType pointerType)
    {
        if (!_shown || _disposed) return false;

        var mappedButton = button switch
        {
            1 => MouseButton.Middle,
            2 => MouseButton.Right,
            3 => MouseButton.XButton1,
            4 => MouseButton.XButton2,
            _ => MouseButton.Left,
        };

        // The DOM does not count clicks (the Pointer Events spec pins detail to 0), so presses close
        // enough in time and place chain up here, the way a platform message loop would report them.
        if (isDown)
        {
            bool chained = mappedButton == _lastPressButton
                && timeStampMs - _lastPressMs <= MULTI_CLICK_WINDOW_MS
                && Math.Abs(x - _lastPressPoint.X) <= MULTI_CLICK_SLOP_DIP
                && Math.Abs(y - _lastPressPoint.Y) <= MULTI_CLICK_SLOP_DIP;
            _clickCount = chained ? _clickCount + 1 : 1;
            _lastPressMs = timeStampMs;
            _lastPressPoint = new Point(x, y);
            _lastPressButton = mappedButton;
        }
        WindowInputRouter.MouseButton(
            Window,
            new Point(x, y),
            new Point(screenX, screenY),
            mappedButton,
            isDown,
            leftDown: (buttons & 1) != 0,
            rightDown: (buttons & 2) != 0,
            middleDown: (buttons & 4) != 0,
            Math.Max(1, _clickCount),
            modifiers,
            pointerType);
        return _mouseCaptured;
    }

    internal void PointerWheel(double x, double y, double screenX, double screenY,
        double deltaX, double deltaY, int buttons, ModifierKeys modifiers)
    {
        if (!_shown || _disposed) return;
        WindowInputRouter.MouseWheel(
            Window,
            new Point(x, y),
            new Point(screenX, screenY),
            new Vector(deltaX, deltaY),
            leftDown: (buttons & 1) != 0,
            rightDown: (buttons & 2) != 0,
            middleDown: (buttons & 4) != 0,
            modifiers);
    }

    internal bool WantsTextInput()
        => _shown && !_disposed && Window.FocusManager.FocusedElement is Input.ITextInputClient;

    /// <summary>Reports the caret in surface coordinates (DIPs); false when nothing holds one.</summary>
    internal bool TryGetCaretRect(out double x, out double y, out double height)
    {
        x = 0;
        y = 0;
        height = 0;
        if (!_shown || _disposed || Window.FocusManager.FocusedElement is not ITextCompositionClient client)
        {
            return false;
        }

        int caretIndex = client is ITextCompositionEditor editor ? editor.CaretPosition : client.CompositionStartIndex;
        var rect = client.GetCharRectInWindow(caretIndex);
        x = rect.X;
        y = rect.Y;

        // Layout may not have run for the control yet, and a target with no height would let the
        // candidate list land on top of the line it belongs to instead of below it.
        height = rect.Height > 0 ? rect.Height : DEFAULT_CARET_HEIGHT_DIP;
        return true;
    }

    /// <summary>
    /// Routes a touch or pen contact going down. <paramref name="eventAgeMs"/> is how long ago the page
    /// received the event, which places it on the core's clock without assuming the two share an origin.
    /// </summary>
    internal void PointerDown(int pointerId, PointerType pointerType, double x, double y, double screenX, double screenY,
        double eventAgeMs, ModifierKeys modifiers)
    {
        if (!_shown || _disposed) return;
        WindowInputRouter.PointerDown(
            Window, pointerId, pointerType, new Point(x, y), new Point(screenX, screenY), TimestampOf(eventAgeMs), modifiers);
    }

    /// <summary>Routes a touch or pen contact moving, or a pen hovering in range.</summary>
    internal void PointerMove(int pointerId, PointerType pointerType, double x, double y, double screenX, double screenY,
        double eventAgeMs, ModifierKeys modifiers)
    {
        if (!_shown || _disposed) return;
        WindowInputRouter.PointerMove(
            Window, pointerId, pointerType, new Point(x, y), new Point(screenX, screenY), TimestampOf(eventAgeMs), modifiers);
    }

    /// <summary>Routes a touch or pen contact lifting.</summary>
    internal void PointerUp(int pointerId, PointerType pointerType, double x, double y, double screenX, double screenY,
        double eventAgeMs, ModifierKeys modifiers)
    {
        if (!_shown || _disposed) return;
        WindowInputRouter.PointerUp(
            Window, pointerId, pointerType, new Point(x, y), new Point(screenX, screenY), TimestampOf(eventAgeMs), modifiers);
    }

    /// <summary>Routes a touch or pen contact that ended without lifting.</summary>
    internal void PointerCancel(int pointerId)
    {
        if (_disposed) return;
        WindowInputRouter.PointerCancel(Window, pointerId);
    }

    /// <summary>True while a released touch scroll is still moving the content.</summary>
    internal bool IsScrollCoasting => Window.IsScrollCoasting;

    internal static long TimestampOf(double ageMs)
        => System.Diagnostics.Stopwatch.GetTimestamp()
            - (long)(Math.Max(0, ageMs) * System.Diagnostics.Stopwatch.Frequency / 1000.0);

    internal void PointerLeave()
    {
        if (!_mouseCaptured && !_disposed)
        {
            WindowInputRouter.UpdateMouseOver(Window, null);
        }
    }

    internal void PointerCancel()
    {
        if (_disposed) return;
        // A cancelled press is not part of a click chain.
        _lastPressMs = double.NegativeInfinity;
        _mouseCaptured = false;
        WindowInputRouter.PointerCancel(Window);
    }

    internal bool KeyDown(string code, int platformKey, ModifierKeys modifiers, bool isRepeat, string? key)
    {
        if (!_shown || _disposed) return false;
        var args = new KeyEventArgs(BrowserKeyMap.Map(code, key), platformKey, modifiers, isRepeat);
        WindowInputRouter.KeyDown(Window, args);
        return args.Handled;
    }

    internal bool KeyUp(string code, int platformKey, ModifierKeys modifiers, string? key)
    {
        if (!_shown || _disposed) return false;
        var args = new KeyEventArgs(BrowserKeyMap.Map(code, key), platformKey, modifiers);
        WindowInputRouter.KeyUp(Window, args);
        return args.Handled;
    }

    internal bool TextInput(string text)
    {
        if (!_shown || _disposed || string.IsNullOrEmpty(text)) return false;

        var args = new TextInputEventArgs(text);
        WindowInputRouter.TextInput(Window, args);

        return args.Handled;
    }

    /// <summary>
    /// Returns the text around the caret for the page to mirror into its IME field, as
    /// "start:end:text" over that text; empty when nothing editable holds focus.
    /// </summary>
    // The mirror is what an IME can revise: a browser IME reads and edits the DOM field, not the
    // app, so text it may reconvert has to be sitting there with the caret in the same place.
    internal string GetTextInputState()
    {
        if (!_shown || _disposed || Window.FocusManager.FocusedElement is not ITextCompositionEditor editor)
        {
            return string.Empty;
        }

        var (selectionStart, selectionEnd) = editor.SelectionRange;
        int start = Math.Clamp(Math.Min(selectionStart, selectionEnd), 0, editor.TextLength);
        int end = Math.Clamp(Math.Max(selectionStart, selectionEnd), start, editor.TextLength);

        // Only the caret's own paragraph is mirrored, bounded on both sides: a whole document would
        // be copied into the DOM on every caret move, and no IME revises across a line break.
        int mirrorStart = editor.TextLength == 0 ? 0 : FindParagraphStart(editor, start);
        int mirrorEnd = editor.TextLength == 0 ? 0 : FindParagraphEnd(editor, end);
        string mirror = mirrorEnd > mirrorStart ? editor.GetTextSubstring(mirrorStart, mirrorEnd - mirrorStart) : string.Empty;
        return $"{start - mirrorStart}:{end - mirrorStart}:{mirror}";
    }

    private static int FindParagraphStart(ITextCompositionEditor editor, int caret)
    {
        int limit = Math.Max(0, caret - MIRROR_MAX_CHARS);
        for (int index = caret; index > limit; index--)
        {
            if (editor.GetTextSubstring(index - 1, 1)[0] == '\n')
            {
                return index;
            }
        }

        return limit;
    }

    private static int FindParagraphEnd(ITextCompositionEditor editor, int caret)
    {
        int limit = Math.Min(editor.TextLength, caret + MIRROR_MAX_CHARS);
        for (int index = caret; index < limit; index++)
        {
            if (editor.GetTextSubstring(index, 1)[0] == '\n')
            {
                return index;
            }
        }

        return limit;
    }

    /// <summary>
    /// Replaces <paramref name="replacePrevious"/> characters before and <paramref name="replaceNext"/>
    /// after the caret with <paramref name="text"/>, the way the page's IME field was just edited.
    /// </summary>
    internal void ReplaceText(int replacePrevious, int replaceNext, string text)
    {
        if (!_shown || _disposed)
        {
            return;
        }

        bool replaces = replacePrevious > 0 || replaceNext > 0;
        if (Window.FocusManager.FocusedElement is ITextCompositionEditor editor)
        {
            if (replaces)
            {
                var (selectionStart, selectionEnd) = editor.SelectionRange;
                int start = Math.Clamp(Math.Min(selectionStart, selectionEnd) - replacePrevious, 0, editor.TextLength);
                int end = Math.Clamp(Math.Max(selectionStart, selectionEnd) + replaceNext, start, editor.TextLength);
                editor.SetSelectionRangeForPlatform(start, end);
            }
        }
        else if (replaces)
        {
            // Nothing to align the replacement against, and inserting alone would duplicate text.
            return;
        }

        if (text.Length != 0)
        {
            TextInput(text);
        }
        else if (replaces)
        {
            // A replacement by nothing is a deletion of what was just selected, which is what the
            // control does for a backspace over a selection.
            KeyDown("Backspace", 0, ModifierKeys.None, isRepeat: false, key: null);
            KeyUp("Backspace", 0, ModifierKeys.None, key: null);
        }
    }

    internal void SetFocus(bool focused)
    {
        if (_disposed) return;
        Window.SetIsActive(focused);
        if (!focused)
        {
            _mouseCaptured = false;
            Window.ClearMouseOverState();
        }
    }

    internal void RenderFrame(double cssWidth, double cssHeight, double devicePixelRatio, int pixelWidth, int pixelHeight)
    {
        if (!_shown || _disposed)
        {
            return;
        }

        double scale = devicePixelRatio > 0 ? devicePixelRatio : 1;
        uint dpi = (uint)Math.Max(96, Math.Round(scale * 96));
        bool resized = _surface.Update(pixelWidth, pixelHeight, scale);
        if (Window.Dpi != dpi)
        {
            Window.SetDpi(dpi);
            resized = true;
        }

        var widthDip = cssWidth > 0 ? cssWidth : pixelWidth / scale;
        var heightDip = cssHeight > 0 ? cssHeight : pixelHeight / scale;
        var oldSize = Window.ClientSize;
        if (Math.Abs(oldSize.Width - widthDip) > 0.01 || Math.Abs(oldSize.Height - heightDip) > 0.01)
        {
            Window.SetClientSizeDip(widthDip, heightDip);
            Window.InvalidateSizingTransaction();
            resized = true;
            Console.WriteLine($"[resize] surface={pixelWidth}x{pixelHeight} dip={widthDip:F1}x{heightDip:F1} was={oldSize.Width:F1}x{oldSize.Height:F1} dpi={dpi}");
        }

        if (resized)
        {
            NeedsRender = true;
        }

        RenderNow();
    }

    private void RenderNow()
    {
        if (!_shown || _disposed)
        {
            return;
        }

        NeedsRender = false;
        if (!BrowserFrameDiagnostics.Enabled)
        {
            Window.PerformLayout();
            Window.RenderFrame(_surface);
            return;
        }

        long phase = BrowserFrameDiagnostics.Now();
        Window.PerformLayout();
        BrowserFrameDiagnostics.NoteLayout(BrowserFrameDiagnostics.Since(phase));

        var statistics = Window.RetainedStatistics;
        int visited = statistics?.CapturedNodeCount ?? 0;
        int recorded = statistics?.ContentRecordCount ?? 0;
        int replayed = statistics?.ContentReplayCount ?? 0;
        int liveFallbacks = statistics?.LiveFallbackCount ?? 0;
        phase = BrowserFrameDiagnostics.Now();
        Window.RenderFrame(_surface);
        BrowserFrameDiagnostics.NoteRender(BrowserFrameDiagnostics.Since(phase));

        statistics = Window.RetainedStatistics;
        BrowserFrameDiagnostics.NoteScene(
            (statistics?.CapturedNodeCount ?? 0) - visited,
            (statistics?.ContentRecordCount ?? 0) - recorded,
            (statistics?.ContentReplayCount ?? 0) - replayed,
            (statistics?.LiveFallbackCount ?? 0) - liveFallbacks,
            Window.LastRetainedDirtyRect,
            _surface.DpiScale);
    }

    public void Dispose()
    {
        _disposed = true;
        _shown = false;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class BrowserWindowSurface : IWindowSurface
    {
        public nint Handle => 1;
        public int PixelWidth { get; private set; } = 1356;
        public int PixelHeight { get; private set; } = 720;
        public double DpiScale { get; private set; } = 1;

        internal bool Update(int pixelWidth, int pixelHeight, double dpiScale)
        {
            pixelWidth = Math.Max(1, pixelWidth);
            pixelHeight = Math.Max(1, pixelHeight);
            dpiScale = dpiScale > 0 ? dpiScale : 1;
            bool changed = PixelWidth != pixelWidth || PixelHeight != pixelHeight || Math.Abs(DpiScale - dpiScale) > 0.001;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            DpiScale = dpiScale;
            return changed;
        }
    }
}
