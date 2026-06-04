using System.Diagnostics;

using Aprillz.MewUI.Platform.Linux;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Framebuffer;

namespace Aprillz.MewUI.Platform.Framebuffer;

public sealed class FramebufferPlatformHost : IPlatformHost
{
    private readonly List<FramebufferWindowBackend> _windows = new();
    private bool _running;
    private LinuxDispatcher? _dispatcher;

    public IMessageBoxService MessageBox { get; } = new FramebufferMessageBoxService();

    public IFileDialogService FileDialog { get; } = new FramebufferFileDialogService();

    public IClipboardService Clipboard { get; } = new FramebufferClipboardService();

    public string DefaultFontFamily => "sans-serif";

    public IReadOnlyList<string> DefaultFontFallbacks { get; } =
    [
        "Noto Sans",
        "Noto Sans CJK SC",
        "Noto Sans CJK TC",
        "DejaVu Sans",
        "Liberation Sans"
    ];

    public IWindowBackend CreateWindowBackend(Window window)
    {
        var backend = new FramebufferWindowBackend(this, window);
        _windows.Add(backend);
        return backend;
    }

    public IDispatcher CreateDispatcher(nint windowHandle)
        => new LinuxDispatcher();

    public uint GetSystemDpi() => 96;

    public ThemeVariant GetSystemThemeVariant() => ThemeVariant.Light;

    public uint GetDpiForWindow(nint hwnd) => 96;

    public bool EnablePerMonitorDpiAwareness() => false;

    public int GetSystemMetricsForDpi(int nIndex, uint dpi) => 0;

    public void Run(Application app, Window mainWindow)
    {
        _running = true;

        var previousContext = SynchronizationContext.Current;
        _dispatcher = (LinuxDispatcher)CreateDispatcher(0);
        app.Dispatcher = _dispatcher;
        SynchronizationContext.SetSynchronizationContext(_dispatcher);

        try
        {
            mainWindow.Show();

            var scheduler = app.RenderLoopSettings;
            long ticksPerSecond = Stopwatch.Frequency;
            long lastFrameTicks = Stopwatch.GetTimestamp();

            while (_running && _windows.Count > 0)
            {
                try
                {
                    _dispatcher.ProcessWorkItems();
                    PollTouchInput();

                    if (scheduler.IsContinuous)
                    {
                        RenderAllWindows();
                        Throttle(scheduler.TargetFps, ticksPerSecond, ref lastFrameTicks);
                    }
                    else
                    {
                        RenderInvalidatedWindows();
                        Thread.Sleep(_dispatcher.GetPollTimeoutMs(16));
                    }
                }
                catch (Exception ex)
                {
                    if (!app.TryHandleDispatcherException(ex))
                    {
                        app.NotifyFatalDispatcherException(ex);
                        _running = false;
                    }
                }
            }
        }
        finally
        {
            foreach (var backend in _windows.ToArray())
            {
                backend.Dispose();
            }

            _windows.Clear();
            app.Dispatcher = null;
            _dispatcher = null;
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    public void Quit(Application app)
        => _running = false;

    public void DoEvents()
    {
        _dispatcher?.ProcessWorkItems();
        RenderInvalidatedWindows();
    }

    public void Dispose()
    {
        foreach (var backend in _windows.ToArray())
        {
            backend.Dispose();
        }

        _windows.Clear();
        _running = false;
    }

    internal void Unregister(FramebufferWindowBackend backend)
    {
        _windows.Remove(backend);
        if (_windows.Count == 0)
        {
            _running = false;
        }
    }

    private void RenderInvalidatedWindows()
    {
        foreach (var backend in _windows.ToArray())
        {
            if (backend.NeedsRender)
            {
                backend.RenderIfNeeded();
            }
        }
    }

    private void PollTouchInput()
    {
        foreach (var backend in _windows.ToArray())
        {
            backend.PollTouchInput();
        }
    }

    private void RenderAllWindows()
    {
        foreach (var backend in _windows.ToArray())
        {
            backend.RenderNow();
        }
    }

    private static void Throttle(int fps, long ticksPerSecond, ref long lastFrameTicks)
    {
        if (fps <= 0)
        {
            Thread.Sleep(1);
            return;
        }

        long frameTicks = ticksPerSecond / fps;
        long now = Stopwatch.GetTimestamp();
        long elapsed = now - lastFrameTicks;
        if (elapsed < frameTicks)
        {
            int waitMs = (int)((frameTicks - elapsed) * 1000 / ticksPerSecond);
            if (waitMs > 0)
            {
                Thread.Sleep(waitMs);
            }
        }

        lastFrameTicks = Stopwatch.GetTimestamp();
    }
}
