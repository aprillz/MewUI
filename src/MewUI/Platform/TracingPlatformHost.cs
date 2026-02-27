namespace Aprillz.MewUI.Platform;

internal sealed class TracingPlatformHost : IPlatformHost
{
    private static int _nextId;
    private readonly int _id = Interlocked.Increment(ref _nextId);
    private readonly IPlatformHost _inner;

    public TracingPlatformHost(IPlatformHost inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        DiagLog.WriteAlways($"[PlatformHost#{_id}] created ({inner.GetType().Name})");
    }

    public IMessageBoxService MessageBox => _inner.MessageBox;
    public IFileDialogService FileDialog => _inner.FileDialog;
    public IClipboardService Clipboard => _inner.Clipboard;

    public IWindowBackend CreateWindowBackend(Window window)
    {
        DiagLog.WriteAlways($"[PlatformHost#{_id}] CreateWindowBackend title='{window.Title}'");
        var backend = _inner.CreateWindowBackend(window);
        return new TracingWindowBackend(backend, window);
    }

    public IDispatcher CreateDispatcher(nint windowHandle)
    {
        DiagLog.WriteAlways($"[PlatformHost#{_id}] CreateDispatcher hwnd=0x{windowHandle:x}");
        var dispatcher = _inner.CreateDispatcher(windowHandle);
        return new TracingDispatcher(dispatcher, windowHandle);
    }

    public uint GetSystemDpi() => _inner.GetSystemDpi();
    public ThemeVariant GetSystemThemeVariant() => _inner.GetSystemThemeVariant();
    public uint GetDpiForWindow(nint hwnd) => _inner.GetDpiForWindow(hwnd);
    public bool EnablePerMonitorDpiAwareness() => _inner.EnablePerMonitorDpiAwareness();
    public int GetSystemMetricsForDpi(int nIndex, uint dpi) => _inner.GetSystemMetricsForDpi(nIndex, dpi);

    public void Run(Application app, Window mainWindow)
    {
        DiagLog.WriteAlways($"[PlatformHost#{_id}] Run mainTitle='{mainWindow.Title}'");
        _inner.Run(app, mainWindow);
        DiagLog.WriteAlways($"[PlatformHost#{_id}] Run returned");
    }

    public void Quit(Application app)
    {
        DiagLog.WriteAlways($"[PlatformHost#{_id}] Quit");
        _inner.Quit(app);
    }

    public void DoEvents()
    {
        DiagLog.WriteAlways($"[PlatformHost#{_id}] DoEvents");
        _inner.DoEvents();
    }

    public void Dispose()
    {
        DiagLog.WriteAlways($"[PlatformHost#{_id}] Dispose");
        _inner.Dispose();
    }

    private sealed class TracingWindowBackend : IWindowBackend
    {
        private static int _nextBackendId;
        private readonly int _backendId = Interlocked.Increment(ref _nextBackendId);
        private readonly IWindowBackend _innerBackend;
        private readonly Window _window;

        public TracingWindowBackend(IWindowBackend innerBackend, Window window)
        {
            _innerBackend = innerBackend ?? throw new ArgumentNullException(nameof(innerBackend));
            _window = window ?? throw new ArgumentNullException(nameof(window));
            DiagLog.WriteAlways($"[WindowBackend#{_backendId}] created ({innerBackend.GetType().Name}) for '{window.Title}'");
        }

        public nint Handle => _innerBackend.Handle;

        public void SetResizable(bool resizable)
        {
            DiagLog.WriteAlways($"[WindowBackend#{_backendId}] SetResizable {resizable}");
            _innerBackend.SetResizable(resizable);
        }

        public void Show()
        {
            DiagLog.WriteAlways($"[WindowBackend#{_backendId}] Show");
            _innerBackend.Show();
        }

        public void Hide()
        {
            DiagLog.WriteAlways($"[WindowBackend#{_backendId}] Hide");
            _innerBackend.Hide();
        }

        public void Close()
        {
            DiagLog.WriteAlways($"[WindowBackend#{_backendId}] Close");
            _innerBackend.Close();
        }

        public void Invalidate(bool erase) => _innerBackend.Invalidate(erase);

        public void SetTitle(string title)
        {
            DiagLog.WriteAlways($"[WindowBackend#{_backendId}] SetTitle '{title}'");
            _innerBackend.SetTitle(title);
        }

        public void SetIcon(IconSource? icon)
        {
            DiagLog.WriteAlways($"[WindowBackend#{_backendId}] SetIcon {(icon == null ? "null" : "set")}");
            _innerBackend.SetIcon(icon);
        }

        public void SetClientSize(double widthDip, double heightDip)
            => _innerBackend.SetClientSize(widthDip, heightDip);

