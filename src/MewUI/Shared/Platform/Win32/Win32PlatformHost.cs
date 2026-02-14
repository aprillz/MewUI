using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

using Microsoft.Win32;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Platform.Win32;

[SupportedOSPlatform("windows")]
public sealed class Win32PlatformHost : IPlatformHost
{
    internal const string WindowClassName = "AprillzMewUIWindow";

    private readonly Dictionary<nint, Win32WindowBackend> _windows = new();
    private readonly List<Win32WindowBackend> _renderBackends = new(capacity: 8);
    private WndProc? _wndProcDelegate;
    private bool _running;
    private ushort _classAtom;
    private nint _classNamePtr;
    private nint _moduleHandle;
    private SynchronizationContext? _previousSynchronizationContext;
    private nint _dispatcherHwnd;
    private Win32UiDispatcher? _dispatcher;
    private Application? _app;
    private ThemeVariant _lastSystemTheme = ThemeVariant.Light;
    private int _renderRequested;
    private nint _renderEvent;

    public IMessageBoxService MessageBox { get; } = new Win32MessageBoxService();

    public IFileDialogService FileDialog { get; } = new Win32FileDialogService();

    public IClipboardService Clipboard { get; } = new Win32ClipboardService();

    public IWindowBackend CreateWindowBackend(Window window) => new Win32WindowBackend(this, window);

    public IUiDispatcher CreateDispatcher(nint windowHandle) => new Win32UiDispatcher(windowHandle);

    public uint GetSystemDpi() => User32.GetDpiForSystem();

    public ThemeVariant GetSystemThemeVariant() => GetSystemThemeVariantFromRegistry();

    public uint GetDpiForWindow(nint hwnd) => hwnd != 0 ? User32.GetDpiForWindow(hwnd) : User32.GetDpiForSystem();

    public bool EnablePerMonitorDpiAwareness()
    {
        const nint DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;
        return User32.SetProcessDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
    }

    public int GetSystemMetricsForDpi(int nIndex, uint dpi) => User32.GetSystemMetricsForDpi(nIndex, dpi);

    private static ThemeVariant GetSystemThemeVariantFromRegistry()
    {
        try
        {
            // Windows app theme (Light/Dark) is commonly exposed via registry.
            // 1 = light, 0 = dark
            // HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme (DWORD)
            object? v = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                null);

            if (v is int i)
            {
                return i == 0 ? ThemeVariant.Dark : ThemeVariant.Light;
            }

            if (v is uint u)
            {
                return u == 0 ? ThemeVariant.Dark : ThemeVariant.Light;
            }
        }
        catch
        {
            // Best-effort. If registry access fails, fallback to Light.
        }

