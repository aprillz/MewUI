using System.Diagnostics;

namespace Aprillz.MewUI.Platform.MacOS;

public sealed class MacOSPlatformHost : IPlatformHost
{
    private readonly Dictionary<nint, MacOSWindowBackend> _windows = new();
    private readonly List<MacOSWindowBackend> _renderBackends = new();
    private MacOSDispatcher? _dispatcher;
    private Application? _app;
    private bool _running;
    private ThemeVariant _lastSystemTheme = ThemeVariant.Light;
    private nint _lastInputWindow;
    private long _lastFrameTicks;
    private int _themeUpdateRequested;
    private RenderLoopMode _lastRenderMode;

    public IMessageBoxService MessageBox { get; } = new MacOSMessageBoxService();

    public IFileDialogService FileDialog { get; } = new MacOSFileDialogService();

    public IClipboardService Clipboard { get; } = new MacOSClipboardService();

    public IWindowBackend CreateWindowBackend(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return new MacOSWindowBackend(this, window);
    }

    public IDispatcher CreateDispatcher(nint windowHandle)
        => _dispatcher ??= new MacOSDispatcher();

    public uint GetSystemDpi()
    {
        // macOS reports a backing scale factor (1.0, 2.0, ...) rather than a DPI number.
        // MewUI uses 96 DPI as the DIP baseline, so scale * 96 is treated as effective DPI.
        var scale = MacOSInterop.GetMainScreenScaleFactor();
        return (uint)Math.Max(1, (int)Math.Round(96.0 * scale));
    }

    public ThemeVariant GetSystemThemeVariant()
    {
        // Most reliable, low-level signal without relying on AppKit notifications:
        // NSUserDefaults "AppleInterfaceStyle" is set to "Dark" when dark mode is enabled.
        // It is absent (null) for light mode.
        var style = MacOSInterop.GetUserDefaultString("AppleInterfaceStyle");
        return style != null && style.StartsWith("Dark", StringComparison.OrdinalIgnoreCase)
            ? ThemeVariant.Dark
            : ThemeVariant.Light;
    }

    public uint GetDpiForWindow(nint hwnd)
    {
        // hwnd is the NSView pointer (MacOSWindowBackend.Handle).
        var scale = MacOSInterop.GetBackingScaleFactorForView(hwnd);
        return (uint)Math.Max(1, (int)Math.Round(96.0 * scale));
    }

    public bool EnablePerMonitorDpiAwareness() => false;

    public int GetSystemMetricsForDpi(int nIndex, uint dpi) => 0;

    internal void RegisterWindow(nint handle, MacOSWindowBackend backend)
        => _windows[handle] = backend;

    internal void UnregisterWindow(nint handle)
    {
        _windows.Remove(handle);
        if (_windows.Count == 0)
        {
            _running = false;
            // Stop theme notifications as early as possible to avoid callbacks during teardown.
            MacOSInterop.TrySetThemeChangedCallback(null);
        }
    }

    internal void RequestRender()
    {
        MacOSInterop.PostWakeEvent();
    }

    private bool AnyWindowNeedsRender()
    {
        foreach (var backend in _windows.Values)
        {
            if (backend.NeedsRender)
            {
                return true;
            }
        }

        return false;
    }

    private void RenderInvalidatedWindows()
    {
        if (_windows.Count == 0)
        {
            return;
        }

        _renderBackends.Clear();
        foreach (var backend in _windows.Values)
        {
            if (backend.NeedsRender)
            {
                _renderBackends.Add(backend);
            }
        }

        for (int i = 0; i < _renderBackends.Count; i++)
        {
            _renderBackends[i].RenderIfNeeded();
        }
    }

    private void RenderAllWindows()
    {
        if (_windows.Count == 0)
        {
            return;
        }

        _renderBackends.Clear();
        foreach (var backend in _windows.Values)
        {
            _renderBackends.Add(backend);
        }

        for (int i = 0; i < _renderBackends.Count; i++)
        {
            _renderBackends[i].RenderNow();
        }
    }

    public void Run(Application app, Window mainWindow)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(mainWindow);

        _app = app;
        _dispatcher = new MacOSDispatcher();

        // Ensure dispatcher wake can break the event wait.
        _dispatcher.SetWake(static () =>
        {
            // Post an empty event into the Cocoa queue.
            MacOSInterop.PostWakeEvent();
        });

        MacOSInterop.EnsureApplicationInitialized();
        _lastSystemTheme = GetSystemThemeVariant();
        if (app.ThemeMode == ThemeVariant.System)
        {
            MacOSInterop.TrySetThemeChangedCallback(OnSystemThemeChanged);
        }
        else
        {
            MacOSInterop.TrySetThemeChangedCallback(null);
        }

