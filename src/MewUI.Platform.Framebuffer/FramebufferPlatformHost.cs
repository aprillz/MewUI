using System.Diagnostics;

using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Diagnostics;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Platform.Linux.Framebuffer;

/// <summary>
/// Platform host for a Linux screen without a display server: one window drawn to a DRM/KMS output or an
/// fbdev device, input from libinput, and a poll loop over the input, presentation and wake descriptors.
/// </summary>
internal sealed class FramebufferPlatformHost : IPlatformHost, IFramebufferInputSink
{
    public const string PlatformIdentifier = "Framebuffer";

    private static readonly EnvDebugLogger Logger = new("MEWUI_FB_DEBUG", "[FB]");

    // Upper bound on a poll wait, so a lost wake still lets the loop look around.
    private const int MAX_WAIT_MS = 500;

    // libinput does not repeat keys; the host does, at a typical desktop delay and rate.
    private static readonly TimeSpan KEY_REPEAT_DELAY = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan KEY_REPEAT_INTERVAL = TimeSpan.FromMilliseconds(33);

    // evdev pointer buttons (linux/input-event-codes.h).
    private const uint BTN_LEFT = 0x110;
    private const uint BTN_RIGHT = 0x111;
    private const uint BTN_MIDDLE = 0x112;
    private const uint BTN_SIDE = 0x113;
    private const uint BTN_EXTRA = 0x114;

    private readonly FramebufferPlatformOptions _options;
    private readonly FramebufferClipboardService _clipboard = new();
    private IFramebufferOutput? _output;
    private FramebufferInput? _input;
    private FramebufferKeyboard? _keyboard;
    private FramebufferWindowBackend? _window;
    private LinuxDispatcher? _dispatcher;
    private Application? _app;
    private bool _running;
    private int _wakeReadFd = -1;
    private int _wakeWriteFd = -1;
    private double _pointerX;
    private double _pointerY;
    private CursorType _cursorType = CursorType.Arrow;

    // The pointer stays hidden until the mouse first moves, so a touch-only screen never shows it, and a touch
    // hides it again until the mouse next moves.
    private bool _cursorHidden = true;
    private bool _cursorMoved;
    private uint _repeatingKey;
    private IDisposable? _keyRepeat;
    private long _nextFrameTimestamp;
    private long _heartbeatTimestamp;
    private int _loopPasses;
    private int _lastPollResult;
    private int _lastPollTimeout;

    public FramebufferPlatformHost(FramebufferPlatformOptions options)
    {
        _options = options;
    }

    public IMessageBoxService MessageBox { get; } = new FramebufferMessageBoxService();

    public IFileDialogService FileDialog { get; } = new FramebufferFileDialogService();

    public IClipboardService Clipboard => _clipboard;

    public IMountedVolumeProvider MountedVolumeProvider { get; } = new X11.UnixMountedVolumeProvider();

    public IPlacesProvider PlacesProvider { get; } = new X11.LinuxPlacesProvider();

    public string DefaultFontFamily => LinuxFontDefaults.SystemFontFamily;

    public IReadOnlyList<string> DefaultFontFallbacks { get; } = LinuxFontDefaults.BuildFallbacks();

    private double DpiScale => _options.DpiScale > 0 ? _options.DpiScale : 1.0;

    public IWindowBackend CreateWindowBackend(Window window)
    {
        if (_window != null)
        {
            throw new NotSupportedException("The framebuffer platform supports one top-level Window.");
        }

        _window = new FramebufferWindowBackend(this, window, EnsureOutput());
        return _window;
    }

    public IDispatcher CreateDispatcher(nint windowHandle) => new LinuxDispatcher();

    public uint GetSystemDpi() => (uint)Math.Max(1, Math.Round(96 * DpiScale));

    public ThemeVariant GetSystemThemeVariant() => _options.SystemTheme;

    public uint GetDpiForWindow(nint windowHandle) => GetSystemDpi();

    public bool EnablePerMonitorDpiAwareness() => false;

    public int GetSystemMetricsForDpi(int nIndex, uint dpi) => 0;

    public Point GetCursorScreenPosition() => new(_pointerX, _pointerY);

