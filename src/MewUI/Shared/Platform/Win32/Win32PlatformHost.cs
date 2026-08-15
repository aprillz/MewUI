using System.Diagnostics;
using System.Runtime.InteropServices;

using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;

using Microsoft.Win32;

namespace Aprillz.MewUI.Platform.Win32;

public sealed class Win32PlatformHost : IPlatformHost
{
    public const string PlatformIdentifier = "Win32";

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
    private Win32Dispatcher? _dispatcher;
    private Application? _app;
    private ThemeVariant _lastSystemTheme = ThemeVariant.Light;
    private int _renderRequested;
    private nint _renderEvent;
    private nint _frameBudgetTimer;
    private bool _frameBudgetTimerResolved;
    // Refresh rate in Hz, 0 before it is read and -1 when the driver reports no usable rate.
    private int _displayRefreshHz;
    private long _displayRefreshReadAt;

    // Re-read so a window moved to another monitor picks up that monitor's rate, without paying a
    // device context per frame.
    private const int REFRESH_RECHECK_SECONDS = 2;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>
    /// Gets the system UI font family, queried once so registration can report it without a host instance.
    /// </summary>
    internal static string SystemFontFamily { get; } = QuerySystemFontFamily();

    public string DefaultFontFamily => SystemFontFamily;

    public IReadOnlyList<string> DefaultFontFallbacks { get; } = BuildDefaultFontFallbacks();

    private static string[] BuildDefaultFontFallbacks()
    {
        var locale = Rendering.FontFallback.ResolvedLocale;
        var cjk = Rendering.FontFallback.OrderCjkByLocale(locale,
            kr: "Malgun Gothic", jp: "Yu Gothic UI",
            sc: "Microsoft YaHei UI", tc: "Microsoft JhengHei UI");

        var chain = new List<string>(12) { "Segoe UI Emoji" };
        chain.AddRange(cjk);
        chain.AddRange([
            "Segoe UI", "Segoe UI Symbol", "Segoe UI Historic",
            "Cambria Math",
        ]);
        return [.. chain];
    }