        _running = true;
        app.Dispatcher = _dispatcher;
        _lastFrameTicks = Stopwatch.GetTimestamp();
        _lastRenderMode = app.RenderLoopSettings.Mode;

        // Note: Window backend will create NSWindow on Show().
        mainWindow.Show();

        // Basic manual event loop (NSApplication without calling [NSApp run]).
        while (_running)
        {
            DrainEvents();

            // Process posted dispatcher work.
            _dispatcher.ProcessWorkItems();

            CleanupClosedWindows();

            // Update system theme only when notified (no polling).
            if (Interlocked.Exchange(ref _themeUpdateRequested, 0) != 0)
            {
                TryUpdateSystemTheme();
            }

            // Render requested windows.
            var scheduler = app.RenderLoopSettings;
            if (scheduler.Mode != _lastRenderMode && scheduler.Mode == RenderLoopMode.OnRequest)
            {
                // Ensure one final render when switching from Continuous to OnRequest.
                foreach (var backend in _windows.Values)
                {
                    backend.Invalidate(erase: true);
                }
                RequestRender();
                RenderAllWindows();
            }
            if (scheduler.Mode == RenderLoopMode.Continuous)
            {
                RenderAllWindows();

                // In MaxFPS mode, rendering can easily dominate the thread (GPU-bound). Poll once and drain again
                // so input arriving during RenderAllWindows() is handled before the next frame starts.
                if (scheduler.TargetFps <= 0)
                {
                    // Keep AppKit view/window geometry up-to-date during continuous rendering (including live resize).
                    // If we skip updateWindows here, AppKit may keep showing a scaled backbuffer until mouse-up.
                    MacOSInterop.WaitForNextEvent(0, updateWindows: true);
                    DrainEvents();
                    _dispatcher.ProcessWorkItems();
                }
            }
            else
            {
                RenderInvalidatedWindows();
                if (AnyWindowNeedsRender())
                {
                    RequestRender();
                }
            }
            _lastRenderMode = scheduler.Mode;

            if (!_running)
            {
                break;
            }

            if (scheduler.Mode == RenderLoopMode.Continuous && scheduler.TargetFps <= 0)
            {
                // Max FPS: never block. We already polled & drained after rendering.
                Thread.Yield();
                _dispatcher.ClearWakeRequest();
                continue;
            }

            // Block waiting for either OS input or dispatcher work.
            // Use an event-driven wait to keep CPU usage low when idle.
            // timeoutMs meanings (see MacOSInterop.WaitForNextEvent):
            //   0  => non-blocking poll
            //  >0  => timed wait
            //  <0  => wait indefinitely
            int timeoutMs;
            if (_dispatcher.HasPendingWork)
            {
                timeoutMs = 0;
            }
            else
            {
                if (scheduler.Mode == RenderLoopMode.Continuous)
                {
                    int fps = scheduler.TargetFps;
                    if (fps > 0)
                    {
                        long ticksPerSecond = Stopwatch.Frequency;
                        long frameTicks = ticksPerSecond / fps;
                        long now = Stopwatch.GetTimestamp();
                        long elapsed = now - _lastFrameTicks;

                        int frameWaitMs = 0;
                        if (elapsed < frameTicks)
                        {
                            frameWaitMs = (int)((frameTicks - elapsed) * 1000 / ticksPerSecond);
                        }

                        // Also consider timers; whichever comes first should wake us.
                        int timerWaitMs = _dispatcher.GetPollTimeoutMs(maxMs: frameWaitMs <= 0 ? 1000 : frameWaitMs);
                        timeoutMs = frameWaitMs <= 0 ? timerWaitMs : (timerWaitMs < 0 ? frameWaitMs : Math.Min(frameWaitMs, timerWaitMs));

                        _lastFrameTicks = Stopwatch.GetTimestamp();
                    }
                    else
                    {
                        // Max FPS: keep pumping without blocking (still yields to OS by polling).
                        timeoutMs = 0;
                    }
                }
                else
                {
                    timeoutMs = _dispatcher.GetPollTimeoutMs(maxMs: 1000);
                }

                // No polling for System theme; handled via notifications only.
            }
            // Avoid calling updateWindows while blocking; it can generate continuous AppKit activity.
            bool updateWindows = timeoutMs == 0;
            if (timeoutMs == 0)
            {
                MacOSInterop.WaitForNextEvent(timeoutMs, updateWindows: updateWindows);
            }
            else
            {
                using var pool = new MacOSInterop.AutoReleasePool();
                if (MacOSInterop.WaitForNextEventDequeue(timeoutMs, updateWindows: false, out var ev))
                {
                    ProcessSingleEvent(ev);
                }

                // Drain any remaining queued events immediately to avoid input latency.
                DrainEvents();
                _dispatcher.ProcessWorkItems();
            }
            _dispatcher.ClearWakeRequest();
        }

