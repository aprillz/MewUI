using System.Runtime.InteropServices;

using Aprillz.MewUI.Diagnostics;
using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Platform.MacOS;

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NSRange
{
    public readonly ulong location;
    public readonly ulong length;

    public NSRange(ulong location, ulong length)
    {
        this.location = location;
        this.length = length;
    }
}

internal sealed class MacOSWindowBackend : IWindowBackend
{
    // NOTE: Cocoa defines NSNotFound as NSIntegerMax (not NSUIntegerMax).
    private const ulong NSNotFound = (ulong)long.MaxValue;

    internal static readonly EnvDebugLog.Logger ImeLogger = new("MEWUI_IME_DEBUG", "[MacOS][IME]");
    internal static readonly EnvDebugLog.Logger ImeNativeLogger = new("MEWUI_IME_DEBUG_NATIVE", "[MacOS][IME]");

    private static string Truncate(string? text, int maxLen = 120)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "…";
    }

    internal static string TruncateForImeLog(string? text, int maxLen = 120) => Truncate(text, maxLen);

    private readonly MacOSPlatformHost _host;
    private readonly Window _window;
    private nint _nsWindow;
    private nint _nsView;
    private nint _nsContext;
    private nint _metalLayer;
    private bool _shown;
    private int _needsRender;
    private double _lastDpiScale = 1.0;
    private double _opacity = 1.0;
    private bool _allowsTransparency;
    private bool _leftDown;
    private bool _rightDown;
    private bool _middleDown;

    private enum ImeState
    {
        Disabled = 0,
        Ground = 1,
        Preedit = 2,
        Committed = 3,
    }

    // winit-style IME state tracking:
    // - keyDown first goes through interpretKeyEvents (NSTextInputClient).
    // - if that produced IME activity (preedit/commit), we suppress the app KeyDown for that key.
    // - if AppKit calls doCommandBySelector:, it means the key wasn't handled as "text" and should
    //   be forwarded to the app.
    private ImeState _imeState = ImeState.Ground;
    private bool _forwardKeyToAppThisKeyDown;
    private readonly HashSet<int> _forceKeyUps = new();
    private bool _enabled = true;
    private bool _closedRaised;
    private double _wheelRemainderX;
    private double _wheelRemainderY;
    private readonly int[] _lastPressClickCounts = new int[5];
    private int _reshapeRendering;
    private long _defaultWindowLevel;
    private ulong _defaultStyleMask;
    private bool? _lastMetalDisplaySyncEnabled;

    private bool _imeHasMarkedText;
    private string _imeMarkedText = string.Empty;
    internal string ImeMarkedText => _imeMarkedText;
    internal Window Window => _window;

    private bool _isHandlingKeyDown;
    private string? _pendingKeyDownTextInput;

    private void UpdateMetalLayerDisplaySyncIfNeeded()
    {
        if (_metalLayer == 0 || !Application.IsRunning)
        {
            return;
        }

        bool enabled = Application.Current.RenderLoopSettings.VSyncEnabled;
        if (_lastMetalDisplaySyncEnabled.HasValue && _lastMetalDisplaySyncEnabled.Value == enabled)
        {
            return;
        }

        MacOSWindowInterop.SetMetalLayerDisplaySyncEnabled(_metalLayer, enabled);
        _lastMetalDisplaySyncEnabled = enabled;
    }

    public MacOSWindowBackend(MacOSPlatformHost host, Window window)
    {
        _host = host;
        _window = window;
    }

    public nint Handle => _nsView;

    internal bool ImeHasMarkedText => _imeHasMarkedText;

    public void SetResizable(bool resizable)
    {
        if (_nsWindow == 0) return;

        const ulong NSWindowStyleMaskResizable = 8ul;
        ulong mask = MacOSWindowInterop.GetWindowStyleMask(_nsWindow);
        if (resizable)
            mask |= NSWindowStyleMaskResizable;
        else
            mask &= ~NSWindowStyleMaskResizable;
        MacOSWindowInterop.SetWindowStyleMask(_nsWindow, mask);

        MacOSWindowInterop.ApplyContentSizeConstraints(_nsWindow, _window);

        // Clamp the current window size if it violates the new constraints.
        var ws = _window.WindowSize;
        double curW = _window.Width;
        double curH = _window.Height;
        double clampedW = Math.Clamp(curW, ws.MinWidth, ws.MaxWidth);
        double clampedH = Math.Clamp(curH, ws.MinHeight, ws.MaxHeight);
        if (clampedW != curW || clampedH != curH)
        {
            MacOSWindowInterop.SetClientSize(_nsWindow, clampedW, clampedH);
        }
    }

    public void Show()
    {
        EnsureCreated();
        if (_nsWindow == 0)
        {
            throw new InvalidOperationException("NSWindow creation failed.");
        }

        UpdateDpiIfNeeded();
        UpdateClientSizeIfNeeded(forceLayout: true);

        if (_shown)
        {
            return;
        }

        _shown = true;
        MacOSWindowInterop.ShowWindow(_nsWindow);
        if (_allowsTransparency)
        {
            MacOSWindowInterop.SetWindowStyleMask(_nsWindow, 0ul);
        }
        _host.RegisterWindow(_nsWindow, this);
        Interlocked.Exchange(ref _needsRender, 1);
        _host.RequestRender();
    }

    public void Hide() { }

    public void Close()
    {
        if (_nsWindow != 0)
        {
            MacOSWindowInterop.CloseWindow(_nsWindow);
            RaiseClosedOnce();
        }
    }

    public void Invalidate(bool erase)
    {
        if (_nsWindow == 0)
        {
            return;
        }

        // Coalesce invalidations.
        if (Interlocked.Exchange(ref _needsRender, 1) == 1)
        {
            return;
        }

        _host.RequestRender();
    }

    public void SetTitle(string title)
    {
        if (_nsWindow != 0)
        {
            MacOSWindowInterop.SetTitle(_nsWindow, title ?? string.Empty);
        }
    }

    public void SetIcon(IconSource? icon) { }

    public void SetClientSize(double widthDip, double heightDip)
    {
        if (_nsWindow != 0)
        {
            MacOSWindowInterop.SetClientSize(_nsWindow, widthDip, heightDip);
        }
    }

    public Point GetPosition()
    {
        if (_nsWindow == 0)
        {
            return default;
        }

        var frame = MacOSWindowInterop.GetWindowFrame(_nsWindow);
        return new Point(frame.origin.x, frame.origin.y);
    }

    public void SetPosition(double leftDip, double topDip)
    {
        if (_nsWindow == 0)
        {
            return;
        }

        MacOSWindowInterop.SetWindowPosition(_nsWindow, leftDip, topDip);
    }

    public void CaptureMouse() { }

    public void ReleaseMouseCapture() { }

    public Point ClientToScreen(Point clientPointDip)
    {
        if (_nsWindow == 0)
        {
            return clientPointDip;
        }

        var client = _window.ClientSize;
        var windowPoint = new NSPoint(clientPointDip.X, client.Height - clientPointDip.Y);
        var screenPoint = MacOSWindowInterop.WindowConvertPointToScreen(_nsWindow, windowPoint);
        return new Point(screenPoint.x, screenPoint.y);
    }

    public Point ScreenToClient(Point screenPointPx)
    {
        if (_nsWindow == 0)
        {
            return screenPointPx;
        }

        var client = _window.ClientSize;
        var windowPoint = MacOSWindowInterop.WindowConvertPointFromScreen(_nsWindow, new NSPoint(screenPointPx.X, screenPointPx.Y));
        return new Point(windowPoint.x, client.Height - windowPoint.y);
    }

    public void EnsureTheme(bool isDark)
    {
        if (_nsWindow == 0)
        {
            return;
        }

        // Only override the window chrome when the app is not following the OS theme.
        if (!Application.IsRunning)
        {
            return;
        }

        ThemeVariant mode;
        try
        {
            mode = Application.Current.ThemeMode;
        }
        catch
        {
            return;
        }

        if (mode == ThemeVariant.System)
        {
            MacOSWindowInterop.ClearWindowAppearance(_nsWindow);
        }
        else
        {
            MacOSWindowInterop.SetWindowAppearance(_nsWindow, isDark);
        }
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;

        if (!enabled)
        {
            _window.ClearMouseOverState();
            _window.ClearMouseCaptureState();
        }

    }

    internal bool IsEnabled => _enabled;

    internal void NotifyInputWhenDisabled()
    {
        _window.NotifyInputWhenDisabled();
    }

    public void Activate()
    {
        if (_nsWindow == 0)
        {
            return;
        }

        MacOSWindowInterop.ActivateWindow(_nsWindow);
    }

    public void SetOwner(nint ownerHandle)
    {
        if (_nsWindow == 0)
        {
            return;
        }

        var ownerWindow = MacOSWindowInterop.GetWindowFromView(ownerHandle);
        if (ownerWindow == 0)
        {
            ownerWindow = ownerHandle;
        }

        MacOSWindowInterop.SetOwnerWindow(_nsWindow, ownerWindow);
        if (ownerWindow != 0)
        {
            long ownerLevel = MacOSWindowInterop.GetWindowLevel(ownerWindow);
            MacOSWindowInterop.SetWindowLevel(_nsWindow, ownerLevel + 1);
        }
        else
        {
            MacOSWindowInterop.SetWindowLevel(_nsWindow, _defaultWindowLevel);
        }
    }

    public void SetOpacity(double opacity)
    {
        _opacity = Math.Clamp(opacity, 0.0, 1.0);
        if (_nsWindow != 0)
        {
            MacOSWindowInterop.SetWindowOpacity(_nsWindow, _opacity);
        }
    }

    public void SetAllowsTransparency(bool allowsTransparency)
    {
        _allowsTransparency = allowsTransparency;
        if (_nsWindow != 0)
        {
            MacOSWindowInterop.SetWindowTransparency(_nsWindow, _nsView, _allowsTransparency);
            MacOSWindowInterop.SetWindowStyleMask(_nsWindow, _allowsTransparency ? 0ul : _defaultStyleMask);
            MacOSWindowInterop.SetTitlebarForTransparency(_nsWindow, _allowsTransparency);
            if (_nsContext != 0)
            {
                MacOSWindowInterop.SetOpenGLSurfaceOpacity(_nsContext, !_allowsTransparency);
            }
            if (_metalLayer != 0)
            {
                MacOSWindowInterop.SetLayerOpaque(_metalLayer, !_allowsTransparency);
            }
        }
    }

    public void Dispose()
    {
        _window.ClearMouseOverState();
        _window.ClearMouseCaptureState();
        if (_nsWindow != 0)
        {
            _host.UnregisterWindow(_nsWindow);
            MacOSWindowInterop.UnregisterWindowCloseTarget(_nsWindow);
            MacOSWindowInterop.UnregisterReshapeTarget(_nsView);
            MacOSWindowInterop.UnregisterTextInputTarget(_nsView);
            if (_metalLayer != 0)
            {
                MacOSWindowInterop.UnregisterMetalLayerTarget(_metalLayer);
            }
            MacOSWindowInterop.ReleaseWindow(_nsWindow);
            _nsWindow = 0;
            _nsView = 0;
            _nsContext = 0;
            _metalLayer = 0;
        }
    }

    private void EnsureCreated()
    {
        if (_nsWindow != 0)
        {
            return;
        }

        MacOSInterop.EnsureApplicationInitialized();
        _allowsTransparency = _window.AllowsTransparency;
        _nsWindow = MacOSWindowInterop.CreateWindow(
            title: _window.Title ?? "MewUI",
            widthDip: Math.Max(1, _window.Width),
            heightDip: Math.Max(1, _window.Height),
            allowsTransparency: _allowsTransparency);

        if (_nsWindow != 0)
        {
            if (_window.StartupLocation == WindowStartupLocation.CenterScreen)
            {
                MacOSWindowInterop.CenterWindow(_nsWindow);
            }

            // Prefer CAMetalLayer path when the active graphics factory requests it.
            // This avoids AppKit's "stretch last frame" behavior during live-resize by rendering from the layer draw cycle.
            if (_window.GraphicsFactory is IWindowSurfaceSelector { PreferredSurfaceKind: WindowSurfaceKind.Metal })
            {
                var (view, layer) = MacOSWindowInterop.AttachMetalLayerView(_nsWindow, _window.Width, _window.Height);
                _nsView = view;
                _metalLayer = layer;
                _nsContext = 0;
                MacOSWindowInterop.RegisterTextInputTarget(_nsView, this);
                MacOSWindowInterop.RegisterMetalLayerTarget(_metalLayer, this);
                MacOSWindowInterop.SetFirstResponder(_nsWindow, _nsView);
                if (_metalLayer != 0)
                {
                    MacOSWindowInterop.SetLayerOpaque(_metalLayer, !_allowsTransparency);
                }
                UpdateMetalLayerDisplaySyncIfNeeded();
            }
            else
            {
                var (view, ctx) = MacOSWindowInterop.AttachLegacyOpenGLView(_nsWindow, _window.Width, _window.Height);
                _nsView = view;
                _nsContext = ctx;
                _metalLayer = 0;
                MacOSWindowInterop.RegisterTextInputTarget(_nsView, this);
                MacOSWindowInterop.RegisterReshapeTarget(_nsView, this);
                MacOSWindowInterop.SetFirstResponder(_nsWindow, _nsView);
            }

            // Establish initial DPI once we have a view/screen.
            UpdateDpiIfNeeded(force: true);
            UpdateClientSizeIfNeeded(forceLayout: true);

            _defaultWindowLevel = MacOSWindowInterop.GetWindowLevel(_nsWindow);
            _defaultStyleMask = MacOSWindowInterop.GetWindowStyleMask(_nsWindow);
            MacOSWindowInterop.SetWindowOpacity(_nsWindow, _opacity);
            MacOSWindowInterop.SetWindowTransparency(_nsWindow, _nsView, _allowsTransparency);
            MacOSWindowInterop.SetWindowStyleMask(_nsWindow, _allowsTransparency ? 0ul : _defaultStyleMask);
            MacOSWindowInterop.SetTitlebarForTransparency(_nsWindow, _allowsTransparency);
            if (_nsContext != 0)
            {
                MacOSWindowInterop.SetOpenGLSurfaceOpacity(_nsContext, !_allowsTransparency);
            }
            MacOSWindowInterop.RegisterWindowCloseTarget(_nsWindow, this);
        }
    }

    internal void RaiseClosedOnce()
    {
        if (_closedRaised)
        {
            return;
        }

        _closedRaised = true;
        try
        {
            _window.RaiseClosed();
        }
        catch
        {
            // Never let exceptions escape the native callback.
        }
    }

    private void UpdateDpiIfNeeded(bool force = false)
    {
        if (_nsView == 0)
        {
            return;
        }

        double scale = MacOSInterop.GetBackingScaleFactorForView(_nsView);
        if (scale <= 0)
        {
            scale = 1.0;
        }

        if (!force && Math.Abs(scale - _lastDpiScale) < 0.001)
        {
            return;
        }

        _lastDpiScale = scale;
        uint newDpi = (uint)Math.Max(1, (int)Math.Round(96.0 * scale));
        uint oldDpi = _window.Dpi;
        if (oldDpi == newDpi)
        {
            return;
        }

        _window.SetDpi(newDpi);
        _window.RaiseDpiChanged(oldDpi, newDpi);

        // Ensure text/layout are recomputed at the new scale before the next frame.
        UpdateClientSizeIfNeeded(forceLayout: true);
    }

    private bool UpdateClientSizeIfNeeded(bool forceLayout = false, bool requestRender = true)
    {
        if (_nsView == 0)
        {
            return false;
        }

        var bounds = MacOSInterop.GetViewBounds(_nsView);
        double widthDip = Math.Max(1, bounds.size.width);
        double heightDip = Math.Max(1, bounds.size.height);

        var old = _window.ClientSize;
        if (!forceLayout &&
            Math.Abs(old.Width - widthDip) < 0.01 &&
            Math.Abs(old.Height - heightDip) < 0.01)
        {
            return false;
        }

        if (_metalLayer != 0)
        {
            MacOSWindowInterop.UpdateMetalLayerDrawableSize(_metalLayer, widthDip, heightDip, _lastDpiScale);
        }
        else if (_nsContext != 0)
        {
            // Ensure the OpenGL drawable is updated to the new view size. Without this, AppKit may stretch
            // the last rendered frame during live resize (content looks like a scaled bitmap until mouse-up).
            // Keep update() serialized with rendering/swap (avoid racing with the render thread).
            MacOSWindowInterop.LockOpenGLContext(_nsContext);
            try
            {
                MacOSWindowInterop.UpdateOpenGLContext(_nsContext);
            }
            finally
            {
                MacOSWindowInterop.UnlockOpenGLContext(_nsContext);
            }
        }

        _window.SetClientSizeDip(widthDip, heightDip);
        _window.RaiseClientSizeChanged(widthDip, heightDip);

        _window.PerformLayout();
        if (requestRender)
        {
            _window.Invalidate();
        }
        return true;
    }

    internal void ProcessNSEvent(nint ev)
    {
        if (ev == 0 || _nsWindow == 0)
        {
            return;
        }

        // Ignore internal wake events posted by the dispatcher (application-defined, no window).
        int type = MacOSInterop.GetEventType(ev);
        if (type == 15 && MacOSInterop.GetEventWindow(ev) == 0)
        {
            return;
        }

        if (!_enabled)
        {
            _window.NotifyInputWhenDisabled();
            return;
        }

        // Ensure we have up-to-date client size for coordinate transforms.
        _ = UpdateClientSizeIfNeeded();

        var loc = MacOSInterop.GetEventLocationInWindow(ev);
        var client = _window.ClientSize;
        var pos = new Point(loc.x, client.Height - loc.y);

        // Best-effort screen position (not used for tooltip; window-relative is enough).
        var screenPos = pos;
        _window.UpdateLastMousePosition(pos, screenPos);

        switch (type)
        {
            // Mouse moved / dragged
            case 5:  // NSEventTypeMouseMoved
            case 6:  // NSEventTypeLeftMouseDragged
            case 7:  // NSEventTypeRightMouseDragged
            case 27: // NSEventTypeOtherMouseDragged
                if (ShouldIgnoreMouseEvent(ev, pos, client, allowOutsideWhileCaptured: true))
                {
                    return;
                }
                HandleMouseMove(pos, screenPos);
                break;

            // Mouse down/up
            case 1:  // NSEventTypeLeftMouseDown
                HandleMouseButton(ev, pos, screenPos, MouseButton.Left, isDown: true);
                break;
            case 2:  // NSEventTypeLeftMouseUp
                HandleMouseButton(ev, pos, screenPos, MouseButton.Left, isDown: false);
                break;
            case 3:  // NSEventTypeRightMouseDown
                HandleMouseButton(ev, pos, screenPos, MouseButton.Right, isDown: true);
                break;
            case 4:  // NSEventTypeRightMouseUp
                HandleMouseButton(ev, pos, screenPos, MouseButton.Right, isDown: false);
                break;
            case 25: // NSEventTypeOtherMouseDown
                HandleMouseButton(ev, pos, screenPos, MapOtherMouseButton(ev), isDown: true);
                break;
            case 26: // NSEventTypeOtherMouseUp
                HandleMouseButton(ev, pos, screenPos, MapOtherMouseButton(ev), isDown: false);
                break;

            case 9: // NSEventTypeMouseExited
                WindowInputRouter.UpdateMouseOver(_window, null);
                break;

            case 22: // NSEventTypeScrollWheel
                if (ShouldIgnoreMouseEvent(ev, pos, client, allowOutsideWhileCaptured: true))
                {
                    return;
                }
                HandleMouseWheel(ev, pos, screenPos);
                break;

            case 10: // NSEventTypeKeyDown
                HandleKeyDown(ev);
                break;

            case 11: // NSEventTypeKeyUp
                HandleKeyUp(ev);
                break;
        }

    }

    private bool ShouldIgnoreMouseEvent(nint ev, Point pos, Size client, bool allowOutsideWhileCaptured)
    {
        var evWindow = MacOSInterop.GetEventWindow(ev);
        bool isCaptured = _window.HasMouseCapture || _leftDown || _rightDown || _middleDown;

        if (evWindow != 0 && evWindow != _nsWindow && !isCaptured)
        {
            return true;
        }

        if (!allowOutsideWhileCaptured || !isCaptured)
        {
            bool inside = pos.X >= 0 && pos.Y >= 0 && pos.X < client.Width && pos.Y < client.Height;
            if (!inside)
            {
                WindowInputRouter.UpdateMouseOver(_window, null);
                return true;
            }
        }

        return false;
    }

    private void HandleMouseMove(Point pos, Point screenPos)
    {
        WindowInputRouter.MouseMove(_window, pos, screenPos, _leftDown, _rightDown, _middleDown);
    }

    private void HandleMouseButton(nint ev, Point pos, Point screenPos, MouseButton button, bool isDown)
    {
        // Keep our view as first responder so key input / NSTextInputClient (IME) continues to work
        // after mouse interaction. AppKit can move first responder to internal views during activation.
        if (isDown && _nsWindow != 0 && _nsView != 0)
        {
            MacOSWindowInterop.SetFirstResponder(_nsWindow, _nsView);
        }

        int clickCount;
        int buttonIndex = (int)button;
        if (isDown)
        {
            clickCount = MacOSInterop.GetEventClickCount(ev);
            if ((uint)buttonIndex < (uint)_lastPressClickCounts.Length)
            {
                _lastPressClickCounts[buttonIndex] = clickCount <= 0 ? 1 : clickCount;
            }
        }
        else
        {
            clickCount = (uint)buttonIndex < (uint)_lastPressClickCounts.Length ? _lastPressClickCounts[buttonIndex] : 1;
            if (clickCount <= 0)
            {
                clickCount = 1;
            }
        }

        switch (button)
        {
            case MouseButton.Left:
                _leftDown = isDown;
                break;
            case MouseButton.Right:
                _rightDown = isDown;
                break;
            case MouseButton.Middle:
                _middleDown = isDown;
                break;
        }

        WindowInputRouter.MouseButton(
            _window,
            pos,
            screenPos,
            button,
            isDown,
            _leftDown,
            _rightDown,
            _middleDown,
            clickCount);
    }

    private void HandleMouseWheel(nint ev, Point pos, Point screenPos)
    {
        // Prefer high-precision deltas when available, but fall back to legacy deltaX/deltaY for devices
        // where scrollingDelta returns 0.
        double dy = MacOSInterop.GetEventScrollingDeltaY(ev);
        double dx = MacOSInterop.GetEventScrollingDeltaX(ev);
        if (dy == 0)
        {
            dy = MacOSInterop.GetEventDeltaY(ev);
        }
        if (dx == 0)
        {
            dx = MacOSInterop.GetEventDeltaX(ev);
        }

        // Normalize to "wheel units" (~120 per notch), but accumulate fractional deltas so trackpads
        // still scroll even when individual events are small.
        _wheelRemainderY += dy * 120.0;
        _wheelRemainderX += dx * 120.0;

        int deltaY = (int)Math.Truncate(_wheelRemainderY);
        int deltaX = (int)Math.Truncate(_wheelRemainderX);

        if (deltaY != 0)
        {
            _wheelRemainderY -= deltaY;
        }
        if (deltaX != 0)
        {
            _wheelRemainderX -= deltaX;
        }

        if (deltaX != 0)
        {
            WindowInputRouter.MouseWheel(_window, pos, screenPos, deltaX, isHorizontal: true, _leftDown, _rightDown, _middleDown);
        }

        if (deltaY != 0)
        {
            WindowInputRouter.MouseWheel(_window, pos, screenPos, deltaY, isHorizontal: false, _leftDown, _rightDown, _middleDown);
        }
    }

    private void HandleKeyDown(nint ev)
    {
        int platformKey = MacOSInterop.GetEventKeyCode(ev);
        var modifiers = GetModifierKeys(ev);
        var key = MapKey(ev, platformKey);

        var oldImeState = _imeState;
        _forwardKeyToAppThisKeyDown = false;
        _pendingKeyDownTextInput = null;

        _isHandlingKeyDown = true;
        try
        {
            // Route through NSTextInputClient so IME/dead-keys AND plain text input can be delivered via insertText/setMarkedText.
            // This must not be gated on PreviewKeyDown handling: containers may handle key events (e.g. shortcuts),
            // but IME still needs the native key events to finalize composition and deliver committed text.
            if (_nsWindow != 0 && _nsView != 0)
            {
                MacOSWindowInterop.SetFirstResponder(_nsWindow, _nsView);
                MacOSWindowInterop.InterpretKeyEvent(_nsView, ev);
                ImeLogger.Write($"InterpretKeyEvent view=0x{_nsView:x} window=0x{_nsWindow:x} imeHasMarked={_imeHasMarkedText} imeState={_imeState}");
            }

            // Shortcuts (Ctrl/Cmd chords) must be routed as key events even while IME is composing.
            // Otherwise, common shortcuts like Ctrl+Z/C/V will stop working during preedit.
            // This mirrors typical Cocoa behavior: text services should not "eat" modifier chords that
            // don't represent text input.
            bool isShortcutChord = (modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Windows)) &&
                                   key != Key.None;
            if (isShortcutChord)
            {
                _forwardKeyToAppThisKeyDown = true;
                _forceKeyUps.Add(platformKey);
            }

            bool hadImeInput = _imeState switch
            {
                ImeState.Committed => true,
                ImeState.Preedit => true,
                _ => oldImeState != _imeState,
            };

            // Allow normal key processing after commit, but still treat this keyDown as IME-related.
            if (_imeState == ImeState.Committed)
            {
                _imeState = ImeState.Ground;
            }

            // winit behavior: only forward KeyDown when IME didn't handle it, OR when doCommandBySelector requested it.
            if (hadImeInput && !_forwardKeyToAppThisKeyDown)
            {
                ImeLogger.Write($"KeyDown suppressed (IME handled). oldImeState={oldImeState} imeState={_imeState}");
                return;
            }

            var args = new KeyEventArgs(key, platformKey, modifiers, isRepeat: false);
            _window.RaisePreviewKeyDown(args);

            ImeLogger.Write($"KeyDown ev=0x{ev:x} keyCode=0x{platformKey:x} key={key} mods={modifiers} handled(preview)={args.Handled} focused={_window.FocusManager.FocusedElement?.GetType().Name ?? "null"} imeState={_imeState} forwardToApp={_forwardKeyToAppThisKeyDown}");

            if (args.Handled)
            {
                return;
            }

            WindowInputRouter.KeyDown(_window, args);

            // WPF-like Tab behavior:
            // - Always let the focused element see KeyDown first.
            // - Only perform focus navigation if the key is still unhandled.
            //
            // When IME composition is active, many IMEs use Tab to navigate candidates,
            // so we must not steal it.
            if (!args.Handled && args.Key == Key.Tab && !_imeHasMarkedText)
            {
                if (modifiers.HasFlag(ModifierKeys.Shift))
                {
                    _window.FocusManager.MoveFocusPrevious();
                }
                else
                {
                    _window.FocusManager.MoveFocusNext();
                }

                _pendingKeyDownTextInput = null;
                return;
            }

            // If insertText delivered a Tab/newline during this keyDown, defer it until after KeyDown routing.
            // This allows KeyDown handlers (e.g. AcceptTab/AcceptReturn) to suppress text input consistently.
            if (_pendingKeyDownTextInput is { Length: > 0 } pending)
            {
                if (args.Handled)
                {
                    ImeLogger.Write($"  pending insertText suppressed by handled KeyDown. pending='{Truncate(pending)}'");
                    return;
                }

                var textArgs = new TextInputEventArgs(pending);
                _window.RaisePreviewTextInput(textArgs);
                if (!textArgs.Handled)
                {
                    if (_window.FocusManager.FocusedElement is ITextInputClient client)
                    {
                        client.HandleTextInput(textArgs);
                    }
                }

                ImeLogger.Write($"  pending insertText emitted TextInput handled={textArgs.Handled}");
            }
        }
        finally
        {
            _isHandlingKeyDown = false;
            _pendingKeyDownTextInput = null;
        }
    }

    private void HandleKeyUp(nint ev)
    {
        int platformKey = MacOSInterop.GetEventKeyCode(ev);
        if (!_forceKeyUps.Remove(platformKey) && _imeState is not (ImeState.Ground or ImeState.Disabled))
        {
            ImeLogger.Write($"KeyUp suppressed keyCode=0x{platformKey:x} imeState={_imeState}");
            return;
        }

        var modifiers = GetModifierKeys(ev);
        var key = MapKey(ev, platformKey);

        var args = new KeyEventArgs(key, platformKey, modifiers, isRepeat: false);
        _window.RaisePreviewKeyUp(args);
        if (args.Handled)
        {
            return;
        }

        WindowInputRouter.KeyUp(_window, args);
    }

    internal void ImeSetMarkedText(string? text)
        => ImeSetMarkedText(text, new NSRange(NSNotFound, 0));

    internal void ImeSetMarkedText(string? text, NSRange replacementRange)
    {
        text ??= string.Empty;
        ImeLogger.Write($"setMarkedText len={text.Length} text='{Truncate(text)}' repl=({replacementRange.location},{replacementRange.length}) imeHasMarked(before)={_imeHasMarkedText} focused={_window.FocusManager.FocusedElement?.GetType().Name ?? "null"}");

        // Some IMEs clear preedit by calling setMarkedText("") instead of unmarkText.
        // Treat empty marked text as "composition ended".
        if (text.Length == 0)
        {
            ImeUnmarkText();
            return;
        }

        if (!_imeHasMarkedText)
        {
            // If the platform provides a replacement range, align our selection/caret so the IME composition
            // replaces the correct portion of the document.
            // (AppKit's NSRange is UTF-16 based, which matches .NET string indexing.)
            if (replacementRange.location != NSNotFound && _window.FocusManager.FocusedElement is Controls.TextBase tb2)
            {
                int start = (int)replacementRange.location;
                int end = start + (int)replacementRange.length;
                tb2.SetSelectionRangeForPlatform(start, end);
            }

            _imeHasMarkedText = true;
            _imeMarkedText = string.Empty;
            _imeState = ImeState.Preedit;

            var startArgs = new TextCompositionEventArgs();
            _window.RaisePreviewTextCompositionStart(startArgs);
            if (!startArgs.Handled)
            {
                if (_window.FocusManager.FocusedElement is ITextCompositionClient client)
                {
                    client.HandleTextCompositionStart(startArgs);
                }
            }
        }

        _imeMarkedText = text;
        _imeState = ImeState.Preedit;
        var updateArgs = new TextCompositionEventArgs(text);
        _window.RaisePreviewTextCompositionUpdate(updateArgs);
        if (!updateArgs.Handled)
        {
            if (_window.FocusManager.FocusedElement is ITextCompositionClient client)
            {
                client.HandleTextCompositionUpdate(updateArgs);
            }
        }

        if (_window.FocusManager.FocusedElement is Controls.TextBase tb)
        {
            ImeLogger.Write($"  TextBase composingStart={tb.CompositionStartIndex} composingLen={tb.CompositionLength} caret={tb.CaretPosition} textLen={tb.TextLengthInternal}");
            try
            {
                int textLen = tb.TextLengthInternal;
                int compStart = Math.Max(0, tb.CompositionStartIndex);
                int compLen = Math.Max(0, tb.CompositionLength);

                string compText = (compLen > 0 && compStart + compLen <= textLen)
                    ? tb.GetTextSubstringInternal(compStart, compLen)
                    : string.Empty;

                int tailLen = Math.Min(32, textLen);
                string tail = tailLen > 0 ? tb.GetTextSubstringInternal(textLen - tailLen, tailLen) : string.Empty;

                var (selStart, selEnd) = tb.SelectionRange;
                ImeLogger.Write($"    TextBase selection=({selStart},{selEnd}) compText='{Truncate(compText)}' tail='{Truncate(tail)}'");
            }
            catch
            {
            }
        }
    }

    internal void ImeUnmarkText()
    {
        ImeLogger.Write($"unmarkText imeHasMarked(before)={_imeHasMarkedText} markedLen={_imeMarkedText.Length} focused={_window.FocusManager.FocusedElement?.GetType().Name ?? "null"}");
        if (!_imeHasMarkedText)
        {
            return;
        }

        var endArgs = new TextCompositionEventArgs(_imeMarkedText);
        _window.RaisePreviewTextCompositionEnd(endArgs);
        if (!endArgs.Handled)
        {
            if (_window.FocusManager.FocusedElement is ITextCompositionClient client)
            {
                client.HandleTextCompositionEnd(endArgs);
            }
        }

        _imeHasMarkedText = false;
        _imeMarkedText = string.Empty;
        _imeState = ImeState.Ground;
    }

    internal void ImeInsertText(string? text)
        => ImeInsertText(text, new NSRange(NSNotFound, 0));

    internal void ImeInsertText(string? text, NSRange replacementRange)
    {
        text ??= string.Empty;
        ImeLogger.Write($"insertText len={text.Length} text='{Truncate(text)}' repl=({replacementRange.location},{replacementRange.length}) imeHasMarked={_imeHasMarkedText} focused={_window.FocusManager.FocusedElement?.GetType().Name ?? "null"}");
        if (text.Length == 0)
        {
            return;
        }

        // If the platform provides a replacement range, align our selection/caret so the inserted text
        // replaces the intended portion of the document.
        if (replacementRange.location != NSNotFound && _window.FocusManager.FocusedElement is Controls.TextBase tbReplace)
        {
            int start = (int)replacementRange.location;
            int end = start + (int)replacementRange.length;
            tbReplace.SetSelectionRangeForPlatform(start, end);
        }

        // IME commit: AppKit typically calls insertText while we still have marked text.
        // Do NOT route this through TextInput. The composition pipeline already materializes the preedit text.
        if (_imeHasMarkedText && _window.FocusManager.FocusedElement is Controls.TextBase tb)
        {
            if (!string.Equals(text, _imeMarkedText, StringComparison.Ordinal))
            {
                // Ensure the document contains the final committed string before committing.
                ImeSetMarkedText(text);
            }

            var endArgs = new TextCompositionEventArgs(text);
            _window.RaisePreviewTextCompositionEnd(endArgs);
            if (!endArgs.Handled)
            {
                tb.CommitTextCompositionInternal();
            }

            _imeHasMarkedText = false;
            _imeMarkedText = string.Empty;
            _imeState = ImeState.Committed;
            ImeLogger.Write("  insertText handled as IME commit (no TextInput emitted).");
            return;
        }

        // Cocoa routes plain text input through insertText during keyDown handling.
        // For Tab/Enter we defer dispatch until after KeyDown routing so KeyDown handlers can suppress
        // the corresponding text input consistently (WPF-like behavior).
        if (_isHandlingKeyDown && !_imeHasMarkedText && _imeState == ImeState.Ground)
        {
            var normalized = TextInputEventArgs.NormalizeText(text);
            if (normalized is "\t" or "\n")
            {
                _pendingKeyDownTextInput = normalized;
                ImeLogger.Write($"  insertText buffered for post-KeyDown dispatch. pending='{Truncate(normalized)}'");
                return;
            }
        }

        // Filter out non-text control characters. Key navigation/editing is handled via KeyDown.
        bool hasPrintable = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (!char.IsControl(c) || c == '\n' || c == '\r' || c == '\t')
            {
                hasPrintable = true;
                break;
            }
        }

        if (!hasPrintable)
        {
            return;
        }

        var textArgs = new TextInputEventArgs(text);
        _window.RaisePreviewTextInput(textArgs);
        if (!textArgs.Handled)
        {
            if (_window.FocusManager.FocusedElement is ITextInputClient client)
            {
                client.HandleTextInput(textArgs);
            }
        }
        ImeLogger.Write($"  insertText emitted TextInput handled={textArgs.Handled}");
    }

    internal void ImeDoCommandBySelector(nint selector)
    {
        // winit:
        // - if the text was just committed, ignore the command selector to avoid double-send (notably Enter/Space).
        // - otherwise forward key to app for this keyDown.
        if (_imeState == ImeState.Committed)
        {
            return;
        }

        _forwardKeyToAppThisKeyDown = true;

        // If we are in preedit, leave it so we also report key-up for this key.
        if (_imeHasMarkedText && _imeState == ImeState.Preedit)
        {
            _imeState = ImeState.Ground;
        }
    }

    private static MouseButton MapOtherMouseButton(nint ev)
    {
        int n = MacOSInterop.GetEventButtonNumber(ev);
        return n switch
        {
            0 => MouseButton.Left,
            1 => MouseButton.Right,
            2 => MouseButton.Middle,
            3 => MouseButton.XButton1,
            4 => MouseButton.XButton2,
            _ => MouseButton.Middle
        };
    }

    private static ModifierKeys GetModifierKeys(nint ev)
    {
        // NSEventModifierFlags:
        // Shift (1<<17), Control (1<<18), Option/Alt (1<<19), Command (1<<20).
        ulong flags = MacOSInterop.GetEventModifierFlags(ev);
        var m = ModifierKeys.None;
        if ((flags & (1ul << 17)) != 0) m |= ModifierKeys.Shift;
        if ((flags & (1ul << 18)) != 0) m |= ModifierKeys.Control;
        if ((flags & (1ul << 19)) != 0) m |= ModifierKeys.Alt;
        if ((flags & (1ul << 20)) != 0) m |= ModifierKeys.Windows;
        return m;
    }

    private static Key MapKey(nint ev, int keyCode)
    {
        // Map physical macOS virtual key codes first so shortcuts (Ctrl/Cmd+Z/C/V/...) work even when
        // NSEvent characters are suppressed/modified by modifiers.
        return keyCode switch
        {
            // Letters (kVK_ANSI_*)
            0x00 => Key.A,
            0x01 => Key.S,
            0x02 => Key.D,
            0x03 => Key.F,
            0x04 => Key.H,
            0x05 => Key.G,
            0x06 => Key.Z,
            0x07 => Key.X,
            0x08 => Key.C,
            0x09 => Key.V,
            0x0B => Key.B,
            0x0C => Key.Q,
            0x0D => Key.W,
            0x0E => Key.E,
            0x0F => Key.R,
            0x10 => Key.Y,
            0x11 => Key.T,
            0x12 => Key.D1,
            0x13 => Key.D2,
            0x14 => Key.D3,
            0x15 => Key.D4,
            0x16 => Key.D6,
            0x17 => Key.D5,
            0x19 => Key.D9,
            0x1A => Key.D7,
            0x1C => Key.D8,
            0x1D => Key.D0,
            0x1F => Key.O,
            0x20 => Key.U,
            0x22 => Key.I,
            0x23 => Key.P,
            0x25 => Key.L,
            0x26 => Key.J,
            0x28 => Key.K,
            0x2D => Key.N,
            0x2E => Key.M,

            0x24 => Key.Enter,      // kVK_Return
            0x30 => Key.Tab,        // kVK_Tab
            0x31 => Key.Space,      // kVK_Space
            0x33 => Key.Backspace,  // kVK_Delete (backspace)
            0x35 => Key.Escape,     // kVK_Escape
            0x75 => Key.Delete,     // kVK_ForwardDelete

            0x7B => Key.Left,
            0x7C => Key.Right,
            0x7D => Key.Down,
            0x7E => Key.Up,

            0x73 => Key.Home,
            0x77 => Key.End,
            0x74 => Key.PageUp,
            0x79 => Key.PageDown,

            // Function keys (kVK_F1..kVK_F12). Apple key codes are non-contiguous.
            0x7A => Key.F1,
            0x78 => Key.F2,
            0x63 => Key.F3,
            0x76 => Key.F4,
            0x60 => Key.F5,
            0x61 => Key.F6,
            0x62 => Key.F7,
            0x64 => Key.F8,
            0x65 => Key.F9,
            0x6D => Key.F10,
            0x67 => Key.F11,
            0x6F => Key.F12,
            _ => MapKeyFromCharacters(ev)
        };
    }

    private static Key MapKeyFromCharacters(nint ev)
    {
        var text = MacOSInterop.GetEventCharactersIgnoringModifiers(ev);
        if (string.IsNullOrEmpty(text) || text.Length != 1)
        {
            return Key.None;
        }

        char c = text[0];
        if (c >= '0' && c <= '9')
        {
            return (Key)((int)Key.D0 + (c - '0'));
        }

        if (c >= 'a' && c <= 'z')
        {
            c = (char)(c - 32);
        }

        if (c >= 'A' && c <= 'Z')
        {
            return (Key)((int)Key.A + (c - 'A'));
        }

        return Key.None;
    }

    internal bool NeedsRender
    {
        get => Volatile.Read(ref _needsRender) != 0;
    }

    internal void RenderIfNeeded()
    {
        if (Interlocked.Exchange(ref _needsRender, 0) == 0)
        {
            return;
        }

        RenderNow();
    }

    internal void RenderNow()
    {
        if (_nsView == 0)
        {
            return;
        }

        UpdateDpiIfNeeded();
        // If we're rendering right now, avoid scheduling another render from size-change invalidations.
        UpdateClientSizeIfNeeded(requestRender: false);
        UpdateMetalLayerDisplaySyncIfNeeded();

        if (_metalLayer != 0)
        {
            // For CAMetalLayer-backed views, render from AppKit's display cycle (displayLayer:).
            // During live-resize, forcing a synchronous display can race with the resize transaction and reintroduce
            // "scaled cached frame" artifacts. Mark the view as needing display and let AppKit drive displayLayer:.
            if (MacOSWindowInterop.IsViewInLiveResize(_nsView))
            {
                // IMPORTANT: Mark the CAMetalLayer (not the NSView) so AppKit will call displayLayer:
                // on our layer delegate. Marking the view alone may not trigger displayLayer:.
                MacOSWindowInterop.SetLayerNeedsDisplay(_metalLayer);
            }
            else
            {
                // IMPORTANT: Force a synchronous layer display so input-driven invalidations are visible
                // immediately (mouse over/scroll/animations). Displaying the NSView does not reliably
                // invoke the CAMetalLayer delegate.
                MacOSWindowInterop.DisplayLayerIfNeeded(_metalLayer);
            }
            return;
        }

        if (_nsContext == 0)
        {
            return;
        }

        MacOSWindowInterop.LockOpenGLContext(_nsContext);
        try
        {
            // Keep the drawable in sync with the view size during live-resize.
            MacOSWindowInterop.UpdateOpenGLContext(_nsContext);
            _window.RenderFrame(CreateOpenGLSurface());
        }
        finally
        {
            MacOSWindowInterop.UnlockOpenGLContext(_nsContext);
        }
    }

    private MacOSOpenGLSurface CreateOpenGLSurface()
    {
        var clientSize = _window.ClientSize;
        int pixelWidth = (int)Math.Max(1, Math.Ceiling(clientSize.Width * _window.DpiScale));
        int pixelHeight = (int)Math.Max(1, Math.Ceiling(clientSize.Height * _window.DpiScale));
        return new MacOSOpenGLSurface(_nsView, _nsContext, pixelWidth, pixelHeight, _window.DpiScale);
    }

    private MacOSMetalSurface CreateMetalSurface()
    {
        var clientSize = _window.ClientSize;
        int pixelWidth = (int)Math.Max(1, Math.Ceiling(clientSize.Width * _window.DpiScale));
        int pixelHeight = (int)Math.Max(1, Math.Ceiling(clientSize.Height * _window.DpiScale));
        return new MacOSMetalSurface(_nsView, _metalLayer, pixelWidth, pixelHeight, _window.DpiScale);
    }

    internal void RenderFromMetalLayerDisplay(nint layer)
    {
        if (_nsView == 0 || _metalLayer == 0 || layer != _metalLayer)
        {
            return;
        }

        UpdateMetalLayerDisplaySyncIfNeeded();

        // Avoid re-entrant displayLayer-triggered renders.
        if (Interlocked.Exchange(ref _reshapeRendering, 1) != 0)
        {
            return;
        }

        try
        {
            UpdateDpiIfNeeded();
            // Force layout to align with live-resize updates, but do not schedule another render.
            UpdateClientSizeIfNeeded(forceLayout: true, requestRender: false);
            _window.RenderFrame(CreateMetalSurface());
        }
        finally
        {
            Volatile.Write(ref _reshapeRendering, 0);
        }
    }

    internal void OnNativeViewReshape()
    {
        if (_nsView == 0 || _nsContext == 0)
        {
            return;
        }

        // Avoid re-entrant reshape-triggered renders.
        if (Interlocked.Exchange(ref _reshapeRendering, 1) != 0)
        {
            return;
        }

        try
        {
            UpdateDpiIfNeeded();
            bool inLiveResize = MacOSWindowInterop.IsViewInLiveResize(_nsView);
            UpdateClientSizeIfNeeded(forceLayout: true, requestRender: !inLiveResize);

            if (inLiveResize)
            {
                // Request a synchronous display. Rendering is performed from the view's draw cycle (drawRect),
                // which keeps the update aligned with AppKit's live-resize display timing.
                MacOSWindowInterop.DisplayIfNeeded(_nsView);
            }
            else
            {
                Invalidate(erase: false);
            }
        }
        finally
        {
            Volatile.Write(ref _reshapeRendering, 0);
        }
    }

    // Live resize is handled by the native -[NSOpenGLView reshape] callback (see MacOSWindowInterop).

    private sealed class MacOSOpenGLSurface : IMacOSOpenGLWindowSurface
    {
        public WindowSurfaceKind Kind => WindowSurfaceKind.OpenGL;

        public nint Handle => View;

        public int PixelWidth { get; }

        public int PixelHeight { get; }

        public double DpiScale { get; }

        public nint View { get; }

        public nint OpenGLContext { get; }

        public MacOSOpenGLSurface(nint view, nint context, int pixelWidth, int pixelHeight, double dpiScale)
        {
            View = view;
            OpenGLContext = context;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            DpiScale = dpiScale <= 0 ? 1.0 : dpiScale;
        }
    }

    private sealed class MacOSMetalSurface : IMacOSMetalWindowSurface
    {
        public WindowSurfaceKind Kind => WindowSurfaceKind.Metal;

        public nint Handle => MetalLayer;

        public int PixelWidth { get; }

        public int PixelHeight { get; }

        public double DpiScale { get; }

        public nint View { get; }

        public nint MetalLayer { get; }

        public MacOSMetalSurface(nint view, nint metalLayer, int pixelWidth, int pixelHeight, double dpiScale)
        {
            View = view;
            MetalLayer = metalLayer;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            DpiScale = dpiScale <= 0 ? 1.0 : dpiScale;
        }
    }
}