    private static string QuerySystemFontFamily()
    {
        try
        {
            // NONCLIENTMETRICS size varies by Windows version.
            // On Vista+ the struct is 504 bytes (with iPaddedBorderWidth).
            const int NONCLIENTMETRICS_SIZE = 504;
            const int LOGFONT_FACENAME_OFFSET = 436; // offset 408 (lfMessageFont) + 28 (lfFaceName in LOGFONT)

            nint buffer = Marshal.AllocHGlobal(NONCLIENTMETRICS_SIZE);
            try
            {
                Marshal.WriteInt32(buffer, NONCLIENTMETRICS_SIZE); // cbSize
                if (User32.SystemParametersInfo(User32.SPI_GETNONCLIENTMETRICS, (uint)NONCLIENTMETRICS_SIZE, buffer, 0))
                {
                    unsafe
                    {
                        char* faceName = (char*)((byte*)buffer + LOGFONT_FACENAME_OFFSET);
                        var name = new string(faceName);
                        if (!string.IsNullOrWhiteSpace(name))
                            return name;
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch
        {
        }

        return "Segoe UI";
    }

    public IMessageBoxService MessageBox { get; } = new Win32MessageBoxService();

    public IFileDialogService FileDialog { get; } = new Win32FileDialogService();

    public IShellIconProvider ShellIconProvider { get; } = new WindowsShellIconProvider();

    public IMountedVolumeProvider MountedVolumeProvider { get; } = new WindowsMountedVolumeProvider();

    public IPlacesProvider PlacesProvider { get; } = new WindowsPlacesProvider();

    public IClipboardService Clipboard { get; } = new Win32ClipboardService();

    public IWindowBackend CreateWindowBackend(Window window) => new Win32WindowBackend(this, window);

    public IDispatcher CreateDispatcher(nint windowHandle) => new Win32Dispatcher(windowHandle);

    public uint GetSystemDpi() => Win32DpiApiResolver.GetSystemDpi();

    public ThemeVariant GetSystemThemeVariant() => GetSystemThemeVariantFromRegistry();

    public uint GetDpiForWindow(nint hwnd) => Win32DpiApiResolver.GetDpiForWindow(hwnd);

    public bool EnablePerMonitorDpiAwareness() => Win32DpiApiResolver.EnablePerMonitorDpiAwareness();

    public int GetSystemMetricsForDpi(int nIndex, uint dpi) => Win32DpiApiResolver.GetSystemMetricsForDpi(nIndex, dpi);

    private static ThemeVariant GetSystemThemeVariantFromRegistry()
    {
        try
        {
            // Windows app theme (Light/Dark) is commonly exposed via registry.
            // 1 = light, 0 = dark
            // HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme (DWORD)
#pragma warning disable CA1416 // Validate platform compatibility
            object? v = Registry.GetValue(
                @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme",
                null);
#pragma warning restore CA1416 // Validate platform compatibility

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
        // The shutdown decision is owned by Application.UnregisterWindow (ShutdownMode-aware); the
        // host only drops its routing entry here.
        _windows.Remove(hwnd);
    }

    public void Run(Application app, Window? mainWindow)
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
            app.OnHostLoopStarting(mainWindow);

            PumpLoop(null);
        }
        finally
        {
            Shutdown(app);
        }
    }

    /// <summary>
    /// Runs the message/render loop until the app quits and, when <paramref name="keepRunning"/> is supplied,
    /// until it returns false. Shared by <see cref="Run"/> and <see cref="RunNestedLoop"/>.
    /// </summary>
    private void PumpLoop(Func<bool>? keepRunning)
    {
        var app = _app!;
        var scheduler = app.RenderLoopSettings;
        long ticksPerSecond = Stopwatch.Frequency;
        long lastFrameTicks = Stopwatch.GetTimestamp();

        while (_running && (keepRunning == null || keepRunning()))
        {
            if (scheduler.IsContinuous)
            {
                try
                {
                    ProcessMessages();
                }
                catch (Exception ex)
                {
                    if (!HandleLoopException(app, ex)) break;
                }
                if (!_running) break;

                try
                {
                    RenderContinuousWindows(scheduler);
                }
                catch (Exception ex)
                {
                    if (!HandleLoopException(app, ex)) break;
                }

                int fps = scheduler.EffectiveFrameCap(app.GraphicsFactory.SupportsVSync, GetDisplayRefreshHz());
                if (fps > 0)
                {
                    long frameTicks = ticksPerSecond / fps;
                    WaitForFrameBudget(lastFrameTicks + frameTicks, ticksPerSecond);

                    // Advance the deadline instead of restarting from now, so a frame that overran is not
                    // paid for twice. A deadline further behind than one frame is abandoned rather than
                    // chased, which would burst frames after a stall.
                    lastFrameTicks += frameTicks;
                    long lag = Stopwatch.GetTimestamp() - lastFrameTicks;
                    if (lag > frameTicks)
                    {
                        lastFrameTicks = Stopwatch.GetTimestamp();
                    }
                }
                else
                {
                    WaitForMessagesOrRender(0, renderOnSignal: false);
                }
            }
            else
            {
                WaitForMessagesOrRender(0xFFFFFFFF, renderOnSignal: true);

                try
                {
                    ProcessMessages();
                }
                catch (Exception ex)
                {
                    if (!HandleLoopException(app, ex)) break;
                }

                if (_running && AnyWindowNeedsRender())
                {
                    RequestRender();
                }
            }
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

    /// <summary>
    /// Returns true if the exception was handled and the loop can continue.
    /// Returns false if fatal - caller should break.
    /// </summary>
    private bool HandleLoopException(Application app, Exception ex)
    {
        if (app.TryHandleDispatcherException(ex))
            return true;

        app.NotifyFatalDispatcherException(ex);
        _running = false;
        User32.PostQuitMessage(0);
        return false;
    }

    public void Quit(Application app)
    {
        var dispatcher = _dispatcher;
        if (dispatcher != null && !dispatcher.IsOnUIThread)
        {
            // PostQuitMessage targets the calling thread's own queue, and the loop can be parked in an
            // infinite wait, so the request is marshalled instead of being posted from here.
            dispatcher.BeginInvoke(() => Quit(app));
            return;
        }

        _running = false;
        User32.PostQuitMessage(0);
    }

    public Point GetCursorScreenPosition()
    {
        return User32.GetCursorPos(out var pt) ? new Point(pt.x, pt.y) : default;
    }

    public uint GetDpiForPoint(Point screenPositionPx)
    {
        const uint MonitorDefaultToNearest = 2;
        var point = new POINT((int)Math.Round(screenPositionPx.X), (int)Math.Round(screenPositionPx.Y));
        var monitor = User32.MonitorFromPoint(point, MonitorDefaultToNearest);
        if (monitor != 0)
        {
            uint dpi = Win32DpiApiResolver.GetDpiForMonitor(monitor);
            if (dpi > 0)
            {
                return dpi;
            }
        }
        return GetSystemDpi();
    }

    public Rect GetWorkAreaForPoint(Point screenPositionPx)
    {
        const uint MonitorDefaultToNearest = 2;
        var point = new POINT((int)Math.Round(screenPositionPx.X), (int)Math.Round(screenPositionPx.Y));
        var monitor = User32.MonitorFromPoint(point, MonitorDefaultToNearest);
        if (monitor != 0)
        {
            var info = MONITORINFO.Create();
            if (User32.GetMonitorInfo(monitor, ref info))
            {
                var work = info.rcWork;
                return new Rect(work.left, work.top, work.Width, work.Height);
            }
        }
        return default;
    }

    // WS_EX_TRANSPARENT | WS_EX_LAYERED | WS_EX_NOACTIVATE provide a click-through, non-activating overlay.
    public bool SupportsTransparentOverlay => true;

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
                if (!Application.IsRunning || !Application.Current.TryHandleDispatcherException(ex))
                {
                    if (Application.IsRunning)
                        Application.Current.NotifyFatalDispatcherException(ex);
                    _running = false;
                    User32.PostQuitMessage(0);
                    break;
                }
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
        wndClass.hIcon = User32.LoadIcon(_moduleHandle, 0);
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
                case Win32Dispatcher.WM_INVOKE:
                    _dispatcher?.ClearInvokeRequest();
                    _dispatcher?.ProcessWorkItems();
                    if (_dispatcher?.HasPendingWork == true)
                    {
                        _dispatcher?.BeginInvoke(DispatcherPriority.Background, () =>
                        {
                        });
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
                if (!Application.Current.TryHandleDispatcherException(ex))
                {
                    Application.Current.NotifyFatalDispatcherException(ex);
                    _running = false;
                    User32.PostQuitMessage(0);
                }
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

    private void RenderContinuousWindows(RenderLoopSettings settings)
    {
        using var pulse = AnimationManager.Instance.BeginPulse(settings);

        _renderBackends.Clear();
        foreach (var backend in _windows.Values)
        {
            if (pulse.ShouldRender(backend.Window, backend.NeedsRender))
            {
                _renderBackends.Add(backend);
            }
        }

        for (int i = 0; i < _renderBackends.Count; i++)
        {
            _renderBackends[i].RenderNow();
        }
    }

    /// <summary>
    /// Waits out the remainder of a frame budget. A high-resolution waitable timer keeps the wait accurate
    /// to well under a millisecond; where the system has none the wait falls back to the millisecond
    /// timeout, whose ~15.6 ms granularity is the historical behaviour.
    /// </summary>
    private void WaitForFrameBudget(long deadlineTicks, long ticksPerSecond)
    {
        nint timer = EnsureFrameBudgetTimer();

        // A pending render request must not cut the budget short: invalidation signals the render event on
        // every animation tick, so waiting on it here would drop the cap entirely. Input still wakes the
        // wait, and the loop re-enters until the deadline passes.
        while (_running)
        {
            long remaining = deadlineTicks - Stopwatch.GetTimestamp();
            if (remaining <= 0)
            {
                return;
            }

            uint result;
            uint messageResult;
            if (timer == 0)
            {
                long waitMs = remaining * 1000 / ticksPerSecond;
                result = WaitWithoutRenderEvent(waitMs > 0 ? (uint)waitMs : 1, 0);
                messageResult = WaitConstants.WAIT_OBJECT_0;
            }
            else
            {
                // Negative due time is relative, in 100 ns units.
                long dueTime = -(remaining * 10_000_000 / ticksPerSecond);
                if (dueTime >= 0 || !Kernel32.SetWaitableTimer(timer, in dueTime, 0, 0, 0, false))
                {
                    return;
                }

                result = WaitWithoutRenderEvent(0xFFFFFFFF, timer);
                Kernel32.CancelWaitableTimer(timer);
                messageResult = WaitConstants.WAIT_OBJECT_0 + 1;
            }

            if (result == messageResult)
            {
                // MWMO_INPUTAVAILABLE returns for a message that was already seen, so leaving the queue
                // untouched would wake this wait again at once and spend the rest of the budget spinning.
                // The dispatcher posts one wake message per animated frame, which is exactly that case.
                ProcessMessages();
            }
        }
    }

    private static uint WaitWithoutRenderEvent(uint timeoutMs, nint extraHandle)
    {
        unsafe
        {
            if (extraHandle == 0)
            {
                return User32.MsgWaitForMultipleObjectsEx(0, null, timeoutMs, WaitConstants.QS_ALLINPUT, WaitConstants.MWMO_INPUTAVAILABLE);
            }

            nint* handles = stackalloc nint[1];
            handles[0] = extraHandle;
            return User32.MsgWaitForMultipleObjectsEx(1, handles, timeoutMs, WaitConstants.QS_ALLINPUT, WaitConstants.MWMO_INPUTAVAILABLE);
        }
    }

    /// <summary>
    /// Returns the refresh rate the loop should hold itself to: the fastest among the monitors currently
    /// hosting a window, so a window on a 144 Hz screen is not capped to a 60 Hz primary. Falls back to
    /// the primary monitor, and to 0 when no rate is reported.
    /// </summary>
    private unsafe int GetDisplayRefreshHz()
    {
        long now = Stopwatch.GetTimestamp();
        if (_displayRefreshHz != 0 && now - _displayRefreshReadAt < Stopwatch.Frequency * REFRESH_RECHECK_SECONDS)
        {
            return Math.Max(0, _displayRefreshHz);
        }

        _displayRefreshReadAt = now;
        int best = 0;
        foreach (var backend in _windows.Values)
        {
            if (backend.Handle == 0)
            {
                continue;
            }

            var monitor = User32.MonitorFromWindow(backend.Handle, MONITOR_DEFAULTTONEAREST);
            var info = MONITORINFOEX.Create();
            if (monitor == 0 || !User32.GetMonitorInfoEx(monitor, ref info))
            {
                continue;
            }

            string device = new string(info.szDevice, 0, 32).TrimEnd('\0');
            best = Math.Max(best, ReadRefreshHz(device));
        }

        if (best == 0)
        {
            best = ReadRefreshHz(null);
        }

        _displayRefreshHz = best > 0 ? best : -1;
        return Math.Max(0, _displayRefreshHz);
    }

    /// <summary>Reads one display device's refresh rate, or the primary monitor's when the name is null.</summary>
    private static int ReadRefreshHz(string? deviceName)
    {
        nint hdc = deviceName == null ? User32.GetDC(0) : Gdi32.CreateDC(deviceName, deviceName, null, 0);
        if (hdc == 0)
        {
            return 0;
        }

        try
        {
            // 0 and 1 both mean "hardware default" rather than a rate.
            int refresh = Gdi32.GetDeviceCaps(hdc, Gdi32.VREFRESH);
            return refresh > 1 ? refresh : 0;
        }
        finally
        {
            if (deviceName == null)
            {
                User32.ReleaseDC(0, hdc);
            }
            else
            {
                Gdi32.DeleteDC(hdc);
            }
        }
    }

    private nint EnsureFrameBudgetTimer()
    {
        if (_frameBudgetTimerResolved)
        {
            return _frameBudgetTimer;
        }

        _frameBudgetTimerResolved = true;
        _frameBudgetTimer = Kernel32.CreateWaitableTimerEx(
            0,
            null,
            Kernel32.CREATE_WAITABLE_TIMER_MANUAL_RESET | Kernel32.CREATE_WAITABLE_TIMER_HIGH_RESOLUTION,
            Kernel32.TIMER_ALL_ACCESS);

        if (_frameBudgetTimer == 0)
        {
            DiagLog.Write("[win32] high-resolution frame timer unavailable; frame pacing uses millisecond waits");
        }

        return _frameBudgetTimer;
    }

    private void WaitForMessagesOrRender(uint timeoutMs, bool renderOnSignal)
        => WaitForMessagesOrRender(timeoutMs, renderOnSignal, extraHandle: 0);

    private void WaitForMessagesOrRender(uint timeoutMs, bool renderOnSignal, nint extraHandle)
    {
        if (_renderEvent == 0 && extraHandle == 0)
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
            nint* handles = stackalloc nint[2];
            uint count = 0;
            uint renderIndex = uint.MaxValue;
            if (_renderEvent != 0)
            {
                renderIndex = count;
                handles[count++] = _renderEvent;
            }
            if (extraHandle != 0)
            {
                handles[count++] = extraHandle;
            }

            uint result = User32.MsgWaitForMultipleObjectsEx(
                count,
                handles,
                timeoutMs,
                WaitConstants.QS_ALLINPUT,
                WaitConstants.MWMO_INPUTAVAILABLE);

            if (result == WaitConstants.WAIT_OBJECT_0 + renderIndex)
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

        _dispatcher = new Win32Dispatcher(_dispatcherHwnd);
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

        if (_frameBudgetTimer != 0)
        {
            Kernel32.CloseHandle(_frameBudgetTimer);
            _frameBudgetTimer = 0;
        }
        _frameBudgetTimerResolved = false;

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