        public Point GetPosition() => _innerBackend.GetPosition();

        public void SetPosition(double leftDip, double topDip)
        {
            DiagLog.WriteAlways($"[WindowBackend#{_backendId}] SetPosition {leftDip:0.##},{topDip:0.##}");
            _innerBackend.SetPosition(leftDip, topDip);
        }

        public void CaptureMouse()
        {
            DiagLog.WriteAlways($"[WindowBackend#{_backendId}] CaptureMouse");
            _innerBackend.CaptureMouse();
        }

        public void ReleaseMouseCapture()
        {
            DiagLog.WriteAlways($"[WindowBackend#{_backendId}] ReleaseMouseCapture");
            _innerBackend.ReleaseMouseCapture();
        }

        public Point ClientToScreen(Point clientPointDip) => _innerBackend.ClientToScreen(clientPointDip);
        public Point ScreenToClient(Point screenPointPx) => _innerBackend.ScreenToClient(screenPointPx);

        public void EnsureTheme(bool isDark)
        {
            DiagLog.WriteAlways($"[WindowBackend#{_backendId}] EnsureTheme isDark={isDark}");
            _innerBackend.EnsureTheme(isDark);
        }

        public void Activate()
        {
            DiagLog.WriteAlways($"[WindowBackend#{_backendId}] Activate");
            _innerBackend.Activate();
        }

        public void SetOwner(nint ownerHandle)
        {
            DiagLog.WriteAlways($"[WindowBackend#{_backendId}] SetOwner ownerHandle=0x{ownerHandle:x}");
            _innerBackend.SetOwner(ownerHandle);
        }

        public void SetEnabled(bool enabled)
        {
            DiagLog.WriteAlways($"[WindowBackend#{_backendId}] SetEnabled {enabled}");
            _innerBackend.SetEnabled(enabled);
        }

        public void SetOpacity(double opacity)
        {
            DiagLog.WriteAlways($"[WindowBackend#{_backendId}] SetOpacity {opacity:0.###}");
            _innerBackend.SetOpacity(opacity);
        }

        public void SetAllowsTransparency(bool allowsTransparency)
        {
            DiagLog.WriteAlways($"[WindowBackend#{_backendId}] SetAllowsTransparency {allowsTransparency}");
            _innerBackend.SetAllowsTransparency(allowsTransparency);
        }

        public void Dispose()
        {
            DiagLog.WriteAlways($"[WindowBackend#{_backendId}] Dispose windowTitle='{_window.Title}'");
            _innerBackend.Dispose();
        }
    }

    private sealed class TracingDispatcher : IDispatcher, IDispatcherCore
    {
        private static int _nextDispatcherId;
        private readonly int _dispatcherId = Interlocked.Increment(ref _nextDispatcherId);
        private readonly IDispatcher _innerDispatcher;
        private readonly IDispatcherCore? _innerCore;
        private readonly nint _hwnd;

        public TracingDispatcher(IDispatcher innerDispatcher, nint hwnd)
        {
            _innerDispatcher = innerDispatcher ?? throw new ArgumentNullException(nameof(innerDispatcher));
            _innerCore = innerDispatcher as IDispatcherCore;
            _hwnd = hwnd;
            DiagLog.WriteAlways($"[Dispatcher#{_dispatcherId}] created ({innerDispatcher.GetType().Name}) hwnd=0x{hwnd:x}");
        }

        public bool IsOnUIThread => _innerDispatcher.IsOnUIThread;

        public DispatcherOperation BeginInvoke(Action action) => _innerDispatcher.BeginInvoke(action);

        public DispatcherOperation BeginInvoke(DispatcherPriority priority, Action action) => _innerDispatcher.BeginInvoke(priority, action);

        public bool PostMerged(DispatcherMergeKey mergeKey, Action action, DispatcherPriority priority)
        {
            bool enqueued = _innerCore?.PostMerged(mergeKey, action, priority) ?? false;
            if (!enqueued)
            {
                DiagLog.WriteAlways($"[Dispatcher#{_dispatcherId}] PostMerged suppressed key={mergeKey} prio={priority}");
            }
            return enqueued;
        }

        public void Invoke(Action action) => _innerDispatcher.Invoke(action);

        public IDisposable Schedule(TimeSpan dueTime, Action action)
        {
            if (dueTime > TimeSpan.Zero)
            {
                DiagLog.WriteAlways($"[Dispatcher#{_dispatcherId}] Schedule due={dueTime.TotalMilliseconds:0.##}ms hwnd=0x{_hwnd:x}");
            }

            return _innerCore!.Schedule(dueTime, action);
        }

        public void ProcessWorkItems() => _innerCore!.ProcessWorkItems();
    }
}
