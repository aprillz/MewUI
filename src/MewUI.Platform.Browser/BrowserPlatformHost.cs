using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Platform.Browser;

internal sealed class BrowserPlatformHost : IPlatformHost
{
    private readonly TaskCompletionSource _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private BrowserDispatcher? _dispatcher;
    private BrowserWindowBackend? _window;
    private SynchronizationContext? _previousContext;
    private bool _running;
    private bool _framePending = true;
    private int _lastPixelWidth;
    private int _lastPixelHeight;

    // Matches the cap desktop hosts use for their OS wait; a longer sleep is reported as "no timer".
    private const int MAX_WAKE_DELAY_MS = 1000;

    internal static BrowserPlatformHost? Active { get; private set; }

    public IMessageBoxService MessageBox { get; } = new UnsupportedMessageBoxService();
    public IFileDialogService FileDialog { get; } = new UnsupportedFileDialogService();
    public IClipboardService Clipboard { get; } = new UnsupportedClipboardService();
    public string DefaultFontFamily => "sans-serif";
    public IReadOnlyList<string> DefaultFontFallbacks => Array.Empty<string>();

    public IWindowBackend CreateWindowBackend(Window window)
    {
        if (_window != null)
        {
            throw new NotSupportedException("MewUI Browser First Boot supports one top-level Window.");
        }

        return _window = new BrowserWindowBackend(this, window);
    }

    public IDispatcher CreateDispatcher(nint windowHandle) => new BrowserDispatcher();
    public uint GetSystemDpi() => 96;
    public ThemeVariant GetSystemThemeVariant()
        => BrowserPlatform.SystemIsDark ? ThemeVariant.Dark : ThemeVariant.Light;
    public uint GetDpiForWindow(nint windowHandle) => _window?.Dpi ?? 96;
    public bool EnablePerMonitorDpiAwareness() => true;
    public int GetSystemMetricsForDpi(int nIndex, uint dpi) => 0;

    public void Run(Application app, Window? mainWindow)
        => throw new NotSupportedException("MewUI Browser requires Application.RunAsync.");

    public Task RunAsync(Application app, Window? mainWindow, CancellationToken cancellationToken = default)
    {
        _running = true;
        Active = this;
        _previousContext = SynchronizationContext.Current;
        _dispatcher = (BrowserDispatcher)CreateDispatcher(0);
        _dispatcher.SetWake(RequestFrame);
        app.Dispatcher = _dispatcher;
        SynchronizationContext.SetSynchronizationContext(_dispatcher);
        app.OnHostLoopStarting(mainWindow);

        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(static state => ((BrowserPlatformHost)state!).Quit(Application.Current), this);
        }