internal static unsafe class MacOSWindowInterop
{
    private static bool _initialized;

    private static nint ClsNSWindow;
    private static nint ClsNSString;
    private static nint ClsNSOpenGLView;
    private static nint ClsMewUIOpenGLView;
    private static nint ClsNSOpenGLPixelFormat;
    private static nint ClsNSAppearance;
    private static nint ClsNSColor;
    private static nint ClsNSArray;
    private static nint ClsMewUITextInputView;

    private static nint SelAlloc;
    private static nint SelInitWithContentRect;
    private static nint SelMakeKeyAndOrderFront;
    private static nint SelClose;
    private static nint SelSetTitle;
    private static nint SelSetContentSize;
    private static nint SelSetContentView;
    private static nint SelSetAcceptsMouseMovedEvents;
    private static nint SelSetIgnoresMouseEvents;
    private static nint SelCenter;
    private static nint SelRelease;
    private static nint SelSetAppearance;
    private static nint SelSetAlphaValue;
    private static nint SelSetBackgroundColor;
    private static nint SelInit;
    private static nint SelInitWithFrame;
    private static nint SelSetLayer;
    private static nint SelSetDelegate;
    private static nint SelSetDrawableSize;
    private static nint SelSetContentsScale;
    private static nint SelSetPresentsWithTransaction;
    private static nint SelSetAllowsNextDrawableTimeout;
    private static nint SelSetDisplaySyncEnabled;
    private static nint SelSetContentMinSize;
    private static nint SelSetContentMaxSize;
    private static nint SelSetParentWindow;
    private static nint SelSetLevel;
    private static nint SelLevel;
    private static nint SelMakeFirstResponder;
    private static nint SelSetStyleMask;
    private static nint SelStyleMask;
    private static nint SelSetTitleVisibility;
    private static nint SelSetTitlebarAppearsTransparent;
    private static nint SelSetMovableByWindowBackground;
    private static nint SelSetFrameOrigin;
    private static nint SelFrame;
    private static nint SelConvertPointToScreen;
    private static nint SelConvertPointFromScreen;
    private static nint SelWindow;
    private static nint SelWindowWillClose;
    private static nint SelObject;
    private static nint SelInterpretKeyEvents;
    private static nint SelArrayWithObject;

