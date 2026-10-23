using System.Diagnostics;

using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Headless;

/// <summary>
/// Platform host of a headless session: supplies in-memory services and window backends, and runs a
/// managed dispatcher loop that lays out and draws windows instead of pumping native messages.
/// </summary>
internal sealed class HeadlessPlatformHost : IPlatformHost
{
    private const int IDLE_WAIT_MS = 250;
    private const int MAX_JOB_PASSES = 64;
    private const int ANIMATION_FRAME_MS = 16;

    private readonly HeadlessOptions _options;
    private readonly HeadlessTimeSource _clock;
    private readonly string _fontFamily;
    private readonly AutoResetEvent _wake = new(false);
    private readonly List<HeadlessWindowBackend> _windows = new();
    private readonly List<HeadlessWindowBackend> _renderList = new();
    private HeadlessDispatcher? _dispatcher;
    private Application? _app;
    private volatile bool _running;
    private volatile bool _disposed;

    public HeadlessPlatformHost(HeadlessOptions options, HeadlessTimeSource clock, string fontFamily,
        HeadlessClipboard clipboard, HeadlessMessageBoxes messageBoxes, HeadlessFileDialogs fileDialogs)
    {
        _options = options;
        _clock = clock;
        _fontFamily = fontFamily;
        Clipboard = clipboard;
        MessageBox = messageBoxes;
        FileDialog = fileDialogs;
    }

    public HeadlessRendering Rendering => _options.Rendering;

    public uint Dpi => _options.Dpi;

    public HeadlessTimeSource Clock => _clock;

    public HeadlessDispatcher? Dispatcher => _dispatcher;

    public IMessageBoxService MessageBox { get; }

    public IFileDialogService FileDialog { get; }

    public IClipboardService Clipboard { get; }

    public string DefaultFontFamily => _fontFamily;

    public IReadOnlyList<string> DefaultFontFallbacks => [];

    public IWindowBackend CreateWindowBackend(Window window) => new HeadlessWindowBackend(window, this);

    public IDispatcher CreateDispatcher(nint windowHandle) => _dispatcher ?? new HeadlessDispatcher(_clock);

    public uint GetSystemDpi() => _options.Dpi;

    public ThemeVariant GetSystemThemeVariant() => _options.SystemTheme;

    public uint GetDpiForWindow(nint windowHandle) => _options.Dpi;

    public bool EnablePerMonitorDpiAwareness() => false;

    public int GetSystemMetricsForDpi(int nIndex, uint dpi) => 0;

    public void Run(Application app, Window? mainWindow)
    {
        _running = true;
        _app = app;
        var dispatcher = new HeadlessDispatcher(_clock);
        _dispatcher = dispatcher;
        dispatcher.SetWake(Wake);

        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(dispatcher);
        app.Dispatcher = dispatcher;
        try
        {
            app.OnHostLoopStarting(mainWindow);
            PumpLoop(null);
        }
        finally
        {
            app.Dispatcher = null;
            SynchronizationContext.SetSynchronizationContext(previousContext);
            _dispatcher = null;
            _app = null;
        }
    }

    public void RunNestedLoop(Func<bool> keepRunning) => PumpLoop(keepRunning);

    private void PumpLoop(Func<bool>? keepRunning)
    {
        var app = _app!;
        var dispatcher = _dispatcher!;

        while (_running && (keepRunning == null || keepRunning()))
        {
            if (!dispatcher.HasPendingWork)
            {
                // Under the virtual clock a future timer never comes due by itself, so the wait is a
                // plain idle wait that AdvanceTime and posted work cut short.
                int timeout = _clock.IsVirtual ? IDLE_WAIT_MS : dispatcher.GetPollTimeoutMs(IDLE_WAIT_MS);
                _wake.WaitOne(timeout);
            }
            dispatcher.ClearWakeRequest();

            try
            {
                dispatcher.ProcessWorkItems();
                RenderPendingWindows();
            }
            catch (Exception ex)
            {
                if (!app.TryHandleDispatcherException(ex))
                {
                    app.NotifyFatalDispatcherException(ex);
                    break;
                }
            }
        }
    }

    public void Quit(Application app)
    {
        _running = false;
        Wake();
    }

    public void DoEvents() => _dispatcher?.ProcessWorkItems();

    public void RequestRender() => Wake();

    private void Wake()
    {
        // A window or dispatcher of an ended run can still ask for a frame; the loop it would wake is gone.
        if (_disposed)
        {
            return;
        }

        try
        {
            _wake.Set();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Register(HeadlessWindowBackend backend)
    {
        if (!_windows.Contains(backend))
        {
            _windows.Add(backend);
        }
    }

    public void Unregister(HeadlessWindowBackend backend) => _windows.Remove(backend);

    /// <summary>Drains queued work and due timers, then draws the windows that asked for a frame, until nothing is left.</summary>
    public void RunJobs()
    {
        var dispatcher = _dispatcher ?? throw new InvalidOperationException("The session is not running.");
        for (int pass = 0; pass < MAX_JOB_PASSES; pass++)
        {
            dispatcher.ProcessWorkItems();
            RenderPendingWindows();

            dispatcher.GetPendingState(out bool queueHasWork, out int dueTimers, out _);
            if (!queueHasWork && dueTimers == 0)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Moves the virtual clock to the end of <paramref name="duration"/>, stopping at every timer that
    /// falls due and at every animation frame on the way so each runs at its own time.
    /// </summary>
    public void AdvanceTime(TimeSpan duration)
    {
        var dispatcher = _dispatcher ?? throw new InvalidOperationException("The session is not running.");
        long remaining = HeadlessTimeSource.ToTicks(duration);
        RunJobs();
        while (remaining > 0)
        {
            long step = remaining;

            int nextTimerMs = dispatcher.GetPollTimeoutMs(int.MaxValue);
            if (nextTimerMs > 0 && nextTimerMs < int.MaxValue)
            {
                step = Math.Min(step, HeadlessTimeSource.ToTicks(TimeSpan.FromMilliseconds(nextTimerMs)));
            }

            if (AnimationManager.Instance.HasUnpausedClocks)
            {
                step = Math.Min(step, HeadlessTimeSource.ToTicks(TimeSpan.FromMilliseconds(ANIMATION_FRAME_MS)));
            }

            step = Math.Max(1, step);
            _clock.Advance(step);
            remaining -= step;
            RunJobs();
        }
    }

    private void RenderPendingWindows()
    {
        var app = _app;
        if (app == null)
        {
            return;
        }

        using var pulse = AnimationManager.Instance.BeginPulse(app.RenderLoopSettings, _clock.GetTimestamp());

        _renderList.Clear();
        for (int index = 0; index < _windows.Count; index++)
        {
            var backend = _windows[index];
            if (backend.IsLive && pulse.ShouldRender(backend.Window, backend.NeedsRender))
            {
                _renderList.Add(backend);
            }
        }

        for (int index = 0; index < _renderList.Count; index++)
        {
            _renderList[index].RenderNow();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _wake.Dispose();
    }
}