        Console.WriteLine("MewUI Browser platform initialized.");
        return _completion.Task;
    }

    public void Quit(Application app)
    {
        if (!_running)
        {
            return;
        }

        _running = false;
        _completion.TrySetResult();
    }

    public void DoEvents() => _dispatcher?.ProcessWorkItems();

    /// <summary>Asks for one more frame; the JS loop reads this through <see cref="RenderFrame"/>.</summary>
    internal void RequestFrame() => _framePending = true;

    /// <summary>
    /// Whether the next animation frame has anything to do. Idling without this would repaint the
    /// canvas at the display's refresh rate even when nothing changed.
    /// </summary>
    private bool ShouldRenderFrame()
        => _framePending
            || _window?.NeedsRender == true
            || _dispatcher?.HasPendingWork == true
            || (Application.IsRunning && Application.Current.RenderLoopSettings.IsContinuous);

    /// <summary>
    /// Milliseconds until the loop must run again for a scheduled timer, or -1 when nothing is
    /// pending. Desktop hosts pass the same value to their OS wait; the page uses it to set a
    /// timeout so a sleeping loop still fires DispatcherTimer on time.
    /// </summary>
    internal int NextWakeDelayMs()
    {
        if (_dispatcher == null)
        {
            return -1;
        }

        int timeout = _dispatcher.GetPollTimeoutMs(MAX_WAKE_DELAY_MS);
        return timeout >= MAX_WAKE_DELAY_MS ? -1 : timeout;
    }

    /// <summary>
    /// Runs one animation frame. Returns false when nothing needed drawing, so the host page can
    /// stop scheduling frames until something asks for one.
    /// </summary>
    internal bool RenderFrame(double cssWidth, double cssHeight, double devicePixelRatio, int pixelWidth, int pixelHeight)
    {
        if (!_running || _window == null)
        {
            return false;
        }

        try
        {
            _dispatcher?.ClearWakeRequest();
            _dispatcher?.ProcessWorkItems();

            // Sizing the drawing buffer discards its contents, so a frame that resized it must
            // paint: returning without drawing would leave the cleared buffer on screen.
            if (pixelWidth != _lastPixelWidth || pixelHeight != _lastPixelHeight)
            {
                _lastPixelWidth = pixelWidth;
                _lastPixelHeight = pixelHeight;
                _framePending = true;
            }

            if (!ShouldRenderFrame())
            {
                return false;
            }

            // Advances every animation clock for this frame; without a pulse the clocks never
            // move and animated properties stay at their first value.
            var app = Application.Current;
            using var pulse = AnimationManager.Instance.BeginPulse(app.RenderLoopSettings);
            bool wanted = _framePending || pulse.ShouldRender(_window.Window, _window.NeedsRender);
            _framePending = false;
            if (!wanted)
            {
                return false;
            }

            _window.RenderFrame(cssWidth, cssHeight, devicePixelRatio, pixelWidth, pixelHeight);
            return true;
        }
        catch (Exception ex)
        {
            Application.RouteLifecycleException(ex);
            return true;
        }
    }

    internal bool PointerMove(double x, double y, double screenX, double screenY, int buttons, ModifierKeys modifiers)
        => RoutePointer(() => _window?.PointerMove(x, y, screenX, screenY, buttons, modifiers) == true);

    internal bool PointerButton(double x, double y, double screenX, double screenY, int button, int buttons,
        bool isDown, int clickCount, ModifierKeys modifiers, PointerType pointerType)
        => RoutePointer(() => _window?.PointerButton(
            x, y, screenX, screenY, button, buttons, isDown, clickCount, modifiers, pointerType) == true);

    internal void PointerWheel(double x, double y, double screenX, double screenY,
        double deltaX, double deltaY, int buttons, ModifierKeys modifiers)
        => RoutePointer(() =>
        {
            _window?.PointerWheel(x, y, screenX, screenY, deltaX, deltaY, buttons, modifiers);
            return false;
        });

    internal bool CaptureConsumesDrag() => _window?.CaptureConsumesDrag() == true;

    internal bool WantsTextInput() => _window?.WantsTextInput() == true;

    internal void PointerPan(double x, double y, double screenX, double screenY,
        double deltaXDip, double deltaYDip, ModifierKeys modifiers)
        => RoutePointer(() =>
        {
            _window?.PointerPan(x, y, screenX, screenY, deltaXDip, deltaYDip, modifiers);
            return false;
        });

    internal void PointerLeave() => RoutePointer(() => { _window?.PointerLeave(); return false; });
    internal void PointerCancel() => RoutePointer(() => { _window?.PointerCancel(); return false; });

    internal bool KeyDown(string code, int platformKey, ModifierKeys modifiers, bool isRepeat)
        => RoutePointer(() => _window?.KeyDown(code, platformKey, modifiers, isRepeat) == true);

    internal bool KeyUp(string code, int platformKey, ModifierKeys modifiers)
        => RoutePointer(() => _window?.KeyUp(code, platformKey, modifiers) == true);

    internal bool TextInput(string text)
        => RoutePointer(() => _window?.TextInput(text) == true);

    internal void FocusChanged(bool focused)
        => RoutePointer(() => { _window?.SetFocus(focused); return false; });

    private static bool RoutePointer(Func<bool> route)
    {
        try
        {
            return route();
        }
        catch (Exception ex)
        {
            Application.RouteLifecycleException(ex);
            return false;
        }
    }

    public void Dispose()
    {
        _running = false;
        if (ReferenceEquals(Active, this))
        {
            Active = null;
        }

        _window?.Dispose();
        _window = null;
        _dispatcher = null;
        SynchronizationContext.SetSynchronizationContext(_previousContext);
        _previousContext = null;
    }

    private sealed class BrowserDispatcher : ManagedUiDispatcher
    {
        protected override int MaxPumpIterations => 8;
        protected override int NoTimerPollTimeout(int maxMs) => maxMs;
        protected override void DispatchDueTimer(Action action) => action();
    }

    private sealed class UnsupportedMessageBoxService : IMessageBoxService
    {
        public bool IsNativeDialogAvailable() => false;
        public bool? Show(nint owner, string text, string caption, NativeMessageBoxButtons buttons, NativeMessageBoxIcon icon)
            => throw new NotSupportedException("Message boxes are not implemented in Browser First Boot.");
    }

    private sealed class UnsupportedFileDialogService : IFileDialogService
    {
        public bool IsNativeDialogAvailable() => false;
        public string[]? OpenFile(OpenFileDialogOptions options)
            => throw new NotSupportedException("File dialogs are not implemented in Browser First Boot.");
        public string? SaveFile(SaveFileDialogOptions options)
            => throw new NotSupportedException("File dialogs are not implemented in Browser First Boot.");
        public string? SelectFolder(FolderDialogOptions options)
            => throw new NotSupportedException("File dialogs are not implemented in Browser First Boot.");
    }

    private sealed class UnsupportedClipboardService : IClipboardService
    {
        public bool TrySetText(string text) => false;
        public bool TryGetText(out string text)
        {
            text = string.Empty;
            return false;
        }
    }
}