    private static nint SelInitWithAttributes;
    private static nint SelInitWithFramePixelFormat;
    private static nint SelOpenGLContext;
    private static nint SelSetWantsBestResolution;
    private static nint SelSetAutoresizingMask;
    private static nint SelSetOpaque;
    private static nint SelInLiveResize;
    private static nint SelUpdateOpenGLContext;
    private static nint SelReshape;
    private static nint SelDrawRect;
    private static nint SelSetValuesForParameter;
    private static nint SelCGLContextObj;
    private static nint SelSetNeedsDisplay;
    private static nint SelSetNeedsDisplayNoArgs;
    private static nint SelDisplayIfNeeded;
    private static nint SelDisplay;
    private static nint SelSetWantsLayer;
    private static nint SelSetLayerContentsRedrawPolicy;
    private static nint SelLayer;
    private static nint SelSetNeedsDisplayOnBoundsChange;
    private static nint SelClearColor;

    private static nint SelAppearanceNamed;
    private static nint _appearanceNameAqua;
    private static nint _appearanceNameDarkAqua;

    private static nint ClsNSView;
    private static nint ClsCAMetalLayer;
    private static nint ClsNSObject;
    private static nint ClsMewUIMetalLayerDelegate;
    private static nint _sharedMetalLayerDelegate;
    private static nint ClsMewUIWindowDelegate;
    private static nint _sharedWindowDelegate;
    private static readonly Dictionary<nint, WeakReference<MacOSWindowBackend>> _windowCloseTargets = new();