        app.Dispatcher = null;
        _app = null;
        MacOSInterop.TrySetThemeChangedCallback(null);
    }

    private void CleanupClosedWindows()
    {
        if (_windows.Count == 0)
        {
            return;
        }

        List<nint>? closed = null;
        foreach (var kvp in _windows)
        {
            if (!MacOSInterop.IsWindowVisible(kvp.Key))
            {
                (closed ??= new List<nint>()).Add(kvp.Key);
            }
        }

        if (closed == null)
        {
            return;
        }

        for (int i = 0; i < closed.Count; i++)
        {
            if (_windows.TryGetValue(closed[i], out var backend))
            {
                backend.Dispose();
            }
        }

        // If all windows were closed, stop the run loop.
        if (_windows.Count == 0)
        {
            _running = false;
        }
    }

    private void TryUpdateSystemTheme()
    {
        var app = _app;
        if (app == null)
        {
            return;
        }

        if (app.ThemeMode != ThemeVariant.System)
        {
            return;
        }

        var current = GetSystemThemeVariant();
        if (current == _lastSystemTheme)
        {
            return;
        }

        _lastSystemTheme = current;
        app.NotifySystemThemeChanged();
    }

    public void Quit(Application app)
    {
        _running = false;
        MacOSInterop.TrySetThemeChangedCallback(null);
        MacOSInterop.RequestTerminate();
    }

    public void DoEvents()
    {
        using var pool = new MacOSInterop.AutoReleasePool();
        while (MacOSInterop.TryDequeueEvent(out var ev))
        {
            if (ev == 0)
            {
                continue;
            }

            int type = MacOSInterop.GetEventType(ev);
            var nsWindow = MacOSInterop.GetEventWindow(ev);

            var windowKey = nsWindow;
            if (windowKey == 0)
            {
                var keyWindow = MacOSInterop.GetKeyWindow();
                if (keyWindow != 0)
                {
                    windowKey = keyWindow;
                }
                else if (_lastInputWindow != 0)
                {
                    windowKey = _lastInputWindow;
                }
                else if (_windows.Count == 1)
                {
                    windowKey = _windows.Keys.FirstOrDefault();
                }
            }

            _windows.TryGetValue(windowKey, out var backend);
            var topModalBackend = GetTopModalBackend();
            if (topModalBackend != null && IsMouseEvent(type) && backend != topModalBackend)
            {
                topModalBackend.Activate();
                continue;
            }

            bool forwardToCocoa = type != 10 && type != 11;
            if (forwardToCocoa && backend != null && !backend.IsEnabled && IsMouseEvent(type))
            {
                forwardToCocoa = false;
            }

            if (forwardToCocoa)
            {
                MacOSInterop.SendEvent(ev);
            }

            if (backend != null)
            {
                backend.ProcessNSEvent(ev);
            }
            else if (_windows.Count == 1)
            {
                _windows.Values.FirstOrDefault()?.ProcessNSEvent(ev);
            }
        }
    }

    public void Dispose()
    {
        _running = false;
        _windows.Clear();
        _dispatcher = null;
        _app = null;
        MacOSInterop.TrySetThemeChangedCallback(null);
    }

    private void DrainEvents()
    {
        // Drain pending events without blocking.
        //
        // IMPORTANT: Do not drain unbounded. During window live-resize and other tracking scenarios,
        // events can arrive continuously; draining forever would starve rendering and dispatcher work,
        // making FPS counters "freeze" until mouse-up.
        const int MaxEventsPerTick = 256;
        using var pool = new MacOSInterop.AutoReleasePool();
        int processed = 0;
        while (processed < MaxEventsPerTick && MacOSInterop.TryDequeueEvent(out var ev))
        {
            if (ev == 0)
            {
                continue;
            }

            processed++;

            int type = MacOSInterop.GetEventType(ev);
            var nsWindow = MacOSInterop.GetEventWindow(ev);

            // Avoid AppKit "beep" on key events when there's no responder chain handling them.
            // MewUI handles keyboard input itself, so forwarding key events to Cocoa is unnecessary here.
            // NSEventTypeKeyDown = 10, NSEventTypeKeyUp = 11
            // Route input to the corresponding window backend (if any).
            //
            // NOTE: In some AppKit configurations (notably when driving rendering via CALayer display callbacks),
            // certain event instances can report a null window even though the app has a visible key window.
            // Fall back to keyWindow / lastInputWindow to keep input working.
            var windowKey = nsWindow;
            if (windowKey == 0)
            {
                var keyWindow = MacOSInterop.GetKeyWindow();
                if (keyWindow != 0)
                {
                    windowKey = keyWindow;
                }
                else if (_lastInputWindow != 0)
                {
                    windowKey = _lastInputWindow;
                }
                else if (_windows.Count == 1)
                {
                    windowKey = _windows.Keys.FirstOrDefault();
                }
            }

            _windows.TryGetValue(windowKey, out var backend);
            var topModalBackend = GetTopModalBackend();
            if (topModalBackend != null && IsMouseEvent(type) && backend != topModalBackend)
            {
                topModalBackend.Activate();
                continue;
            }

            bool forwardToCocoa = type != 10 && type != 11;
            if (forwardToCocoa && backend != null && !backend.IsEnabled && IsMouseEvent(type))
            {
                forwardToCocoa = false;
            }

            // For live-resize, AppKit updates view/window geometry while processing the event.
            // Forward first, then compute bounds/route to MewUI (otherwise content can look "stretched"
            // until mouse-up because we observe 1-frame-late sizes).
            if (forwardToCocoa)
            {
                MacOSInterop.SendEvent(ev);
            }

            if (backend != null)
            {
                _lastInputWindow = windowKey;
                backend.ProcessNSEvent(ev);
            }
            else if ((type == 10 || type == 11) && _windows.Count > 0)
            {
                // Some AppKit configurations can yield key events with a null window while we're running
                // a manual event loop. Route them to the last known input window (or the single window).
                var target = _lastInputWindow != 0 && _windows.TryGetValue(_lastInputWindow, out var lastBackend)
                    ? lastBackend
                    : _windows.Values.FirstOrDefault();

                target?.ProcessNSEvent(ev);
            }
            else if (_windows.Count == 1)
            {
                // Last-resort: if AppKit reports no window (or a window we didn't register) but there's only one
                // top-level window, route the event there so basic input continues to work.
                _windows.Values.FirstOrDefault()?.ProcessNSEvent(ev);
            }
        }
    }

    private void OnSystemThemeChanged()
    {
        if (!_running || _app == null)
        {
            return;
        }

        Interlocked.Exchange(ref _themeUpdateRequested, 1);
        MacOSInterop.PostWakeEvent();
    }

    private void ProcessSingleEvent(nint ev)
    {
        if (ev == 0)
        {
            return;
        }

        int type = MacOSInterop.GetEventType(ev);
        var nsWindow = MacOSInterop.GetEventWindow(ev);

        // Avoid AppKit "beep" on key events when there's no responder chain handling them.
        // MewUI handles keyboard input itself, so forwarding key events to Cocoa is unnecessary here.
        var windowKey = nsWindow;
        if (windowKey == 0)
        {
            var keyWindow = MacOSInterop.GetKeyWindow();
            if (keyWindow != 0)
            {
                windowKey = keyWindow;
            }
            else if (_lastInputWindow != 0)
            {
                windowKey = _lastInputWindow;
            }
            else if (_windows.Count == 1)
            {
                windowKey = _windows.Keys.FirstOrDefault();
            }
        }

        _windows.TryGetValue(windowKey, out var backend);
        var topModalBackend = GetTopModalBackend();
        if (topModalBackend != null && IsMouseEvent(type) && backend != topModalBackend)
        {
            topModalBackend.Activate();
            return;
        }

        bool forwardToCocoa = type != 10 && type != 11;
        if (forwardToCocoa && backend != null && !backend.IsEnabled && IsMouseEvent(type))
        {
            forwardToCocoa = false;
        }

        if (forwardToCocoa)
        {
            MacOSInterop.SendEvent(ev);
        }

        if (backend != null)
        {
            _lastInputWindow = windowKey;
            backend.ProcessNSEvent(ev);
        }
        else if ((type == 10 || type == 11) && _windows.Count > 0)
        {
            var target = _lastInputWindow != 0 && _windows.TryGetValue(_lastInputWindow, out var lastBackend)
                ? lastBackend
                : _windows.Values.FirstOrDefault();

            target?.ProcessNSEvent(ev);
        }
        else if (_windows.Count == 1)
        {
            _windows.Values.FirstOrDefault()?.ProcessNSEvent(ev);
        }
    }

    private static bool IsMouseEvent(int type)
    {
        return type is 1 or 2 or 3 or 4 or 5 or 6 or 7 or 8 or 9 or 22 or 25 or 26 or 27;
    }

    private MacOSWindowBackend? GetTopModalBackend()
    {
        Window? topModal = null;
        foreach (var backend in _windows.Values)
        {
            var candidate = backend.Window.GetTopModalChild();
            if (candidate != null)
            {
                topModal = candidate;
                break;
            }
        }

        if (topModal == null)
        {
            return null;
        }

        foreach (var backend in _windows.Values)
        {
            if (ReferenceEquals(backend.Window, topModal))
            {
                return backend;
            }
        }

        return null;
    }
}