        return ThemeVariant.Light;
    }

    internal void RegisterWindow(nint hwnd, Win32WindowBackend backend) => _windows[hwnd] = backend;

    internal void UnregisterWindow(nint hwnd)
    {
        _windows.Remove(hwnd);
        if (_windows.Count == 0)
        {
            _running = false;
            User32.PostQuitMessage(0);
        }
    }

    public void Run(Application app, Window mainWindow)
    {
        try
        {
            DpiHelper.EnablePerMonitorDpiAwareness();
            RegisterWindowClass();

            _running = true;

            _previousSynchronizationContext = SynchronizationContext.Current;
            EnsureDispatcher(app);
            _app = app;

            EnsureRenderEvent();

            // Initialize and apply System theme at startup (best-effort).
            _lastSystemTheme = GetSystemThemeVariant();
            if (app.ThemeMode == ThemeVariant.System)
            {
                app.NotifySystemThemeChanged();
            }

            // Show after dispatcher is ready so timers/postbacks work immediately (WPF-style dispatcher lifetime).
            mainWindow.Show();

            var scheduler = app.RenderLoopSettings;
            long ticksPerSecond = Stopwatch.Frequency;
            long lastFrameTicks = Stopwatch.GetTimestamp();

            while (_running)
            {
                try
                {
                    if (scheduler.Mode == RenderLoopMode.Continuous)
                    {
                        ProcessMessages();
                        if (!_running)
                        {
                            break;
                        }

                        RenderAllWindows();

                        int fps = scheduler.TargetFps;
                        if (fps > 0)
                        {
                            long frameTicks = ticksPerSecond / fps;
                            long now = Stopwatch.GetTimestamp();
                            long elapsed = now - lastFrameTicks;
                            if (elapsed < frameTicks)
                            {
                                var waitMs = (frameTicks - elapsed) * 1000 / ticksPerSecond;
                                if (waitMs > 0)
                                {
                                    WaitForMessagesOrRender((uint)waitMs, renderOnSignal: false);
                                }
                            }
                            lastFrameTicks = Stopwatch.GetTimestamp();
                        }
                        else
                        {
                            WaitForMessagesOrRender(0, renderOnSignal: false);
                        }
                    }
                    else
                    {
                        WaitForMessagesOrRender(0xFFFFFFFF, renderOnSignal: true);
                        ProcessMessages();

                        if (_running && AnyWindowNeedsRender())
                        {
                            RequestRender();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Avoid letting managed exceptions escape into the native message loop.
                    // Dispatcher-level handling is performed by the dispatcher queue.
                    app.NotifyFatalDispatcherException(ex);
                    _running = false;
                    User32.PostQuitMessage(0);
                    break;
                }
            }
        }
        finally
        {
            Shutdown(app);
        }
    }

    public void Quit(Application app)
    {
        _running = false;
        User32.PostQuitMessage(0);
    }

    public void DoEvents()
    {
        MSG msg;
        while (User32.PeekMessage(out msg, 0, 0, 0, 1)) // PM_REMOVE = 1
        {
            try
            {
                User32.TranslateMessage(ref msg);
                User32.DispatchMessage(ref msg);
            }
            catch (Exception ex)
            {
                if (Application.IsRunning)
                {
                    Application.Current.NotifyFatalDispatcherException(ex);
                }
                _running = false;
                User32.PostQuitMessage(0);
                break;
            }
        }
    }

    private void RegisterWindowClass()
    {
        _wndProcDelegate = WndProc;
        var wndProcPtr = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);

        _moduleHandle = Kernel32.GetModuleHandle(null);
        _classNamePtr = Marshal.StringToHGlobalUni(WindowClassName);

        var wndClass = WNDCLASSEX.Create();
        // CS_OWNDC is important for stable OpenGL (WGL) contexts; it is harmless for other backends.
        wndClass.style = ClassStyles.CS_HREDRAW | ClassStyles.CS_VREDRAW | ClassStyles.CS_DBLCLKS | ClassStyles.CS_OWNDC;
        wndClass.lpfnWndProc = wndProcPtr;
        wndClass.cbClsExtra = 0;
        wndClass.cbWndExtra = 0;
        wndClass.hInstance = _moduleHandle;
        wndClass.hIcon = 0;
        wndClass.hCursor = User32.LoadCursor(0, SystemCursors.IDC_ARROW);
        wndClass.hbrBackground = 0;
        wndClass.lpszMenuName = 0;
        wndClass.lpszClassName = _classNamePtr;
        wndClass.hIconSm = 0;

        _classAtom = User32.RegisterClassEx(ref wndClass);
        if (_classAtom == 0)
        {
            Marshal.FreeHGlobal(_classNamePtr);
            _classNamePtr = 0;
            throw new InvalidOperationException($"Failed to register window class. Error: {Marshal.GetLastWin32Error()}");
        }
    }

    private nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == WindowMessages.WM_SETTINGCHANGE ||
            msg == WindowMessages.WM_THEMECHANGED ||
            msg == WindowMessages.WM_SYSCOLORCHANGE)
        {
            TryUpdateSystemTheme();
        }

        if (hWnd == _dispatcherHwnd)
        {
            switch (msg)
            {
                case Win32UiDispatcher.WM_INVOKE:
                    _dispatcher?.ClearInvokeRequest();
                    _dispatcher?.ProcessWorkItems();
                    if (_dispatcher?.HasPendingWork == true)
                    {
                        _dispatcher?.Post(() => { }, UiDispatcherPriority.Background);
                    }
                    return 0;

                case WindowMessages.WM_TIMER:
                    if (_dispatcher?.ProcessTimer((nuint)wParam) == true)
                    {
                        return 0;
                    }
                    return 0;
            }
        }

        if (_windows.TryGetValue(hWnd, out var backend))
        {
            try
            {
                return backend.ProcessMessage(msg, wParam, lParam);
            }
            catch (Exception ex)
            {
                Application.Current.NotifyFatalDispatcherException(ex);
                _running = false;
                User32.PostQuitMessage(0);
                return 0;
            }
        }

        return User32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    internal void RequestRender()
    {
        if (_renderEvent == 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref _renderRequested, 1) == 0)
        {
            Kernel32.SetEvent(_renderEvent);
        }
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

        if (_renderBackends.Count == 0)
        {
            return;
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

    private void WaitForMessagesOrRender(uint timeoutMs, bool renderOnSignal)
    {
        if (_renderEvent == 0)
        {
            if (timeoutMs == 0)
            {
                return;
            }

            Thread.Sleep(timeoutMs == 0xFFFFFFFF ? 1 : (int)timeoutMs);
            return;
        }

        unsafe
        {
            nint* handles = stackalloc nint[1];
            handles[0] = _renderEvent;
            uint result = User32.MsgWaitForMultipleObjectsEx(
                1,
                handles,
                timeoutMs,
                WaitConstants.QS_ALLINPUT,
                WaitConstants.MWMO_INPUTAVAILABLE);

            if (result == WaitConstants.WAIT_OBJECT_0)
            {
                Interlocked.Exchange(ref _renderRequested, 0);
                if (renderOnSignal)
                {
                    RenderInvalidatedWindows();
                    if (AnyWindowNeedsRender())
                    {
                        RequestRender();
                    }
                }
            }
        }
    }

    private void ProcessMessages()
    {
        MSG msg;
        while (User32.PeekMessage(out msg, 0, 0, 0, 1)) // PM_REMOVE
        {
            if (msg.message == WindowMessages.WM_QUIT)
            {
                _running = false;
                break;
            }

            User32.TranslateMessage(ref msg);
            User32.DispatchMessage(ref msg);
        }
    }

    private void EnsureRenderEvent()
    {
        if (_renderEvent != 0)
        {
            return;
        }

        _renderEvent = Kernel32.CreateEvent(0, bManualReset: false, bInitialState: false, 0);
        if (_renderEvent == 0)
        {
            throw new InvalidOperationException($"Failed to create render event. Error: {Marshal.GetLastWin32Error()}");
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

    private void EnsureDispatcher(Application app)
    {
        if (_dispatcher != null && _dispatcherHwnd != 0)
        {
            app.Dispatcher = _dispatcher;
            SynchronizationContext.SetSynchronizationContext(_dispatcher);
            return;
        }

        const nint HWND_MESSAGE = -3;
        _dispatcherHwnd = User32.CreateWindowEx(
            0,
            WindowClassName,
            "AprillzMewUI_Dispatcher",
            dwStyle: 0,
            x: 0,
            y: 0,
            nWidth: 0,
            nHeight: 0,
            hWndParent: HWND_MESSAGE,
            hMenu: 0,
            hInstance: _moduleHandle,
            lpParam: 0);

        if (_dispatcherHwnd == 0)
        {
            throw new InvalidOperationException($"Failed to create dispatcher window. Error: {Marshal.GetLastWin32Error()}");
        }

        _dispatcher = new Win32UiDispatcher(_dispatcherHwnd);
        app.Dispatcher = _dispatcher;
        SynchronizationContext.SetSynchronizationContext(_dispatcher);
    }

    private void Shutdown(Application app)
    {
        SynchronizationContext.SetSynchronizationContext(_previousSynchronizationContext);
        app.Dispatcher = null;
        _app = null;

        foreach (var backend in _windows.Values.ToArray())
        {
            backend.Window.Close();
        }
        _windows.Clear();

        _dispatcher = null;
        if (_dispatcherHwnd != 0 && User32.IsWindow(_dispatcherHwnd))
        {
            User32.DestroyWindow(_dispatcherHwnd);
        }
        _dispatcherHwnd = 0;

        if (_renderEvent != 0)
        {
            Kernel32.CloseHandle(_renderEvent);
            _renderEvent = 0;
        }

        if (_classAtom != 0)
        {
            User32.UnregisterClass(WindowClassName, _moduleHandle);
            _classAtom = 0;
        }

        if (_classNamePtr != 0)
        {
            Marshal.FreeHGlobal(_classNamePtr);
            _classNamePtr = 0;
        }
    }

    public void Dispose()
    { }
}