    // NSWindowStyleMaskTitled | Closable | Miniaturizable | Resizable
    private const ulong DefaultStyleMask = 1ul | 2ul | 4ul | 8ul;
    private const int NSBackingStoreBuffered = 2;
    private const ulong NSViewWidthSizable = 2;
    private const ulong NSViewHeightSizable = 16;

    private static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        // Ensure frameworks are loaded and NSApplication is initialized before resolving classes/selectors.
        MacOSInterop.EnsureApplicationInitialized();

        ClsNSWindow = ObjC.GetClass("NSWindow");
        ClsNSString = ObjC.GetClass("NSString");
        ClsNSArray = ObjC.GetClass("NSArray");
        ClsNSOpenGLView = ObjC.GetClass("NSOpenGLView");
        ClsNSOpenGLPixelFormat = ObjC.GetClass("NSOpenGLPixelFormat");
        ClsNSAppearance = ObjC.GetClass("NSAppearance");
        ClsNSColor = ObjC.GetClass("NSColor");

        SelAlloc = ObjC.Sel("alloc");
        SelInitWithContentRect = ObjC.Sel("initWithContentRect:styleMask:backing:defer:");
        SelMakeKeyAndOrderFront = ObjC.Sel("makeKeyAndOrderFront:");
        SelClose = ObjC.Sel("close");
        SelSetTitle = ObjC.Sel("setTitle:");
        SelSetContentSize = ObjC.Sel("setContentSize:");
        SelSetContentView = ObjC.Sel("setContentView:");
        SelSetAcceptsMouseMovedEvents = ObjC.Sel("setAcceptsMouseMovedEvents:");
        SelSetIgnoresMouseEvents = ObjC.Sel("setIgnoresMouseEvents:");
        SelCenter = ObjC.Sel("center");
        SelRelease = ObjC.Sel("release");
        SelSetAppearance = ObjC.Sel("setAppearance:");
        SelSetAlphaValue = ObjC.Sel("setAlphaValue:");
        SelSetBackgroundColor = ObjC.Sel("setBackgroundColor:");
        SelInit = ObjC.Sel("init");
        SelInitWithFrame = ObjC.Sel("initWithFrame:");
        SelSetLayer = ObjC.Sel("setLayer:");
        SelSetDelegate = ObjC.Sel("setDelegate:");
        SelSetDrawableSize = ObjC.Sel("setDrawableSize:");
        SelSetContentsScale = ObjC.Sel("setContentsScale:");
        SelSetPresentsWithTransaction = ObjC.Sel("setPresentsWithTransaction:");
        SelSetAllowsNextDrawableTimeout = ObjC.Sel("setAllowsNextDrawableTimeout:");
        SelSetDisplaySyncEnabled = ObjC.Sel("setDisplaySyncEnabled:");
        SelSetContentMinSize = ObjC.Sel("setContentMinSize:");
        SelSetContentMaxSize = ObjC.Sel("setContentMaxSize:");
        SelSetParentWindow = ObjC.Sel("setParentWindow:");
        SelSetLevel = ObjC.Sel("setLevel:");
        SelLevel = ObjC.Sel("level");
        SelMakeFirstResponder = ObjC.Sel("makeFirstResponder:");
        SelSetStyleMask = ObjC.Sel("setStyleMask:");
        SelStyleMask = ObjC.Sel("styleMask");
        SelSetTitleVisibility = ObjC.Sel("setTitleVisibility:");
        SelSetTitlebarAppearsTransparent = ObjC.Sel("setTitlebarAppearsTransparent:");
        SelSetMovableByWindowBackground = ObjC.Sel("setMovableByWindowBackground:");
        SelSetFrameOrigin = ObjC.Sel("setFrameOrigin:");
        SelFrame = ObjC.Sel("frame");
        SelConvertPointToScreen = ObjC.Sel("convertPointToScreen:");
        SelConvertPointFromScreen = ObjC.Sel("convertPointFromScreen:");
        SelWindow = ObjC.Sel("window");
        SelWindowWillClose = ObjC.Sel("windowWillClose:");
        SelObject = ObjC.Sel("object");
        SelInterpretKeyEvents = ObjC.Sel("interpretKeyEvents:");
        SelArrayWithObject = ObjC.Sel("arrayWithObject:");

