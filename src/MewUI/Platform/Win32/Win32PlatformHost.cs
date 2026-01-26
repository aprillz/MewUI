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
    private readonly IMessageBoxService _messageBox = new Win32MessageBoxService();
    private readonly IFileDialogService _fileDialog = new Win32FileDialogService();
    private readonly IClipboardService _clipboard = new Win32ClipboardService();
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

    public IMessageBoxService MessageBox => _messageBox;

    public IFileDialogService FileDialog => _fileDialog;

    public IClipboardService Clipboard => _clipboard;

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

            // Initialize and apply System theme at startup (best-effort).
            _lastSystemTheme = GetSystemThemeVariant();
            if (Theme.Default == ThemeVariant.System)
            {
                app.NotifySystemThemeChanged();
            }

            // Show after dispatcher is ready so timers/postbacks work immediately (WPF-style dispatcher lifetime).
            mainWindow.Show();

            MSG msg;
            while (_running && User32.GetMessage(out msg, 0, 0, 0) > 0)
            {
                try
                {
                    User32.TranslateMessage(ref msg);
                    User32.DispatchMessage(ref msg);
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
                    _dispatcher?.ProcessWorkItems();
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

    private void TryUpdateSystemTheme()
    {
        var app = _app;
        if (app == null)
        {
            return;
        }

        if (Theme.Default != ThemeVariant.System)
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

    public void Dispose() { }
}
