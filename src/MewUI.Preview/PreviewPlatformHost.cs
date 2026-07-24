using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Preview;

/// <summary>
/// Platform host for preview sessions: delegates platform services to the registered host but
/// supplies headless window backends and runs a managed dispatcher loop that renders preview
/// frames instead of pumping native messages.
/// </summary>
internal sealed class PreviewPlatformHost : IPlatformHost
{
    private const int IDLE_WAIT_MS = 250;
    private const uint PREVIEW_DPI = 96;

    private readonly IPlatformHost _inner;
    private readonly PreviewSession _session = new();
    private readonly AutoResetEvent _wake = new(false);
    private PreviewDispatcher? _dispatcher;
    private Application? _app;
    private volatile bool _running;

    public PreviewPlatformHost(IPlatformHost inner)
    {
        _inner = inner;
    }

    public IMessageBoxService MessageBox => _inner.MessageBox;

    public IFileDialogService FileDialog => _inner.FileDialog;

    public IClipboardService Clipboard => _inner.Clipboard;

    public string DefaultFontFamily => _inner.DefaultFontFamily;

    public IReadOnlyList<string> DefaultFontFallbacks => _inner.DefaultFontFallbacks;

    public IShellIconProvider ShellIconProvider => _inner.ShellIconProvider;

    public IMountedVolumeProvider MountedVolumeProvider => _inner.MountedVolumeProvider;

    public IWindowBackend CreateWindowBackend(Window window) => new PreviewWindowBackend(window, _session);

    public IDispatcher CreateDispatcher(nint windowHandle) => new PreviewDispatcher();

    public uint GetSystemDpi() => PREVIEW_DPI;

    public ThemeVariant GetSystemThemeVariant() => _inner.GetSystemThemeVariant();

    public uint GetDpiForWindow(nint windowHandle) => PREVIEW_DPI;

    public bool EnablePerMonitorDpiAwareness() => false;

    public int GetSystemMetricsForDpi(int nIndex, uint dpi) => _inner.GetSystemMetricsForDpi(nIndex, dpi);

    public void Run(Application app, Window mainWindow)
    {
        _running = true;
        _app = app;
        var dispatcher = new PreviewDispatcher();
        _dispatcher = dispatcher;
        dispatcher.SetWake(() => _wake.Set());

        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(dispatcher);
        app.Dispatcher = dispatcher;
        try
        {
            _session.Start(app, mainWindow, () => _wake.Set());
            mainWindow.Show();
            PumpLoop(null);
        }
        finally
        {
            _session.Stop();
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
                _wake.WaitOne(dispatcher.GetPollTimeoutMs(IDLE_WAIT_MS));
            }
            dispatcher.ClearWakeRequest();

            try
            {
                dispatcher.ProcessWorkItems();
                _session.RenderPendingFrame();
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
        _wake.Set();
    }

    public void DoEvents() => _dispatcher?.ProcessWorkItems();

    public void Dispose()
    {
        _wake.Dispose();
        _inner.Dispose();
    }
}