        SelInitWithAttributes = ObjC.Sel("initWithAttributes:");
        SelInitWithFramePixelFormat = ObjC.Sel("initWithFrame:pixelFormat:");
        SelOpenGLContext = ObjC.Sel("openGLContext");
        SelSetWantsBestResolution = ObjC.Sel("setWantsBestResolutionOpenGLSurface:");
        SelSetAutoresizingMask = ObjC.Sel("setAutoresizingMask:");
        SelSetOpaque = ObjC.Sel("setOpaque:");
        SelInLiveResize = ObjC.Sel("inLiveResize");
        SelUpdateOpenGLContext = ObjC.Sel("update");
        SelReshape = ObjC.Sel("reshape");
        SelDrawRect = ObjC.Sel("drawRect:");
        SelSetValuesForParameter = ObjC.Sel("setValues:forParameter:");
        SelCGLContextObj = ObjC.Sel("CGLContextObj");
        SelSetNeedsDisplay = ObjC.Sel("setNeedsDisplay:");
        SelSetNeedsDisplayNoArgs = ObjC.Sel("setNeedsDisplay");
        SelDisplayIfNeeded = ObjC.Sel("displayIfNeeded");
        SelDisplay = ObjC.Sel("display");
        SelSetWantsLayer = ObjC.Sel("setWantsLayer:");
        SelSetLayerContentsRedrawPolicy = ObjC.Sel("setLayerContentsRedrawPolicy:");
        SelLayer = ObjC.Sel("layer");
        SelSetNeedsDisplayOnBoundsChange = ObjC.Sel("setNeedsDisplayOnBoundsChange:");
        SelClearColor = ObjC.Sel("clearColor");

        EnsureOpenGLViewSubclass();

        SelAppearanceNamed = ObjC.Sel("appearanceNamed:");
        _appearanceNameAqua = ObjC.CreateNSString("NSAppearanceNameAqua");
        _appearanceNameDarkAqua = ObjC.CreateNSString("NSAppearanceNameDarkAqua");

        ClsNSView = ObjC.GetClass("NSView");
        ClsCAMetalLayer = ObjC.GetClass("CAMetalLayer");
        ClsNSObject = ObjC.GetClass("NSObject");

        EnsureMetalLayerDelegate();