    public Rect GetWorkAreaForPoint(Point screenPositionPx)
        => _output == null ? default : new Rect(0, 0, _output.PixelWidth, _output.PixelHeight);

    public void Run(Application app, Window? mainWindow)
    {
        _running = true;
        _app = app;

        var previousContext = SynchronizationContext.Current;
        try
        {
            EnsureOutput();
            OpenInput();
            SetupWakePipe();

            _dispatcher = (LinuxDispatcher)CreateDispatcher(0);
            app.Dispatcher = _dispatcher;
            SynchronizationContext.SetSynchronizationContext(_dispatcher);
            _dispatcher.SetWake(SignalWake);

            app.OnHostLoopStarting(mainWindow);
            PumpLoop(null);
        }
        finally
        {
            StopKeyRepeat();
            if (_window != null)
            {
                try { _window.Dispose(); } catch (Exception ex) { Application.RouteLifecycleException(ex); }
                _window = null;
            }

            _input?.Dispose();
            _input = null;
            _keyboard?.Dispose();
            _keyboard = null;
            CloseWakePipe();
            _dispatcher = null;
            _app = null;
            app.Dispatcher = null;
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    public void RunNestedLoop(Func<bool> keepRunning)
    {
        ArgumentNullException.ThrowIfNull(keepRunning);
        if (_running)
        {
            PumpLoop(keepRunning);
        }
    }

    public void Quit(Application app)
    {
        var dispatcher = _dispatcher;
        if (dispatcher != null && !dispatcher.IsOnUIThread)
        {
            dispatcher.BeginInvoke(() => Quit(app));
            return;
        }

        _running = false;
        SignalWake();
    }

    [Obsolete("DoEvents will be removed. Await asynchronous work, use the dispatcher, or run an explicit nested loop via RunNestedLoop.")]
    public void DoEvents()
    {
        DispatchInput();
        _dispatcher?.ProcessWorkItems();
    }

    internal void RequestWake() => SignalWake();

    internal void UnregisterWindow(FramebufferWindowBackend backend)
    {
        if (ReferenceEquals(_window, backend))
        {
            _window = null;
        }
    }

    internal void SetCursor(CursorType cursorType)
    {
        _cursorType = cursorType;
        _output?.SetCursor(ShownCursor);
    }

    private CursorType? ShownCursor
        => _options.ShowCursor && _cursorType != CursorType.None && !_cursorHidden ? _cursorType : null;

    private IFramebufferOutput EnsureOutput()
    {
        if (_output != null)
        {
            return _output;
        }

        string drmFailure = string.Empty;
        IFramebufferOutput? output = null;
        if (_options.Output != FramebufferOutputKind.Fbdev)
        {
            var drm = DrmKmsOutput.TryOpen(_options, out drmFailure);
            if (drm != null)
            {
                drm.FlipCompleted += OnFlipCompleted;
                output = drm;
            }
        }

        if (output == null && _options.Output != FramebufferOutputKind.DrmKms)
        {
            output = FbdevOutput.TryOpen(_options, out string fbdevFailure);
            if (output == null)
            {
                throw new PlatformNotSupportedException(
                    $"No framebuffer output could be opened. DRM: {(drmFailure.Length > 0 ? drmFailure : "skipped")}. fbdev: {fbdevFailure}.");
            }

            if (drmFailure.Length > 0)
            {
                Console.Error.WriteLine($"[MewUI.Framebuffer] DRM unavailable ({drmFailure}); using fbdev.");
            }
        }

        if (output == null)
        {
            throw new PlatformNotSupportedException($"No DRM/KMS output could be opened: {drmFailure}.");
        }

        _output = output;
        _pointerX = output.PixelWidth / 2.0;
        _pointerY = output.PixelHeight / 2.0;
        output.MoveCursor((int)_pointerX, (int)_pointerY);
        output.SetCursor(ShownCursor);
        Console.Error.WriteLine($"[MewUI.Framebuffer] output: {output.Description}, scale {DpiScale:0.##}");
        return output;
    }

    private void OpenInput()
    {
        _keyboard = FramebufferKeyboard.Create(_options.KeyboardLayout);
        _input = FramebufferInput.TryOpen(_options.Seat, out string failure);
        Console.Error.WriteLine(_input != null
            ? $"[MewUI.Framebuffer] input: libinput seat {_options.Seat}; {_keyboard.Description}"
            : $"[MewUI.Framebuffer] {failure}; running without input");
        string? problem = FramebufferInput.DescribePermissionProblem();
        if (problem != null)
        {
            Console.Error.WriteLine($"[MewUI.Framebuffer] {problem}");
        }
    }

    private void OnFlipCompleted()
    {
        if (_app != null && _app.RenderLoopSettings.IsContinuous && _window != null)
        {
            // The next animation frame starts as soon as the screen took the last one.
            SignalWake();
        }
    }

    /// <summary>
    /// Runs the input, dispatcher and render loop until the app quits and, when <paramref name="keepRunning"/>
    /// is supplied, until it returns false.
    /// </summary>
    private void PumpLoop(Func<bool>? keepRunning)
    {
        var app = _app!;
        while (_running && (keepRunning == null || keepRunning()))
        {
            try
            {
                DispatchInput();
                _dispatcher?.ProcessWorkItems();
                RenderIfDue(app.RenderLoopSettings);
                Wait(app.RenderLoopSettings);
                WriteHeartbeat();
            }
            catch (Exception ex)
            {
                if (!HandleLoopException(app, ex))
                {
                    break;
                }
            }
        }
    }

    private void WriteHeartbeat()
    {
        if (!Logger.Enabled)
        {
            return;
        }

        _loopPasses++;
        long now = Stopwatch.GetTimestamp();
        if (now - _heartbeatTimestamp < Stopwatch.Frequency)
        {
            return;
        }

        _heartbeatTimestamp = now;
        var retained = _window?.Window.RetainedFrames;
        Logger.Write($"loop passes={_loopPasses} flipPending={_output?.FlipPending} canTake={_output?.CanTakeFrame} needsRender={_window?.NeedsRender} poll={_lastPollResult}/{_lastPollTimeout}ms retained whole={retained?.Whole} partial={retained?.Partial} untouched={retained?.Untouched} presented={_window?.Window.PresentCounts.Presented} skipped={_window?.Window.PresentCounts.Skipped}");
        _window?.Window.ResetRetainedFrameCounts();
        _loopPasses = 0;
    }

    private bool HandleLoopException(Application app, Exception ex)
    {
        if (app.TryHandleDispatcherException(ex))
        {
            return true;
        }

        app.NotifyFatalDispatcherException(ex);
        _running = false;
        return false;
    }

    private void RenderIfDue(RenderLoopSettings settings)
    {
        var window = _window;
        var output = _output;
        if (window == null || output == null || !window.IsShown || !output.CanTakeFrame)
        {
            return;
        }

        if (!settings.IsContinuous)
        {
            if (window.NeedsRender)
            {
                RenderFrame(window, output);
            }

            return;
        }

        long now = Stopwatch.GetTimestamp();
        if (now < _nextFrameTimestamp)
        {
            return;
        }

        using var pulse = AnimationManager.Instance.BeginPulse(settings);
        if (pulse.ShouldRender(window.Window, window.NeedsRender))
        {
            RenderFrame(window, output);
        }

        // Flips pace only the frames that are presented; a frame that changed nothing presents nothing, so without
        // this cap an animation that repaints nothing would spin the loop.
        int cap = settings.EffectiveFrameCap(output.RefreshHz);
        _nextFrameTimestamp = cap > 0 ? now + Stopwatch.Frequency / cap : 0;
    }

    private static void RenderFrame(FramebufferWindowBackend window, IFramebufferOutput output)
    {
        long start = Logger.Enabled ? Stopwatch.GetTimestamp() : 0;
        window.Render();
        if (Logger.Enabled)
        {
            Logger.Write($"frame {Stopwatch.GetElapsedTime(start).TotalMilliseconds:0.00}ms flipPending={output.FlipPending}");
        }
    }

    private unsafe void Wait(RenderLoopSettings settings)
    {
        if (!_running)
        {
            return;
        }

        var window = _window;
        var output = _output;
        bool flipPending = output?.FlipPending == true;
        bool canRender = window != null && window.IsShown && output?.CanTakeFrame == true;
        int timeout = _dispatcher?.GetPollTimeoutMs(MAX_WAIT_MS) ?? MAX_WAIT_MS;

        if (canRender && window!.NeedsRender && !settings.IsContinuous)
        {
            timeout = 0;
        }
        else if (canRender && settings.IsContinuous)
        {
            long remaining = _nextFrameTimestamp - Stopwatch.GetTimestamp();
            int frameWait = remaining <= 0 ? 0 : (int)Math.Max(1, remaining * 1000 / Stopwatch.Frequency);
            timeout = Math.Min(timeout, frameWait);
        }

        if (_dispatcher?.HasPendingWork == true)
        {
            timeout = 0;
        }

        // Input readiness needs no handling here: the next pass reads it at the top of the loop.
        PollFd* fds = stackalloc PollFd[3];
        int count = 0;
        int outputIndex = -1;
        int wakeIndex = -1;
        if (_input != null)
        {
            fds[count++] = new PollFd { fd = _input.Fd, events = PollEvents.POLLIN };
        }

        if (output != null && output.EventFd >= 0 && flipPending)
        {
            outputIndex = count;
            fds[count++] = new PollFd { fd = output.EventFd, events = PollEvents.POLLIN };
        }

        if (_wakeReadFd >= 0)
        {
            wakeIndex = count;
            fds[count++] = new PollFd { fd = _wakeReadFd, events = PollEvents.POLLIN };
        }

        if (count == 0)
        {
            if (timeout > 0)
            {
                Thread.Sleep(Math.Min(timeout, 8));
            }

            return;
        }

        _lastPollTimeout = timeout;
        _lastPollResult = LibC.Poll(fds, (nuint)count, timeout);
        if (_lastPollResult <= 0)
        {
            return;
        }

        if (outputIndex >= 0 && (fds[outputIndex].revents & PollEvents.POLLIN) != 0)
        {
            output!.DispatchEvents();
        }

        if (wakeIndex >= 0 && (fds[wakeIndex].revents & PollEvents.POLLIN) != 0)
        {
            DrainWakePipe();
        }
    }

    private void DispatchInput()
    {
        var output = _output;
        if (_input == null || output == null)
        {
            return;
        }

        _input.Dispatch(this, output.PixelWidth, output.PixelHeight);

        // A mouse reports up to a thousand motions a second; the pointer only has to land where the batch ended.
        if (_cursorMoved)
        {
            _cursorMoved = false;
            output.MoveCursor((int)_pointerX, (int)_pointerY);
        }
    }

    private ModifierKeys CurrentModifiers => _keyboard?.CurrentModifiers ?? ModifierKeys.None;

    void IFramebufferInputSink.OnPointerMotion(double deltaX, double deltaY)
        => MovePointer(_pointerX + deltaX, _pointerY + deltaY);

    void IFramebufferInputSink.OnPointerPosition(double x, double y) => MovePointer(x, y);

    private void MovePointer(double x, double y)
    {
        var output = _output;
        if (output == null)
        {
            return;
        }

        _pointerX = Math.Clamp(x, 0, output.PixelWidth - 1);
        _pointerY = Math.Clamp(y, 0, output.PixelHeight - 1);
        _cursorMoved = true;
        if (_cursorHidden)
        {
            _cursorHidden = false;
            output.SetCursor(ShownCursor);
        }

        _window?.RoutePointerMove(new Point(_pointerX, _pointerY), PointerType.Mouse, CurrentModifiers);
    }

    void IFramebufferInputSink.OnPointerButton(uint evdevButton, bool pressed)
    {
        MouseButton? button = evdevButton switch
        {
            BTN_LEFT => MouseButton.Left,
            BTN_RIGHT => MouseButton.Right,
            BTN_MIDDLE => MouseButton.Middle,
            BTN_SIDE => MouseButton.XButton1,
            BTN_EXTRA => MouseButton.XButton2,
            _ => null,
        };

        Logger.Write($"button {evdevButton:X} {(pressed ? "down" : "up")} at {_pointerX:0},{_pointerY:0}");
        if (button != null)
        {
            _window?.RoutePointerButton(new Point(_pointerX, _pointerY), button.Value, pressed, PointerType.Mouse, CurrentModifiers);
        }
    }

    void IFramebufferInputSink.OnScroll(double notchesX, double notchesY)
    {
        if (notchesX != 0 || notchesY != 0)
        {
            _window?.RouteWheel(new Point(_pointerX, _pointerY), new Vector(notchesX, notchesY), CurrentModifiers);
        }
    }

    void IFramebufferInputSink.OnTouch(int slot, TouchPhase phase, double x, double y, long timestamp)
    {
        Logger.Write($"touch {slot} {phase} at {x:0},{y:0} age={Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds:0.000}ms");
        if (phase == TouchPhase.Down && !_cursorHidden)
        {
            _cursorHidden = true;
            _output?.SetCursor(null);
        }

        _window?.RouteTouch(slot, phase, new Point(x, y), timestamp, CurrentModifiers);
    }

    void IFramebufferInputSink.OnKey(uint evdevCode, bool pressed)
    {
        var keyboard = _keyboard;
        if (keyboard == null)
        {
            return;
        }

        var stroke = keyboard.Process(evdevCode, pressed);
        Logger.Write($"key {evdevCode} {(pressed ? "down" : "up")} -> {stroke.Key} {stroke.Modifiers} text={(stroke.Text == null ? "-" : $"'{stroke.Text}'")}");
        if (pressed)
        {
            StopKeyRepeat();
            if (stroke.Repeats)
            {
                StartKeyRepeat(evdevCode);
            }
        }
        else if (evdevCode == _repeatingKey)
        {
            StopKeyRepeat();
        }

        _window?.RouteKey(stroke, pressed, isRepeat: false);
    }

    private void StartKeyRepeat(uint evdevCode)
    {
        var dispatcher = _dispatcher;
        if (dispatcher == null)
        {
            return;
        }

        _repeatingKey = evdevCode;
        _keyRepeat = dispatcher.Schedule(KEY_REPEAT_DELAY, () => RepeatKey(evdevCode));
    }

    private void RepeatKey(uint evdevCode)
    {
        var keyboard = _keyboard;
        var dispatcher = _dispatcher;
        if (_repeatingKey != evdevCode || keyboard == null || dispatcher == null)
        {
            return;
        }

        _window?.RouteKey(keyboard.Repeat(evdevCode), isDown: true, isRepeat: true);
        _keyRepeat = dispatcher.Schedule(KEY_REPEAT_INTERVAL, () => RepeatKey(evdevCode));
    }

    private void StopKeyRepeat()
    {
        _repeatingKey = 0;
        _keyRepeat?.Dispose();
        _keyRepeat = null;
    }

    private unsafe void SetupWakePipe()
    {
        CloseWakePipe();
        int* fds = stackalloc int[2];
        if (LibC.Pipe(fds) != 0)
        {
            return;
        }

        _wakeReadFd = fds[0];
        _wakeWriteFd = fds[1];
    }

    private void CloseWakePipe()
    {
        int readFd = Interlocked.Exchange(ref _wakeReadFd, -1);
        if (readFd >= 0)
        {
            LibC.Close(readFd);
        }

        int writeFd = Interlocked.Exchange(ref _wakeWriteFd, -1);
        if (writeFd >= 0)
        {
            LibC.Close(writeFd);
        }
    }

    private unsafe void SignalWake()
    {
        int fd = _wakeWriteFd;
        if (fd < 0)
        {
            return;
        }

        byte signal = 1;
        _ = LibC.Write(fd, &signal, 1);
    }

    private unsafe void DrainWakePipe()
    {
        int fd = _wakeReadFd;
        if (fd >= 0)
        {
            byte* buffer = stackalloc byte[64];
            _ = LibC.Read(fd, buffer, 64);
        }

        _dispatcher?.ClearWakeRequest();
    }

    public void Dispose()
    {
        StopKeyRepeat();
        _window?.Dispose();
        _window = null;
        _input?.Dispose();
        _input = null;
        _keyboard?.Dispose();
        _keyboard = null;
        _output?.Dispose();
        _output = null;
        CloseWakePipe();
    }
}