        _initialized = true;
    }

    public static void SetWindowOpacity(nint window, double opacity)
    {
        EnsureInitialized();
        if (window == 0 || SelSetAlphaValue == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_double(window, SelSetAlphaValue, opacity);
    }

    public static void SetMetalLayerDisplaySyncEnabled(nint metalLayer, bool enabled)
    {
        EnsureInitialized();
        if (metalLayer == 0 || SelSetDisplaySyncEnabled == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_bool(metalLayer, SelSetDisplaySyncEnabled, enabled);
    }

    public static void SetWindowEnabled(nint window, bool enabled)
    {
        EnsureInitialized();
        if (window == 0 || SelSetIgnoresMouseEvents == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_bool(window, SelSetIgnoresMouseEvents, !enabled);
    }

    public static void ActivateWindow(nint window)
    {
        EnsureInitialized();
        if (window == 0 || SelMakeKeyAndOrderFront == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_nint(window, SelMakeKeyAndOrderFront, 0);
    }

    public static void SetOwnerWindow(nint window, nint ownerWindow)
    {
        EnsureInitialized();
        if (window == 0 || SelSetParentWindow == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_nint(window, SelSetParentWindow, ownerWindow);
    }

    public static long GetWindowLevel(nint window)
    {
        EnsureInitialized();
        if (window == 0 || SelLevel == 0)
        {
            return 0;
        }

        return ObjC.MsgSend_long(window, SelLevel);
    }

    public static void SetWindowLevel(nint window, long level)
    {
        EnsureInitialized();
        if (window == 0 || SelSetLevel == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_int(window, SelSetLevel, unchecked((int)level));
    }

    public static ulong GetWindowStyleMask(nint window)
    {
        EnsureInitialized();
        if (window == 0 || SelStyleMask == 0)
        {
            return 0;
        }

        return ObjC.MsgSend_ulong(window, SelStyleMask);
    }

    public static void SetWindowStyleMask(nint window, ulong styleMask)
    {
        EnsureInitialized();
        if (window == 0 || SelSetStyleMask == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_ulong(window, SelSetStyleMask, styleMask);
    }

    public static void ApplyContentSizeConstraints(nint window, Window mewWindow)
    {
        EnsureInitialized();
        if (window == 0) return;

        var ws = mewWindow.WindowSize;
        double minW = ws.MinWidth;
        double minH = ws.MinHeight;
        double maxW = ws.MaxWidth;
        double maxH = ws.MaxHeight;

        if (SelSetContentMinSize != 0)
        {
            var minSize = new NSSize(
                minW > 0 ? minW : 0,
                minH > 0 ? minH : 0);
            ObjC.MsgSend_void_nint_size(window, SelSetContentMinSize, minSize);
        }

        if (SelSetContentMaxSize != 0)
        {
            var maxSize = new NSSize(
                !double.IsPositiveInfinity(maxW) ? maxW : 10000,
                !double.IsPositiveInfinity(maxH) ? maxH : 10000);
            ObjC.MsgSend_void_nint_size(window, SelSetContentMaxSize, maxSize);
        }
    }

    public static NSRect GetWindowFrame(nint window)
    {
        EnsureInitialized();
        if (window == 0 || SelFrame == 0)
        {
            return default;
        }

        return ObjC.MsgSend_rect(window, SelFrame);
    }

    public static void SetWindowPosition(nint window, double leftDip, double topDip)
    {
        EnsureInitialized();
        if (window == 0 || SelSetFrameOrigin == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_point(window, SelSetFrameOrigin, new NSPoint(leftDip, topDip));
    }

    public static NSPoint WindowConvertPointToScreen(nint window, NSPoint point)
    {
        EnsureInitialized();
        if (window == 0 || SelConvertPointToScreen == 0)
        {
            return point;
        }

        return ObjC.MsgSend_point_nint_point(window, SelConvertPointToScreen, point);
    }

    public static NSPoint WindowConvertPointFromScreen(nint window, NSPoint point)
    {
        EnsureInitialized();
        if (window == 0 || SelConvertPointFromScreen == 0)
        {
            return point;
        }

        return ObjC.MsgSend_point_nint_point(window, SelConvertPointFromScreen, point);
    }

    public static void SetTitlebarForTransparency(nint window, bool transparent)
    {
        EnsureInitialized();
        if (window == 0)
        {
            return;
        }

        // NSWindowTitleVisibilityVisible=0, Hidden=1
        if (SelSetTitleVisibility != 0)
        {
            ObjC.MsgSend_void_nint_int(window, SelSetTitleVisibility, transparent ? 1 : 0);
        }

        if (SelSetTitlebarAppearsTransparent != 0)
        {
            ObjC.MsgSend_void_nint_bool(window, SelSetTitlebarAppearsTransparent, transparent);
        }

        if (SelSetMovableByWindowBackground != 0)
        {
            // Keep window drag under explicit control (e.g., custom drag zones),
            // so transparent windows don't drag from arbitrary background clicks.
            ObjC.MsgSend_void_nint_bool(window, SelSetMovableByWindowBackground, false);
        }
    }

    public static nint GetWindowFromView(nint view)
    {
        EnsureInitialized();
        if (view == 0 || SelWindow == 0)
        {
            return 0;
        }

        return ObjC.MsgSend_nint(view, SelWindow);
    }

    public static nint GetWindowFromNotification(nint notification)
    {
        EnsureInitialized();
        if (notification == 0 || SelObject == 0)
        {
            return 0;
        }

        return ObjC.MsgSend_nint(notification, SelObject);
    }

    public static void SetWindowTransparency(nint window, nint view, bool allowsTransparency)
    {
        EnsureInitialized();
        if (window == 0)
        {
            return;
        }

        // NSWindow uses setOpaque + backgroundColor to control compositing.
        if (SelSetOpaque != 0)
        {
            ObjC.MsgSend_void_nint_bool(window, SelSetOpaque, !allowsTransparency);
        }

        if (allowsTransparency && ClsNSColor != 0 && SelClearColor != 0 && SelSetBackgroundColor != 0)
        {
            var clear = ObjC.MsgSend_nint(ClsNSColor, SelClearColor);
            if (clear != 0)
            {
                ObjC.MsgSend_void_nint_nint(window, SelSetBackgroundColor, clear);
            }
        }

        // Also ensure the content view is not treated as opaque.
        if (view != 0 && SelSetOpaque != 0)
        {
            ObjC.MsgSend_void_nint_bool(view, SelSetOpaque, !allowsTransparency);
        }
    }

    public static void SetLayerOpaque(nint layer, bool opaque)
    {
        EnsureInitialized();
        if (layer == 0 || SelSetOpaque == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_bool(layer, SelSetOpaque, opaque);
    }

    public static void SetFirstResponder(nint window, nint responder)
    {
        EnsureInitialized();
        if (window == 0 || responder == 0 || SelMakeFirstResponder == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_nint(window, SelMakeFirstResponder, responder);
    }

    private static readonly Dictionary<nint, WeakReference<MacOSWindowBackend>> _reshapeTargets = new();
    private static readonly Dictionary<nint, WeakReference<MacOSWindowBackend>> _textInputTargets = new();

    public static void RegisterReshapeTarget(nint view, MacOSWindowBackend backend)
    {
        if (view == 0)
        {
            return;
        }

        lock (_reshapeTargets)
        {
            _reshapeTargets[view] = new WeakReference<MacOSWindowBackend>(backend);
        }
    }

    public static void UnregisterReshapeTarget(nint view)
    {
        if (view == 0)
        {
            return;
        }

        lock (_reshapeTargets)
        {
            _reshapeTargets.Remove(view);
        }
    }

    public static void RegisterTextInputTarget(nint view, MacOSWindowBackend backend)
    {
        if (view == 0)
        {
            return;
        }

        lock (_textInputTargets)
        {
            _textInputTargets[view] = new WeakReference<MacOSWindowBackend>(backend);
        }
    }

    public static void UnregisterTextInputTarget(nint view)
    {
        if (view == 0)
        {
            return;
        }

        lock (_textInputTargets)
        {
            _textInputTargets.Remove(view);
        }
    }

    private static bool TryGetTextInputTarget(nint view, out MacOSWindowBackend backend)
    {
        lock (_textInputTargets)
        {
            if (_textInputTargets.TryGetValue(view, out var wr))
            {
                if (wr.TryGetTarget(out var target) && target != null)
                {
                    backend = target;
                    return true;
                }

                _textInputTargets.Remove(view);
            }
        }

        backend = null!;
        return false;
    }

    private static bool TryGetReshapeTarget(nint view, out MacOSWindowBackend backend)
    {
        lock (_reshapeTargets)
        {
            if (_reshapeTargets.TryGetValue(view, out var wr))
            {
                if (wr.TryGetTarget(out var target) && target != null)
                {
                    backend = target;
                    return true;
                }

                // Backend collected without unregistration; remove stale mapping.
                _reshapeTargets.Remove(view);
            }
        }

        backend = null!;
        return false;
    }

    public static void InterpretKeyEvent(nint view, nint ev)
    {
        EnsureInitialized();
        EnsureTextInputViewSubclass();
        EnsureOpenGLViewSubclass();

        if (view == 0 || ev == 0 || ClsNSArray == 0 || SelArrayWithObject == 0 || SelInterpretKeyEvents == 0)
        {
            return;
        }

        var array = ObjC.MsgSend_nint_nint(ClsNSArray, SelArrayWithObject, ev);
        if (array != 0)
        {
            ObjC.MsgSend_void_nint_nint(view, SelInterpretKeyEvents, array);
        }
    }

    [UnmanagedCallersOnly]
    private static void MewUITextInputView_insertText(nint self, nint _cmd, nint text, NSRange replacementRange)
    {
        try
        {
            if (!TryGetTextInputTarget(self, out var backend))
            {
                return;
            }

            MacOSWindowBackend.ImeNativeLogger.Write($"objc insertText:replacementRange: view=0x{self:x} textObj=0x{text:x} repl=({replacementRange.location},{replacementRange.length}) imeHasMarked={backend.ImeHasMarkedText}");
            var s = MacOSInterop.GetUtf8TextFromNSStringOrAttributedString(text);
            MacOSWindowBackend.ImeNativeLogger.Write($"  -> text len={(s?.Length ?? -1)} '{MacOSWindowBackend.TruncateForImeLog(s)}'");
            backend.ImeInsertText(s, replacementRange);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void MewUITextInputView_insertTextLegacy(nint self, nint _cmd, nint text)
    {
        try
        {
            if (!TryGetTextInputTarget(self, out var backend))
            {
                return;
            }

            MacOSWindowBackend.ImeNativeLogger.Write($"objc insertText: (legacy) view=0x{self:x} textObj=0x{text:x} imeHasMarked={backend.ImeHasMarkedText}");
            var s = MacOSInterop.GetUtf8TextFromNSStringOrAttributedString(text);
            MacOSWindowBackend.ImeNativeLogger.Write($"  -> text len={(s?.Length ?? -1)} '{MacOSWindowBackend.TruncateForImeLog(s)}'");
            backend.ImeInsertText(s);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void MewUITextInputView_setMarkedText(nint self, nint _cmd, nint text, NSRange selectedRange, NSRange replacementRange)
    {
        try
        {
            if (!TryGetTextInputTarget(self, out var backend))
            {
                return;
            }

            MacOSWindowBackend.ImeNativeLogger.Write($"objc setMarkedText:selectedRange:replacementRange: view=0x{self:x} textObj=0x{text:x} sel=({selectedRange.location},{selectedRange.length}) repl=({replacementRange.location},{replacementRange.length}) imeHasMarked={backend.ImeHasMarkedText}");
            var s = MacOSInterop.GetUtf8TextFromNSStringOrAttributedString(text) ?? string.Empty;
            MacOSWindowBackend.ImeNativeLogger.Write($"  -> marked len={s.Length} '{MacOSWindowBackend.TruncateForImeLog(s)}'");
            backend.ImeSetMarkedText(s, replacementRange);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void MewUITextInputView_setMarkedTextLegacy(nint self, nint _cmd, nint text, NSRange selectedRange)
    {
        try
        {
            if (!TryGetTextInputTarget(self, out var backend))
            {
                return;
            }

            MacOSWindowBackend.ImeNativeLogger.Write($"objc setMarkedText:selectedRange: (legacy) view=0x{self:x} textObj=0x{text:x} sel=({selectedRange.location},{selectedRange.length}) imeHasMarked={backend.ImeHasMarkedText}");
            var s = MacOSInterop.GetUtf8TextFromNSStringOrAttributedString(text) ?? string.Empty;
            MacOSWindowBackend.ImeNativeLogger.Write($"  -> marked len={s.Length} '{MacOSWindowBackend.TruncateForImeLog(s)}'");
            backend.ImeSetMarkedText(s);
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void MewUITextInputView_unmarkText(nint self, nint _cmd)
    {
        try
        {
            if (TryGetTextInputTarget(self, out var backend))
            {
                MacOSWindowBackend.ImeNativeLogger.Write($"objc unmarkText view=0x{self:x} imeHasMarked={backend.ImeHasMarkedText}");
                backend.ImeUnmarkText();
            }
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly]
    private static byte MewUITextInputView_hasMarkedText(nint self, nint _cmd)
    {
        try
        {
            var result = TryGetTextInputTarget(self, out var backend) && backend.ImeHasMarkedText ? (byte)1 : (byte)0;
            MacOSWindowBackend.ImeNativeLogger.Write($"objc hasMarkedText view=0x{self:x} -> {result}");
            return result;
        }
        catch
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static NSRange MewUITextInputView_markedRange(nint self, nint _cmd)
    {
        try
        {
            const ulong NSNotFound = (ulong)long.MaxValue;
            if (!TryGetTextInputTarget(self, out var backend) || !backend.ImeHasMarkedText)
            {
                MacOSWindowBackend.ImeNativeLogger.Write($"objc markedRange view=0x{self:x} -> (NSNotFound,0)");
                return new NSRange(NSNotFound, 0);
            }

            if (backend.Window.FocusManager.FocusedElement is Controls.TextBase tb)
            {
                // NSTextInputClient expects ranges in the document's coordinates.
                // TextBase maintains the composition range inside the document.
                int start = Math.Max(0, tb.CompositionStartIndex);
                int len = Math.Max(0, tb.CompositionLength);
                if (len == 0)
                {
                    // Composition started but TextBase may not have received the first update yet.
                    // Report the current marked string length so IME treats the range as active.
                    len = backend.ImeMarkedText?.Length ?? 0;
                }
                var r = new NSRange((ulong)start, (ulong)len);
                MacOSWindowBackend.ImeNativeLogger.Write($"objc markedRange view=0x{self:x} -> ({r.location},{r.length}) [TextBase]");
                return r;
            }

            // Fallback to a minimal "active marked range" when there is no focused TextBase.
            var rr = new NSRange(0, (ulong)(backend.ImeMarkedText?.Length ?? 0));
            MacOSWindowBackend.ImeNativeLogger.Write($"objc markedRange view=0x{self:x} -> ({rr.location},{rr.length}) [fallback]");
            return rr;
        }
        catch
        {
            MacOSWindowBackend.ImeNativeLogger.Write($"objc markedRange view=0x{self:x} -> (NSNotFound,0) [exception]");
            return new NSRange(ulong.MaxValue, 0);
        }
    }

    [UnmanagedCallersOnly]
    private static NSRange MewUITextInputView_selectedRange(nint self, nint _cmd)
    {
        // Without a full text-store bridge, keep this minimal but coherent for IME:
        // - When composing, report the caret at the end of the marked text.
        // - Otherwise, report an empty selection at 0.
        try
        {
            if (TryGetTextInputTarget(self, out var backend))
            {
                if (backend.Window.FocusManager.FocusedElement is Controls.TextBase tb)
                {
                    var (s, e) = tb.SelectionRange;
                    int start = Math.Min(s, e);
                    int end = Math.Max(s, e);
                    var r = new NSRange((ulong)Math.Max(0, start), (ulong)Math.Max(0, end - start));
                    MacOSWindowBackend.ImeNativeLogger.Write($"objc selectedRange view=0x{self:x} -> ({r.location},{r.length}) [TextBase]");
                    return r;
                }

                if (backend.ImeHasMarkedText)
                {
                    ulong len = (ulong)(backend.ImeMarkedText?.Length ?? 0);
                    var rr = new NSRange(len, 0);
                    MacOSWindowBackend.ImeNativeLogger.Write($"objc selectedRange view=0x{self:x} -> ({rr.location},{rr.length}) [marked]");
                    return rr;
                }
            }
        }
        catch
        {
        }

        MacOSWindowBackend.ImeNativeLogger.Write($"objc selectedRange view=0x{self:x} -> (0,0) [fallback]");
        return new NSRange(0, 0);
    }

    [UnmanagedCallersOnly]
    private static nint MewUITextInputView_validAttributesForMarkedText(nint self, nint _cmd)
    {
        try
        {
            // Return an empty NSArray rather than nil; some IMEs are stricter about this.
            EnsureInitialized();
            if (ObjC.GetClass("NSArray") is var cls && cls != 0)
            {
                var selArray = ObjC.Sel("array");
                if (selArray != 0)
                {
                    var arr = ObjC.MsgSend_nint(cls, selArray);
                    MacOSWindowBackend.ImeNativeLogger.Write($"objc validAttributesForMarkedText view=0x{self:x} -> 0x{arr:x}");
                    return arr;
                }
            }
        }
        catch
        {
        }

        MacOSWindowBackend.ImeNativeLogger.Write($"objc validAttributesForMarkedText view=0x{self:x} -> 0");
        return 0;
    }

    [UnmanagedCallersOnly]
    private static long MewUITextInputView_conversationIdentifier(nint self, nint _cmd)
    {
        // Required by NSTextInputClient. Use the view pointer as a stable per-instance identifier.
        MacOSWindowBackend.ImeNativeLogger.Write($"objc conversationIdentifier view=0x{self:x} -> 0x{self:x}");
        return self;
    }

    [UnmanagedCallersOnly]
    private static void MewUITextInputView_updateTextAttributes(nint self, nint _cmd, nint attributes)
    {
        // Optional NSTextInputClient method; ignored for now (we don't expose attribute runs).
        MacOSWindowBackend.ImeNativeLogger.Write($"objc updateTextAttributes: view=0x{self:x} attrs=0x{attributes:x}");
    }

    [UnmanagedCallersOnly]
    private static nint MewUITextInputView_attributedSubstringForProposedRange(nint self, nint _cmd, NSRange proposedRange, nint actualRange)
    {
        try
        {
            if (!TryGetTextInputTarget(self, out var backend))
            {
                return 0;
            }

            MacOSWindowBackend.ImeNativeLogger.Write($"objc attributedSubstringForProposedRange view=0x{self:x} proposed=({proposedRange.location},{proposedRange.length}) actualRangePtr=0x{actualRange:x}");
            string text;
            int textLen;
            if (backend.Window.FocusManager.FocusedElement is Controls.TextBase tb)
            {
                textLen = tb.TextLengthInternal;
                text = textLen > 0 ? tb.GetTextSubstringInternal(0, textLen) : string.Empty;
            }
            else
            {
                text = backend.ImeMarkedText ?? string.Empty;
                textLen = text.Length;
            }

            ulong totalLen = (ulong)Math.Max(0, textLen);
            ulong start = proposedRange.location > totalLen ? totalLen : proposedRange.location;
            ulong len = proposedRange.length;
            if (start + len > totalLen)
            {
                len = totalLen - start;
            }

            unsafe
            {
                if (actualRange != 0)
                {
                    *(NSRange*)actualRange = new NSRange(start, len);
                }
            }

            string slice = len == 0 ? string.Empty : text.Substring((int)start, (int)len);
            MacOSWindowBackend.ImeNativeLogger.Write($"  -> sliceStart={start} sliceLen={len} '{MacOSWindowBackend.TruncateForImeLog(slice)}'");
            nint nsString = ObjC.CreateNSString(slice);
            if (nsString == 0)
            {
                return 0;
            }

            // Return an NSAttributedString so IME can query attributes if it wants.
            nint clsAttr = ObjC.GetClass("NSAttributedString");
            nint selAlloc = ObjC.Sel("alloc");
            nint selInitWithString = ObjC.Sel("initWithString:");
            if (clsAttr == 0 || selAlloc == 0 || selInitWithString == 0)
            {
                return 0;
            }

            nint attr = ObjC.MsgSend_nint(clsAttr, selAlloc);
            attr = attr != 0 ? ObjC.MsgSend_nint_nint(attr, selInitWithString, nsString) : 0;
            MacOSWindowBackend.ImeNativeLogger.Write($"  -> returning NSAttributedString 0x{attr:x}");
            return attr;
        }
        catch
        {
            return 0;
        }
    }

    [UnmanagedCallersOnly]
    private static ulong MewUITextInputView_characterIndexForPoint(nint self, nint _cmd, NSPoint point)
    {
        MacOSWindowBackend.ImeNativeLogger.Write($"objc characterIndexForPoint view=0x{self:x} pt=({point.x},{point.y}) -> 0");
        return 0;
    }

    [UnmanagedCallersOnly]
    private static NSRect MewUITextInputView_firstRectForCharacterRange(nint self, nint _cmd, NSRange range, nint actualRange)
    {
        try
        {
            EnsureInitialized();
            if (SelWindow != 0 && SelFrame != 0)
            {
                var window = ObjC.MsgSend_nint(self, SelWindow);
                if (window != 0)
                {
                    var frame = ObjC.MsgSend_rect(window, SelFrame);
                    var r = new NSRect(frame.origin.x + 10, frame.origin.y + 10, 0, 0);
                    MacOSWindowBackend.ImeNativeLogger.Write($"objc firstRectForCharacterRange view=0x{self:x} range=({range.location},{range.length}) actualRangePtr=0x{actualRange:x} -> ({r.origin.x},{r.origin.y},{r.size.width},{r.size.height})");
                    return r;
                }
            }
        }
        catch
        {
        }

        MacOSWindowBackend.ImeNativeLogger.Write($"objc firstRectForCharacterRange view=0x{self:x} range=({range.location},{range.length}) -> default");
        return default;
    }

    [UnmanagedCallersOnly]
    private static void MewUITextInputView_doCommandBySelector(nint self, nint _cmd, nint selector)
    {
        MacOSWindowBackend.ImeNativeLogger.Write($"objc doCommandBySelector view=0x{self:x} selector=0x{selector:x}");
        try
        {
            if (TryGetTextInputTarget(self, out var backend))
            {
                backend.ImeDoCommandBySelector(selector);
            }
        }
        catch
        {
        }
    }

    [UnmanagedCallersOnly]
    private static void MewUIOpenGLView_reshape(nint self, nint _cmd)
    {
        try
        {
            // Call super -[NSOpenGLView reshape]
            var super = new ObjC.objc_super(self, ClsNSOpenGLView);
            ObjC.MsgSendSuper_void(ref super, _cmd);

            if (TryGetReshapeTarget(self, out var backend))
            {
                backend.OnNativeViewReshape();
            }
        }
        catch
        {
            // Never let an exception cross the unmanaged boundary.
        }
    }

    [UnmanagedCallersOnly]
    private static void MewUIOpenGLView_drawRect(nint self, nint _cmd, NSRect dirtyRect)
    {
        try
        {
            if (TryGetReshapeTarget(self, out var backend))
            {
                backend.RenderNow();
            }
        }
        catch
        {
            // Never let an exception cross the unmanaged boundary.
        }
    }

    private static void EnsureOpenGLViewSubclass()
    {
        if (ClsMewUIOpenGLView != 0 || ClsNSOpenGLView == 0 || SelReshape == 0 || SelDrawRect == 0)
        {
            return;
        }

        const string className = "MewUIOpenGLView";
        var cls = ObjC.GetClass(className);
        bool needsRegister = false;
        if (cls == 0)
        {
            cls = ObjC.AllocateClassPair(ClsNSOpenGLView, className);
            needsRegister = cls != 0;
        }

        if (cls != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, void>)&MewUIOpenGLView_reshape;
            _ = ObjC.AddMethod(cls, SelReshape, imp, "v@:");

            var impDraw = (nint)(delegate* unmanaged<nint, nint, NSRect, void>)&MewUIOpenGLView_drawRect;
            // drawRect: takes an NSRect (CGRect) by value.
            _ = ObjC.AddMethod(cls, SelDrawRect, impDraw, "v@:{CGRect={CGPoint=dd}{CGSize=dd}}");

            // NSTextInputClient / text services
            AddTextInputClientMethods(cls);
            _ = ObjC.AddProtocol(cls, "NSTextInputClient");

            if (needsRegister)
            {
                ObjC.RegisterClassPair(cls);
            }
        }

        ClsMewUIOpenGLView = cls;
    }

    [UnmanagedCallersOnly]
    private static byte MewUITextInputView_acceptsFirstResponder(nint self, nint _cmd)
    {
        MacOSWindowBackend.ImeNativeLogger.Write($"objc acceptsFirstResponder view=0x{self:x} -> 1");
        return 1;
    }

    private static void AddTextInputClientMethods(nint cls)
    {
        var selInsertLegacy = ObjC.Sel("insertText:");
        var selInsert = ObjC.Sel("insertText:replacementRange:");
        var selSetMarkedLegacy = ObjC.Sel("setMarkedText:selectedRange:");
        var selSetMarked = ObjC.Sel("setMarkedText:selectedRange:replacementRange:");
        var selUnmark = ObjC.Sel("unmarkText");
        var selHasMarked = ObjC.Sel("hasMarkedText");
        var selMarkedRange = ObjC.Sel("markedRange");
        var selSelectedRange = ObjC.Sel("selectedRange");
        var selValidAttrs = ObjC.Sel("validAttributesForMarkedText");
        var selConversationId = ObjC.Sel("conversationIdentifier");
        var selUpdateTextAttrs = ObjC.Sel("updateTextAttributes:");
        var selAttrSub = ObjC.Sel("attributedSubstringForProposedRange:actualRange:");
        var selCharIndex = ObjC.Sel("characterIndexForPoint:");
        var selFirstRect = ObjC.Sel("firstRectForCharacterRange:actualRange:");
        var selDoCommand = ObjC.Sel("doCommandBySelector:");
        var selAcceptsFirstResponder = ObjC.Sel("acceptsFirstResponder");

        if (selInsertLegacy != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, nint, void>)&MewUITextInputView_insertTextLegacy;
            _ = ObjC.AddMethod(cls, selInsertLegacy, imp, "v@:@");
        }

        if (selInsert != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, nint, NSRange, void>)&MewUITextInputView_insertText;
            _ = ObjC.AddMethod(cls, selInsert, imp, "v@:@{_NSRange=QQ}");
        }

        if (selSetMarkedLegacy != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, nint, NSRange, void>)&MewUITextInputView_setMarkedTextLegacy;
            _ = ObjC.AddMethod(cls, selSetMarkedLegacy, imp, "v@:@{_NSRange=QQ}");
        }

        if (selSetMarked != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, nint, NSRange, NSRange, void>)&MewUITextInputView_setMarkedText;
            _ = ObjC.AddMethod(cls, selSetMarked, imp, "v@:@{_NSRange=QQ}{_NSRange=QQ}");
        }

        if (selUnmark != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, void>)&MewUITextInputView_unmarkText;
            _ = ObjC.AddMethod(cls, selUnmark, imp, "v@:");
        }

        if (selHasMarked != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, byte>)&MewUITextInputView_hasMarkedText;
            _ = ObjC.AddMethod(cls, selHasMarked, imp, "B@:");
        }

        if (selMarkedRange != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, NSRange>)&MewUITextInputView_markedRange;
            _ = ObjC.AddMethod(cls, selMarkedRange, imp, "{_NSRange=QQ}@:");
        }

        if (selSelectedRange != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, NSRange>)&MewUITextInputView_selectedRange;
            _ = ObjC.AddMethod(cls, selSelectedRange, imp, "{_NSRange=QQ}@:");
        }

        if (selValidAttrs != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, nint>)&MewUITextInputView_validAttributesForMarkedText;
            _ = ObjC.AddMethod(cls, selValidAttrs, imp, "@@:");
        }

        if (selConversationId != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, long>)&MewUITextInputView_conversationIdentifier;
            _ = ObjC.AddMethod(cls, selConversationId, imp, "q@:");
        }

        if (selUpdateTextAttrs != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, nint, void>)&MewUITextInputView_updateTextAttributes;
            _ = ObjC.AddMethod(cls, selUpdateTextAttrs, imp, "v@:@");
        }

        if (selAttrSub != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, NSRange, nint, nint>)&MewUITextInputView_attributedSubstringForProposedRange;
            _ = ObjC.AddMethod(cls, selAttrSub, imp, "@@:{_NSRange=QQ}^{_NSRange=QQ}");
        }

        if (selCharIndex != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, NSPoint, ulong>)&MewUITextInputView_characterIndexForPoint;
            _ = ObjC.AddMethod(cls, selCharIndex, imp, "Q@:{CGPoint=dd}");
        }

        if (selFirstRect != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, NSRange, nint, NSRect>)&MewUITextInputView_firstRectForCharacterRange;
            _ = ObjC.AddMethod(cls, selFirstRect, imp, "{CGRect={CGPoint=dd}{CGSize=dd}}@:{_NSRange=QQ}^{_NSRange=QQ}");
        }

        if (selDoCommand != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, nint, void>)&MewUITextInputView_doCommandBySelector;
            _ = ObjC.AddMethod(cls, selDoCommand, imp, "v@::");
        }

        if (selAcceptsFirstResponder != 0)
        {
            var imp = (nint)(delegate* unmanaged<nint, nint, byte>)&MewUITextInputView_acceptsFirstResponder;
            _ = ObjC.AddMethod(cls, selAcceptsFirstResponder, imp, "B@:");
        }
    }

    private static void EnsureTextInputViewSubclass()
    {
        if (ClsMewUITextInputView != 0 || ClsNSView == 0)
        {
            return;
        }

        const string className = "MewUITextInputView";
        var cls = ObjC.GetClass(className);
        bool needsRegister = false;
        if (cls == 0)
        {
            cls = ObjC.AllocateClassPair(ClsNSView, className);
            needsRegister = cls != 0;
        }

        if (cls != 0)
        {
            AddTextInputClientMethods(cls);
            _ = ObjC.AddProtocol(cls, "NSTextInputClient");
            if (needsRegister)
            {
                ObjC.RegisterClassPair(cls);
            }
        }

        ClsMewUITextInputView = cls;
    }

    private static readonly Dictionary<nint, WeakReference<MacOSWindowBackend>> _metalLayerTargets = new();

    public static void RegisterMetalLayerTarget(nint metalLayer, MacOSWindowBackend backend)
    {
        if (metalLayer == 0)
        {
            return;
        }

        lock (_metalLayerTargets)
        {
            _metalLayerTargets[metalLayer] = new WeakReference<MacOSWindowBackend>(backend);
        }
    }

    public static void UnregisterMetalLayerTarget(nint metalLayer)
    {
        if (metalLayer == 0)
        {
            return;
        }

        lock (_metalLayerTargets)
        {
            _metalLayerTargets.Remove(metalLayer);
        }
    }

    private static bool TryGetMetalLayerTarget(nint metalLayer, out MacOSWindowBackend backend)
    {
        lock (_metalLayerTargets)
        {
            if (_metalLayerTargets.TryGetValue(metalLayer, out var wr))
            {
                if (wr.TryGetTarget(out var target) && target != null)
                {
                    backend = target;
                    return true;
                }

                _metalLayerTargets.Remove(metalLayer);
            }
        }

        backend = null!;
        return false;
    }

    [UnmanagedCallersOnly]
    private static void MewUIMetalLayerDelegate_displayLayer(nint self, nint _cmd, nint layer)
    {
        try
        {
            if (TryGetMetalLayerTarget(layer, out var backend))
            {
                backend.RenderFromMetalLayerDisplay(layer);
            }
        }
        catch
        {
            // Never let an exception cross the unmanaged boundary.
        }
    }

    private static void EnsureMetalLayerDelegate()
    {
        if (_sharedMetalLayerDelegate != 0)
        {
            return;
        }

        if (ClsNSObject == 0)
        {
            ClsNSObject = ObjC.GetClass("NSObject");
        }

        if (ClsNSObject == 0)
        {
            return;
        }

        const string className = "MewUIMetalLayerDelegate";
        var cls = ObjC.GetClass(className);
        if (cls == 0)
        {
            cls = ObjC.AllocateClassPair(ClsNSObject, className);
            if (cls != 0)
            {
                var sel = ObjC.Sel("displayLayer:");
                var imp = (nint)(delegate* unmanaged<nint, nint, nint, void>)&MewUIMetalLayerDelegate_displayLayer;
                _ = ObjC.AddMethod(cls, sel, imp, "v@:@");
                ObjC.RegisterClassPair(cls);
            }
        }

        ClsMewUIMetalLayerDelegate = cls;
        if (cls == 0 || SelAlloc == 0 || SelInit == 0)
        {
            return;
        }

        // Keep a single shared delegate instance alive for the lifetime of the process.
        var obj = ObjC.MsgSend_nint(cls, SelAlloc);
        obj = obj != 0 ? ObjC.MsgSend_nint(obj, SelInit) : 0;
        _sharedMetalLayerDelegate = obj;
    }

    [UnmanagedCallersOnly]
    private static void MewUIWindowDelegate_windowWillClose(nint self, nint sel, nint notification)
    {
        try
        {
            if (notification == 0)
            {
                return;
            }

            nint window = GetWindowFromNotification(notification);
            if (window == 0)
            {
                return;
            }

            if (TryGetWindowCloseTarget(window, out var backend))
            {
                backend.RaiseClosedOnce();
            }
        }
        catch
        {
            // Never let an exception cross the unmanaged boundary.
        }
    }

    private static void EnsureWindowDelegate()
    {
        if (_sharedWindowDelegate != 0)
        {
            return;
        }

        if (ClsNSObject == 0)
        {
            ClsNSObject = ObjC.GetClass("NSObject");
        }

        if (ClsNSObject == 0)
        {
            return;
        }

        const string className = "MewUIWindowDelegate";
        var cls = ObjC.GetClass(className);
        if (cls == 0)
        {
            cls = ObjC.AllocateClassPair(ClsNSObject, className);
            if (cls != 0)
            {
                var imp = (nint)(delegate* unmanaged<nint, nint, nint, void>)&MewUIWindowDelegate_windowWillClose;
                _ = ObjC.AddMethod(cls, SelWindowWillClose, imp, "v@:@");
                ObjC.RegisterClassPair(cls);
            }
        }

        ClsMewUIWindowDelegate = cls;
        if (cls == 0 || SelAlloc == 0 || SelInit == 0)
        {
            return;
        }

        var obj = ObjC.MsgSend_nint(cls, SelAlloc);
        obj = obj != 0 ? ObjC.MsgSend_nint(obj, SelInit) : 0;
        _sharedWindowDelegate = obj;
    }

    internal static void RegisterWindowCloseTarget(nint window, MacOSWindowBackend backend)
    {
        if (window == 0 || backend == null)
        {
            return;
        }

        EnsureWindowDelegate();
        if (_sharedWindowDelegate != 0 && SelSetDelegate != 0)
        {
            ObjC.MsgSend_void_nint_nint(window, SelSetDelegate, _sharedWindowDelegate);
        }

        lock (_windowCloseTargets)
        {
            _windowCloseTargets[window] = new WeakReference<MacOSWindowBackend>(backend);
        }
    }

    internal static void UnregisterWindowCloseTarget(nint window)
    {
        if (window == 0)
        {
            return;
        }

        lock (_windowCloseTargets)
        {
            _windowCloseTargets.Remove(window);
        }
    }

    private static bool TryGetWindowCloseTarget(nint window, out MacOSWindowBackend backend)
    {
        lock (_windowCloseTargets)
        {
            if (_windowCloseTargets.TryGetValue(window, out var weak))
            {
                if (weak.TryGetTarget(out var target))
                {
                    backend = target;
                    return true;
                }
            }
        }

        backend = null!;
        return false;
    }

    public static nint CreateWindow(string title, double widthDip, double heightDip, bool allowsTransparency)
    {
        EnsureInitialized();
        var win = ObjC.MsgSend_nint(ClsNSWindow, SelAlloc);
        if (win == 0)
        {
            return 0;
        }

        var rect = new NSRect(0, 0, widthDip, heightDip);
        ulong styleMask = allowsTransparency ? 0ul : DefaultStyleMask;
        win = ObjC.MsgSend_nint_rect_ulong_int_bool(win, SelInitWithContentRect, rect, styleMask, NSBackingStoreBuffered, false);
        if (win == 0)
        {
            return 0;
        }

        SetTitle(win, title);
        // MouseMoved events are not delivered unless this is enabled.
        ObjC.MsgSend_void_nint_bool(win, SelSetAcceptsMouseMovedEvents, true);
        return win;
    }

    public static void ShowWindow(nint window)
    {
        EnsureInitialized();
        ObjC.MsgSend_void_nint_nint(window, SelMakeKeyAndOrderFront, 0);
    }

    public static void CloseWindow(nint window)
    {
        EnsureInitialized();
        ObjC.MsgSend_void(window, SelClose);
    }

    public static void SetTitle(nint window, string title)
    {
        EnsureInitialized();
        nint ns = ObjC.CreateNSString(title);
        ObjC.MsgSend_void_nint_nint(window, SelSetTitle, ns);
    }

    public static void SetClientSize(nint window, double widthDip, double heightDip)
    {
        EnsureInitialized();
        ObjC.MsgSend_void_nint_size(window, SelSetContentSize, new NSSize(widthDip, heightDip));
    }

    public static void CenterWindow(nint window)
    {
        EnsureInitialized();
        ObjC.MsgSend_void(window, SelCenter);
    }

    public static void ReleaseWindow(nint window)
    {
        EnsureInitialized();
        ObjC.MsgSend_void(window, SelRelease);
    }

    public static void SetWindowAppearance(nint window, bool isDark)
    {
        EnsureInitialized();

        var name = isDark ? _appearanceNameDarkAqua : _appearanceNameAqua;
        if (name == 0 || ClsNSAppearance == 0)
        {
            return;
        }

        var appearance = ObjC.MsgSend_nint_nint(ClsNSAppearance, SelAppearanceNamed, name);
        ObjC.MsgSend_void_nint_nint(window, SelSetAppearance, appearance);
    }

    public static void ClearWindowAppearance(nint window)
    {
        EnsureInitialized();
        ObjC.MsgSend_void_nint_nint(window, SelSetAppearance, 0);
    }

    public static bool IsViewInLiveResize(nint view)
    {
        EnsureInitialized();
        return view != 0 && ObjC.MsgSend_bool(view, SelInLiveResize);
    }

    public static void UpdateOpenGLContext(nint nsOpenGLContext)
    {
        EnsureInitialized();
        if (nsOpenGLContext == 0)
        {
            return;
        }

        ObjC.MsgSend_void(nsOpenGLContext, SelUpdateOpenGLContext);
    }

    public static void SetSwapInterval(nint nsOpenGLContext, int interval)
    {
        EnsureInitialized();
        if (nsOpenGLContext == 0 || SelSetValuesForParameter == 0)
        {
            return;
        }

        // NSOpenGLContextParameterSwapInterval = 222
        const int NSOpenGLContextParameterSwapInterval = 222;
        int value = Math.Clamp(interval, 0, 1);
        ObjC.MsgSend_void_intPtr_int(nsOpenGLContext, SelSetValuesForParameter, &value, NSOpenGLContextParameterSwapInterval);
    }

    public static void SetOpenGLSurfaceOpacity(nint nsOpenGLContext, bool opaque)
    {
        EnsureInitialized();
        if (nsOpenGLContext == 0 || SelSetValuesForParameter == 0)
        {
            return;
        }

        // NSOpenGLCPSurfaceOpacity = 236
        const int NSOpenGLCPSurfaceOpacity = 236;
        int value = opaque ? 1 : 0;
        ObjC.MsgSend_void_intPtr_int(nsOpenGLContext, SelSetValuesForParameter, &value, NSOpenGLCPSurfaceOpacity);
    }

    public static void LockOpenGLContext(nint nsOpenGLContext)
    {
        EnsureInitialized();
        if (nsOpenGLContext == 0 || SelCGLContextObj == 0)
        {
            return;
        }

        var cgl = ObjC.MsgSend_nint(nsOpenGLContext, SelCGLContextObj);
        if (cgl != 0)
        {
            CGL.LockContext(cgl);
        }
    }

    public static void UnlockOpenGLContext(nint nsOpenGLContext)
    {
        EnsureInitialized();
        if (nsOpenGLContext == 0 || SelCGLContextObj == 0)
        {
            return;
        }

        var cgl = ObjC.MsgSend_nint(nsOpenGLContext, SelCGLContextObj);
        if (cgl != 0)
        {
            CGL.UnlockContext(cgl);
        }
    }

    public static void DisplayIfNeeded(nint nsView)
    {
        EnsureInitialized();
        if (nsView == 0)
        {
            return;
        }

        if (SelSetNeedsDisplay != 0)
        {
            ObjC.MsgSend_void_nint_bool(nsView, SelSetNeedsDisplay, true);
        }

        // During live-resize, AppKit can keep showing a scaled cached frame until it decides to redraw.
        // Calling display forces a synchronous draw cycle for this view.
        if (SelDisplay != 0)
        {
            ObjC.MsgSend_void(nsView, SelDisplay);
            return;
        }

        if (SelDisplayIfNeeded != 0)
        {
            ObjC.MsgSend_void(nsView, SelDisplayIfNeeded);
        }
    }

    public static void SetNeedsDisplay(nint nsView)
    {
        EnsureInitialized();
        if (nsView == 0 || SelSetNeedsDisplay == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_bool(nsView, SelSetNeedsDisplay, true);
    }

    public static void DisplayLayerIfNeeded(nint layer)
    {
        EnsureInitialized();
        if (layer == 0)
        {
            return;
        }

        // CALayer/CAMetalLayer does not implement setNeedsDisplay:(BOOL). It implements setNeedsDisplay (no args)
        // and display/displayIfNeeded (no args).
        if (SelSetNeedsDisplayNoArgs != 0)
        {
            ObjC.MsgSend_void(layer, SelSetNeedsDisplayNoArgs);
        }

        if (SelDisplay != 0)
        {
            ObjC.MsgSend_void(layer, SelDisplay);
            return;
        }

        if (SelDisplayIfNeeded != 0)
        {
            ObjC.MsgSend_void(layer, SelDisplayIfNeeded);
        }
    }

    public static void SetLayerNeedsDisplay(nint layer)
    {
        EnsureInitialized();
        if (layer == 0 || SelSetNeedsDisplayNoArgs == 0)
        {
            return;
        }

        ObjC.MsgSend_void(layer, SelSetNeedsDisplayNoArgs);
    }

    // Structs used in objc_msgSend signatures must be blittable and have the exact native layout.
    // Keep them internal so ObjC helper can reference them.

    public static (nint View, nint Context) AttachLegacyOpenGLView(nint window, double widthDip, double heightDip)
    {
        EnsureInitialized();
        // Must create the view as our subclass so it can:
        // - forward reshape/draw callbacks
        // - participate in NSTextInputClient (insertText/setMarkedText/...) for IME and plain text input
        //
        // If we create a plain NSOpenGLView first and only later register the subclass, existing instances
        // won't gain the required Objective-C methods, and text input will silently stop working.
        EnsureOpenGLViewSubclass();
        // Minimal legacy pixel format (OpenGL 2.1 on macOS when using legacy profile).
        // https://developer.apple.com/library/archive/documentation/GraphicsImaging/Conceptual/OpenGL-MacProgGuide/opengl_chap3/opengl_chap3.html
        const int NSOpenGLPFAOpenGLProfile = 99;
        const int NSOpenGLProfileVersionLegacy = 0x1000;
        const int NSOpenGLPFAColorSize = 8;
        const int NSOpenGLPFAAlphaSize = 11;
        const int NSOpenGLPFADepthSize = 12;
        const int NSOpenGLPFAStencilSize = 13;
        const int NSOpenGLPFADoubleBuffer = 5;
        const int NSOpenGLPFAMultisample = 59;
        const int NSOpenGLPFASampleBuffers = 55;
        const int NSOpenGLPFASamples = 56;
        int stencilBits = Math.Max(0, GraphicsRuntimeOptions.PreferredMewVGStencilBits);

        int* msaaAttrs = stackalloc int[]
        {
            NSOpenGLPFAOpenGLProfile, NSOpenGLProfileVersionLegacy,
            NSOpenGLPFAColorSize, 24,
            NSOpenGLPFAAlphaSize, 8,
            NSOpenGLPFADepthSize, 24,
            NSOpenGLPFAStencilSize, stencilBits,
            NSOpenGLPFADoubleBuffer,
            // Prefer MSAA to reduce jaggies on filled primitives (Ellipse/RoundRect/etc).
            NSOpenGLPFAMultisample,
            NSOpenGLPFASampleBuffers, 1,
            NSOpenGLPFASamples, 4,
            0
        };

        int* fallbackAttrs = stackalloc int[]
        {
            NSOpenGLPFAOpenGLProfile, NSOpenGLProfileVersionLegacy,
            NSOpenGLPFAColorSize, 24,
            NSOpenGLPFAAlphaSize, 8,
            NSOpenGLPFADepthSize, 24,
            NSOpenGLPFAStencilSize, stencilBits,
            NSOpenGLPFADoubleBuffer,
            0
        };

        nint pf = ObjC.MsgSend_nint(ClsNSOpenGLPixelFormat, SelAlloc);
        pf = ObjC.MsgSend_nint_intPtr(pf, SelInitWithAttributes, msaaAttrs);
        if (pf == 0)
        {
            // Some systems/drivers may not support the requested sample count/profile combo.
            // Fall back to a non-MSAA pixel format.
            pf = ObjC.MsgSend_nint(ClsNSOpenGLPixelFormat, SelAlloc);
            pf = ObjC.MsgSend_nint_intPtr(pf, SelInitWithAttributes, fallbackAttrs);
        }
        if (pf == 0)
        {
            return (0, 0);
        }

        var viewClass = ClsMewUIOpenGLView != 0 ? ClsMewUIOpenGLView : ClsNSOpenGLView;
        var view = ObjC.MsgSend_nint(viewClass, SelAlloc);
        var rect = new NSRect(0, 0, widthDip, heightDip);
        view = ObjC.MsgSend_nint_rect_nint(view, SelInitWithFramePixelFormat, rect, pf);
        if (view == 0)
        {
            return (0, 0);
        }

        // Prefer retina backing if available.
        ObjC.MsgSend_void_nint_bool(view, SelSetWantsBestResolution, true);
        ObjC.MsgSend_void_nint_bool(view, SelSetOpaque, true);

        // Force redraw during live resize to avoid AppKit scaling the last rendered frame (the "stretched bitmap" effect).
        // This is the OpenGL analogue of the CAMetalLayer-based recipe: keep the view's layer-backed redraw policy
        // in sync with bounds changes and request draws during resize.
        if (SelSetWantsLayer != 0)
        {
            ObjC.MsgSend_void_nint_bool(view, SelSetWantsLayer, true);
        }

        // NSViewLayerContentsRedrawPolicy.DuringViewResize = 2
        const int NSViewLayerContentsRedrawDuringViewResize = 2;
        if (SelSetLayerContentsRedrawPolicy != 0)
        {
            ObjC.MsgSend_void_nint_int(view, SelSetLayerContentsRedrawPolicy, NSViewLayerContentsRedrawDuringViewResize);
        }

        if (SelLayer != 0 && SelSetNeedsDisplayOnBoundsChange != 0)
        {
            var layer = ObjC.MsgSend_nint(view, SelLayer);
            if (layer != 0)
            {
                ObjC.MsgSend_void_nint_bool(layer, SelSetNeedsDisplayOnBoundsChange, true);
            }
        }

        // Note: AppKit's live-resize content preservation flag is on NSWindow
        // (setPreservesContentDuringLiveResize:), not NSView.
        ObjC.MsgSend_void_nint_ulong(view, SelSetAutoresizingMask, NSViewWidthSizable | NSViewHeightSizable);

        ObjC.MsgSend_void_nint_nint(window, SelSetContentView, view);

        // Extract the context.
        var ctx = ObjC.MsgSend_nint(view, SelOpenGLContext);
        return (view, ctx);
    }

    public static (nint View, nint Layer) AttachMetalLayerView(nint window, double widthDip, double heightDip)
    {
        EnsureInitialized();
        EnsureTextInputViewSubclass();

        if (ClsNSView == 0 || ClsCAMetalLayer == 0)
        {
            return (0, 0);
        }

        var viewClass = ClsMewUITextInputView != 0 ? ClsMewUITextInputView : ClsNSView;
        var view = ObjC.MsgSend_nint(viewClass, SelAlloc);
        var rect = new NSRect(0, 0, widthDip, heightDip);
        view = view != 0 ? ObjC.MsgSend_nint_rect(view, SelInitWithFrame, rect) : 0;
        if (view == 0)
        {
            return (0, 0);
        }

        // View settings (recipe).
        if (SelSetWantsLayer != 0)
        {
            ObjC.MsgSend_void_nint_bool(view, SelSetWantsLayer, true);
        }

        // NSViewLayerContentsRedrawPolicy.DuringViewResize = 2
        const int NSViewLayerContentsRedrawDuringViewResize = 2;
        if (SelSetLayerContentsRedrawPolicy != 0)
        {
            ObjC.MsgSend_void_nint_int(view, SelSetLayerContentsRedrawPolicy, NSViewLayerContentsRedrawDuringViewResize);
        }

        ObjC.MsgSend_void_nint_ulong(view, SelSetAutoresizingMask, NSViewWidthSizable | NSViewHeightSizable);

        // Create CAMetalLayer.
        var layer = ObjC.MsgSend_nint(ClsCAMetalLayer, SelAlloc);
        layer = layer != 0 ? ObjC.MsgSend_nint(layer, SelInit) : 0;
        if (layer == 0)
        {
            return (view, 0);
        }

        // Layer settings (recipe).
        if (SelSetNeedsDisplayOnBoundsChange != 0)
        {
            ObjC.MsgSend_void_nint_bool(layer, SelSetNeedsDisplayOnBoundsChange, true);
        }

        // CALayerAutoresizingMask: kCALayerWidthSizable=2, kCALayerHeightSizable=16
        const ulong CALayerWidthSizable = 1ul << 1;
        const ulong CALayerHeightSizable = 1ul << 4;
        if (SelSetAutoresizingMask != 0)
        {
            ObjC.MsgSend_void_nint_ulong(layer, SelSetAutoresizingMask, CALayerWidthSizable | CALayerHeightSizable);
        }

        if (SelSetPresentsWithTransaction != 0)
        {
            ObjC.MsgSend_void_nint_bool(layer, SelSetPresentsWithTransaction, true);
        }

        if (SelSetAllowsNextDrawableTimeout != 0)
        {
            ObjC.MsgSend_void_nint_bool(layer, SelSetAllowsNextDrawableTimeout, false);
        }

        // Use shared displayLayer: delegate so rendering happens aligned with AppKit transactions.
        if (_sharedMetalLayerDelegate != 0 && SelSetDelegate != 0)
        {
            ObjC.MsgSend_void_nint_nint(layer, SelSetDelegate, _sharedMetalLayerDelegate);
        }

        if (SelSetLayer != 0)
        {
            ObjC.MsgSend_void_nint_nint(view, SelSetLayer, layer);
        }

        ObjC.MsgSend_void_nint_nint(window, SelSetContentView, view);
        return (view, layer);
    }

    public static void UpdateMetalLayerDrawableSize(nint metalLayer, double widthDip, double heightDip, double dpiScale)
    {
        EnsureInitialized();

        if (metalLayer == 0)
        {
            return;
        }

        if (dpiScale <= 0)
        {
            dpiScale = 1.0;
        }

        double widthPx = Math.Max(1.0, widthDip * dpiScale);
        double heightPx = Math.Max(1.0, heightDip * dpiScale);

        if (SelSetDrawableSize != 0)
        {
            ObjC.MsgSend_void_nint_size(metalLayer, SelSetDrawableSize, new NSSize(widthPx, heightPx));
        }

        if (SelSetContentsScale != 0)
        {
            ObjC.MsgSend_void_nint_double(metalLayer, SelSetContentsScale, dpiScale);
        }
    }
}
