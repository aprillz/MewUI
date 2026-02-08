using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using Aprillz.MewUI.Native;

using NativeX11 = Aprillz.MewUI.Native.X11;

namespace Aprillz.MewUI.Platform.Linux.X11;

/// <summary>
/// Experimental Linux (X11) platform host.
/// </summary>
public sealed class X11PlatformHost : IPlatformHost
{
    private readonly Dictionary<nint, X11WindowBackend> _windows = new();
    private readonly List<X11WindowBackend> _renderBackends = new(capacity: 8);
    private bool _running;
    private Application? _app;
    private uint _systemDpi = 96u;
    private ThemeVariant _lastSystemTheme = ThemeVariant.Light;
    private long _lastThemePollTick;
    private int _systemThemeDirty;
    private Aprillz.MewUI.Platform.Linux.LinuxGSettingsMonitor? _gsettingsThemeMonitor;
    private LinuxUiDispatcher? _dispatcher;
    private long _lastDpiPollTick;
    private nint _resourceManagerAtom;
    private nint _xsettingsAtom;
    private nint _xsettingsSelectionAtom;
    private nint _xsettingsOwnerWindow;
    private nint _rootWindow;

    private int _xConnectionFd = -1;
    private int _wakeReadFd = -1;
    private int _wakeWriteFd = -1;
    private bool _usePollWait;

    public IMessageBoxService MessageBox { get; } = new X11MessageBoxService();

    public IFileDialogService FileDialog { get; } = new X11FileDialogService();

    public IClipboardService Clipboard { get; } = new LinuxClipboardService();

    public IWindowBackend CreateWindowBackend(Window window) => new X11WindowBackend(this, window);

    public IUiDispatcher CreateDispatcher(nint windowHandle) => new LinuxUiDispatcher();

    public uint GetSystemDpi()
    {
        EnsureDisplay();
        return _systemDpi;
    }

    public ThemeVariant GetSystemThemeVariant()
    {
        EnsureDisplay();
        return DetectSystemThemeVariant();
    }

    public uint GetDpiForWindow(nint hwnd) => GetSystemDpi();

    public bool EnablePerMonitorDpiAwareness() => false;

    public int GetSystemMetricsForDpi(int nIndex, uint dpi) => 0;

    internal void RegisterWindow(nint window, X11WindowBackend backend) => _windows[window] = backend;

    internal void UnregisterWindow(nint window)
    {
        _windows.Remove(window);
        if (_windows.Count == 0)
        {
            _running = false;
        }
    }

    public void Run(Application app, Window mainWindow)
    {
        _running = true;
        _app = app;

        EnsureDisplay();

        var previousContext = SynchronizationContext.Current;
        var dispatcher = CreateDispatcher(0);
        _dispatcher = dispatcher as LinuxUiDispatcher;
        app.Dispatcher = dispatcher;
        SynchronizationContext.SetSynchronizationContext(dispatcher as SynchronizationContext);
        _dispatcher?.SetWake(SignalWake);

        // GNOME/Wayland doesn't provide X11 notifications for theme changes.
        // Best-effort: listen to gsettings notifications and wake the loop.
        if (_gsettingsThemeMonitor == null)
        {
            _gsettingsThemeMonitor = new Aprillz.MewUI.Platform.Linux.LinuxGSettingsMonitor("org.gnome.desktop.interface");
            _gsettingsThemeMonitor.Start(() =>
            {
                Interlocked.Exchange(ref _systemThemeDirty, 1);
                SignalWake();
            });
        }

        // Show after dispatcher is ready so timers/postbacks work immediately (WPF-style dispatcher lifetime).
        mainWindow.Show();

        // Very simple single-display loop (from the main window).
        if (!_windows.TryGetValue(mainWindow.Handle, out var mainBackend))
        {
            throw new InvalidOperationException("X11 main window backend not registered.");
        }

        nint display = Display;
        var scheduler = app.RenderLoopSettings;
        long ticksPerSecond = Stopwatch.Frequency;
        long lastFrameTicks = Stopwatch.GetTimestamp();

        while (_running)
        {
            try
            {
                // Drain pending events
                while (_running && NativeX11.XPending(display) != 0)
                {
                    NativeX11.XNextEvent(display, out var ev);
                    if (ev.type == 28) // PropertyNotify
                    {
                        HandlePropertyNotify(ev.xproperty);
                        continue;
                    }
                    var window = GetEventWindow(ev);
                    if (window != 0 && _windows.TryGetValue(window, out var backend))
                    {
                        backend.ProcessEvent(ev);
                    }
                }

                PollDpiChanges();
                if (Interlocked.Exchange(ref _systemThemeDirty, 0) != 0)
                {
                    TryUpdateSystemTheme(force: true);
                }
                else
                {
                    TryUpdateSystemTheme();
                }

                _dispatcher?.ProcessWorkItems();

                if (scheduler.Mode == RenderLoopMode.Continuous)
                {
                    RenderAllWindows();

                    int fps = scheduler.TargetFps;
                    if (fps > 0)
                    {
                        long frameTicks = ticksPerSecond / fps;
                        long now = Stopwatch.GetTimestamp();
                        long elapsed = now - lastFrameTicks;
                        if (elapsed < frameTicks)
                        {
                            int waitMs = (int)((frameTicks - elapsed) * 1000 / ticksPerSecond);
                            if (waitMs > 0)
                            {
                                WaitForWorkOrEvents(timeoutOverrideMs: waitMs, ignoreRenderRequests: true);
                            }
                        }
                        lastFrameTicks = Stopwatch.GetTimestamp();
                    }
                    else
                    {
                        WaitForWorkOrEvents(timeoutOverrideMs: 0, ignoreRenderRequests: true);
                    }
                }
                else
                {
                    // Coalesced rendering for all windows.
                    RenderInvalidatedWindows();
                }
            }
            catch (Exception ex)
            {
                // Dispatcher-level handling is performed by the dispatcher queue.
                app.NotifyFatalDispatcherException(ex);
                _running = false;
                break;
            }

            if (scheduler.Mode == RenderLoopMode.Continuous)
            {
                continue;
            }

            WaitForWorkOrEvents();
        }

        if (Display != 0)
        {
            try { NativeX11.XCloseDisplay(Display); } catch { }
            Display = 0;
        }

        CloseWakePipe();
        _gsettingsThemeMonitor?.Dispose();
        _gsettingsThemeMonitor = null;
        _dispatcher = null;
        _app = null;
        app.Dispatcher = null;
        SynchronizationContext.SetSynchronizationContext(previousContext);
    }

    internal void RequestWake()
        => SignalWake();

    private static nint GetEventWindow(in XEvent ev)
    {
        // Xlib event types
        const int KeyPress = 2;
        const int KeyRelease = 3;
        const int ButtonPress = 4;
        const int ButtonRelease = 5;
        const int MotionNotify = 6;
        const int DestroyNotify = 17;
        const int Expose = 12;
        const int ConfigureNotify = 22;
        const int ClientMessage = 33;
        const int PropertyNotify = 28;

        return ev.type switch
        {
            KeyPress or KeyRelease => ev.xkey.window,
            ButtonPress or ButtonRelease => ev.xbutton.window,
            MotionNotify => ev.xmotion.window,
            DestroyNotify => ev.xdestroywindow.window,
            Expose => ev.xexpose.window,
            ConfigureNotify => ev.xconfigure.window,
            ClientMessage => ev.xclient.window,
            PropertyNotify => ev.xproperty.window,
            _ => 0
        };
    }

    public void Quit(Application app) => _running = false;

    public void DoEvents()
    {
        if (Display == 0)
        {
            return;
        }

        while (NativeX11.XPending(Display) != 0)
        {
            NativeX11.XNextEvent(Display, out var ev);
            if (ev.type == 28) // PropertyNotify
            {
                HandlePropertyNotify(ev.xproperty);
                continue;
            }
            var window = GetEventWindow(ev);
            if (window != 0 && _windows.TryGetValue(window, out var backend))
            {
                backend.ProcessEvent(ev);
            }
        }

        PollDpiChanges();

        _dispatcher?.ProcessWorkItems();

        RenderInvalidatedWindows();
    }

    public void Dispose()
    {
        foreach (var backend in _windows.Values.ToArray())
        {
            backend.Dispose();
        }

        _windows.Clear();

        if (Display != 0)
        {
            try { NativeX11.XCloseDisplay(Display); } catch { }
            Display = 0;
        }

        CloseWakePipe();
    }

    internal nint Display { get; private set; }

    internal void EnsureDisplay()
    {
        if (Display != 0)
        {
            return;
        }

        Display = NativeX11.XOpenDisplay(0);
        if (Display == 0)
        {
            throw new InvalidOperationException("XOpenDisplay failed.");
        }

        int screen = NativeX11.XDefaultScreen(Display);
        _rootWindow = NativeX11.XRootWindow(Display, screen);

        _resourceManagerAtom = NativeX11.XInternAtom(Display, "RESOURCE_MANAGER", false);
        _xsettingsAtom = NativeX11.XInternAtom(Display, "_XSETTINGS_SETTINGS", false);
        _xsettingsSelectionAtom = NativeX11.XInternAtom(Display, "_XSETTINGS_S0", false);
        UpdateXsettingsOwner();

        // Listen for root property changes (RESOURCE_MANAGER / XSETTINGS) to refresh DPI.
        if (_rootWindow != 0)
        {
            NativeX11.XSelectInput(Display, _rootWindow, (nint)X11EventMask.PropertyChangeMask);
        }

        _systemDpi = TryGetXSettingsDpi(Display, _xsettingsOwnerWindow, _xsettingsAtom)
            ?? TryGetXftDpi(Display)
            ?? 96u;
        _lastDpiPollTick = Environment.TickCount64;

        _lastSystemTheme = DetectSystemThemeVariant();
        _lastThemePollTick = Environment.TickCount64;

        SetupWakePipe();
    }

    private void SetupWakePipe()
    {
        CloseWakePipe();

        if (Display == 0)
        {
            return;
        }

        int xfd;
        try
        {
            xfd = NativeX11.XConnectionNumber(Display);
        }
        catch
        {
            xfd = -1;
        }

        if (xfd <= 0)
        {
            _usePollWait = false;
            _xConnectionFd = -1;
            return;
        }

        unsafe
        {
            int* fds = stackalloc int[2];
            if (LibC.Pipe(fds) != 0)
            {
                _usePollWait = false;
                _xConnectionFd = -1;
                return;
            }

            _wakeReadFd = fds[0];
            _wakeWriteFd = fds[1];
        }

        _xConnectionFd = xfd;
        _usePollWait = true;
    }

    private void CloseWakePipe()
    {
        var readFd = Interlocked.Exchange(ref _wakeReadFd, -1);
        if (readFd >= 0)
        {
            try { LibC.Close(readFd); } catch { }
        }

        var writeFd = Interlocked.Exchange(ref _wakeWriteFd, -1);
        if (writeFd >= 0)
        {
            try { LibC.Close(writeFd); } catch { }
        }

        _xConnectionFd = -1;
        _usePollWait = false;
    }

    private void SignalWake()
    {
        int fd = _wakeWriteFd;
        if (fd < 0)
        {
            return;
        }

        unsafe
        {
            byte b = 1;
            _ = LibC.Write(fd, &b, 1);
        }
    }

    private void DrainWakePipe()
    {
        int fd = _wakeReadFd;
        if (fd < 0)
        {
            return;
        }

        unsafe
        {
            byte* buf = stackalloc byte[64];
            _ = LibC.Read(fd, buf, 64);
        }

        _dispatcher?.ClearWakeRequest();
    }

    private bool AnyWindowNeedsRender()
    {
        foreach (var backend in _windows.Values)
        {
            if (backend.NeedsRender && backend.Display != 0 && backend.Handle != 0)
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
            if (backend.NeedsRender && backend.Display != 0 && backend.Handle != 0)
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
            if (backend.Display != 0 && backend.Handle != 0)
            {
                _renderBackends.Add(backend);
            }
        }

        for (int i = 0; i < _renderBackends.Count; i++)
        {
            _renderBackends[i].RenderNow();
        }
    }

    private void WaitForWorkOrEvents(int? timeoutOverrideMs = null, bool ignoreRenderRequests = false)
    {
        if (Display == 0)
        {
            Thread.Sleep(1);
            return;
        }

        if (NativeX11.XPending(Display) != 0)
        {
            return;
        }

        if (!ignoreRenderRequests && AnyWindowNeedsRender())
        {
            // Avoid a busy loop when a backend throttles rendering (e.g. software/VM).
            // If the next render time is in the future, block in poll for that duration.
            int minDelayMs = int.MaxValue;
            foreach (var backend in _windows.Values)
            {
                if (!backend.NeedsRender)
                {
                    continue;
                }

                minDelayMs = Math.Min(minDelayMs, backend.GetRenderDelayMs());
                if (minDelayMs == 0)
                {
                    break;
                }
            }

            if (minDelayMs <= 0)
            {
                return;
            }

            timeoutOverrideMs = timeoutOverrideMs != null
                ? Math.Min(timeoutOverrideMs.Value, minDelayMs)
                : minDelayMs;

            ignoreRenderRequests = true;
        }

        var dispatcher = _dispatcher;
        if (dispatcher != null && dispatcher.HasPendingWork)
        {
            // Work can be posted while rendering/input handling. If we simply return here,
            // the platform loop can become a busy loop. Pump a bounded amount of dispatcher
            // work before deciding whether we still need to spin.
            const int MaxPumps = 8;
            for (int i = 0; i < MaxPumps; i++)
            {
                dispatcher.ProcessWorkItems();
                if (!dispatcher.HasPendingWork)
                {
                    break;
                }
            }

            if (dispatcher.HasPendingWork)
            {
                return;
            }
        }

        // If poll-based waiting isn't available, fall back to a modest sleep to avoid a busy loop.
        if (!_usePollWait || _xConnectionFd < 0 || _wakeReadFd < 0)
        {
            Thread.Sleep(8);
            return;
        }

        // Keep a finite timeout so DPI polling can still occur even if the DE doesn't notify (best-effort).
        const int MaxWaitMs = 500;
        int timeoutMs = timeoutOverrideMs ?? (dispatcher?.GetPollTimeoutMs(MaxWaitMs) ?? MaxWaitMs);

        bool wakeSignaled = false;
        unsafe
        {
            PollFd* fds = stackalloc PollFd[2];
            fds[0] = new PollFd { fd = _xConnectionFd, events = PollEvents.POLLIN };
            fds[1] = new PollFd { fd = _wakeReadFd, events = PollEvents.POLLIN };

            _ = LibC.Poll(fds, 2, timeoutMs);
            wakeSignaled = (fds[1].revents & PollEvents.POLLIN) != 0;
        }

        if (wakeSignaled)
        {
            DrainWakePipe();
        }
    }

    private static uint? TryGetXftDpi(nint display)
    {
        try
        {
            // Xft.dpi from Xresources (XResourceManagerString + XrmGetResource)
            NativeX11.XrmInitialize();
            nint resourceString = NativeX11.XResourceManagerString(display);
            if (resourceString == 0)
            {
                return null;
            }

            nint db = NativeX11.XrmGetStringDatabase(resourceString);
            if (db == 0)
            {
                return null;
            }

            try
            {
                if (NativeX11.XrmGetResource(db, "Xft.dpi", "Xft.Dpi", out _, out var value) == 0)
                {
                    return null;
                }

                if (value.addr == 0)
                {
                    return null;
                }

                string? dpiText = Marshal.PtrToStringUTF8(value.addr);
                if (string.IsNullOrWhiteSpace(dpiText))
                {
                    return null;
                }

                if (!double.TryParse(dpiText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var dpi))
                {
                    return null;
                }

                if (dpi <= 0)
                {
                    return null;
                }

                return (uint)Math.Clamp((int)Math.Round(dpi), 48, 480);
            }
            finally
            {
                NativeX11.XrmDestroyDatabase(db);
            }
        }
        catch
        {
            return null;
        }
    }

    private void PollDpiChanges(bool force = false)
    {
        // Best-effort: X11 doesn't provide a universal per-monitor DPI signal.
        // Poll Xft.dpi and broadcast when it changes.
        const int PollIntervalMs = 500;

        long now = Environment.TickCount64;
        if (!force && now - _lastDpiPollTick < PollIntervalMs)
        {
            return;
        }

        _lastDpiPollTick = now;

        uint? dpi = TryGetXSettingsDpi(Display, _xsettingsOwnerWindow, _xsettingsAtom);
        dpi ??= TryGetXftDpi(Display);
        if (dpi == null || dpi.Value == _systemDpi)
        {
            return;
        }

        uint old = _systemDpi;
        _systemDpi = dpi.Value;

        foreach (var backend in _windows.Values.ToArray())
        {
            backend.NotifyDpiChanged(old, _systemDpi);
        }
    }

    private void TryUpdateSystemTheme(bool force = false)
    {
        // X11 doesn't have a single universal theme API. We do best-effort detection by reading
        // XSettings (Gtk/ThemeName, Net/ThemeName) and GTK_THEME env override.
        const int PollIntervalMs = 500;

        var app = _app;
        if (app == null || app.ThemeMode != ThemeVariant.System)
        {
            return;
        }

        long now = Environment.TickCount64;
        if (!force && now - _lastThemePollTick < PollIntervalMs)
        {
            return;
        }

        _lastThemePollTick = now;

        var current = DetectSystemThemeVariant();
        if (current == _lastSystemTheme)
        {
            return;
        }

        _lastSystemTheme = current;
        app.NotifySystemThemeChanged();
    }

    private void HandlePropertyNotify(in XPropertyEvent e)
    {
        if (e.window == _rootWindow)
        {
            if (_resourceManagerAtom != 0 && e.atom == _resourceManagerAtom)
            {
                PollDpiChanges(force: true);
            }

            // Selection owner can change when the DE restarts; refresh.
            if (_xsettingsSelectionAtom != 0)
            {
                UpdateXsettingsOwner();
                PollDpiChanges(force: true);
                TryUpdateSystemTheme(force: true);
            }
            return;
        }

        if (_xsettingsOwnerWindow != 0 &&
            e.window == _xsettingsOwnerWindow &&
            _xsettingsAtom != 0 &&
            e.atom == _xsettingsAtom)
        {
            PollDpiChanges(force: true);
            TryUpdateSystemTheme(force: true);
        }
    }

    private void UpdateXsettingsOwner()
    {
        if (Display == 0 || _xsettingsSelectionAtom == 0)
        {
            return;
        }

        var owner = NativeX11.XGetSelectionOwner(Display, _xsettingsSelectionAtom);
        if (owner == _xsettingsOwnerWindow)
        {
            return;
        }

        _xsettingsOwnerWindow = owner;
        if (_xsettingsOwnerWindow != 0)
        {
            // Subscribe to owner property changes for _XSETTINGS_SETTINGS.
            NativeX11.XSelectInput(Display, _xsettingsOwnerWindow, (nint)X11EventMask.PropertyChangeMask);
        }
    }

    private static uint? TryGetXSettingsDpi(nint display, nint xsettingsOwnerWindow, nint xsettingsSettingsAtom)
    {
        if (display == 0 || xsettingsOwnerWindow == 0 || xsettingsSettingsAtom == 0)
        {
            return null;
        }

        const nint AnyPropertyType = 0;

        int status = NativeX11.XGetWindowProperty(
            display,
            xsettingsOwnerWindow,
            xsettingsSettingsAtom,
            long_offset: 0,
            long_length: 64 * 1024, // long_length is in 32-bit chunks; but Xlib treats it as long count. Keep large.
            delete: false,
            req_type: AnyPropertyType,
            out _,
            out int actualFormat,
            out nuint nitems,
            out _,
            out nint prop);

        if (status != 0 || prop == 0 || actualFormat != 8 || nitems == 0)
        {
            if (prop != 0)
            {
                NativeX11.XFree(prop);
            }

            return null;
        }

        try
        {
            int len = checked((int)nitems);
            unsafe
            {
                var bytes = new ReadOnlySpan<byte>((void*)prop, len);
                return ParseXSettingsDpi(bytes);
            }
        }
        finally
        {
            NativeX11.XFree(prop);
        }
    }

    private ThemeVariant DetectSystemThemeVariant()
    {
        // 1) Environment override (GTK_THEME=Adwaita:dark etc).
        var gtkTheme = Environment.GetEnvironmentVariable("GTK_THEME");
        if (!string.IsNullOrWhiteSpace(gtkTheme) && ContainsDarkKeyword(gtkTheme))
        {
            return ThemeVariant.Dark;
        }

        // 2) XSettings: prefer-dark flag (GNOME/GTK commonly sets this even when theme name doesn't contain "dark").
        // Key: Gtk/ApplicationPreferDarkTheme (int; 0 or 1).
        int? preferDark = TryGetXSettingsInt(Display, _xsettingsOwnerWindow, _xsettingsAtom, "Gtk/ApplicationPreferDarkTheme");
        if (preferDark is > 0)
        {
            return ThemeVariant.Dark;
        }

        // 3) XSettings: theme name (Gtk/ThemeName or Net/ThemeName) when available.
        string? themeName = TryGetXSettingsString(Display, _xsettingsOwnerWindow, _xsettingsAtom, "Gtk/ThemeName")
            ?? TryGetXSettingsString(Display, _xsettingsOwnerWindow, _xsettingsAtom, "Net/ThemeName");

        if (!string.IsNullOrWhiteSpace(themeName) && ContainsDarkKeyword(themeName))
        {
            return ThemeVariant.Dark;
        }

        // 4) Fallback to config-file heuristics (GTK settings.ini / KDE kdeglobals).
        return LinuxThemeDetector.DetectSystemThemeVariant();
    }

    private static bool ContainsDarkKeyword(string value)
        => value.Contains("dark", StringComparison.OrdinalIgnoreCase) ||
           value.Contains(":dark", StringComparison.OrdinalIgnoreCase) ||
           value.EndsWith("-dark", StringComparison.OrdinalIgnoreCase);

    private static string? TryGetXSettingsString(nint display, nint xsettingsOwnerWindow, nint xsettingsSettingsAtom, string key)
    {
        if (display == 0 || xsettingsOwnerWindow == 0 || xsettingsSettingsAtom == 0)
        {
            return null;
        }

        const nint AnyPropertyType = 0;
        int status = NativeX11.XGetWindowProperty(
            display,
            xsettingsOwnerWindow,
            xsettingsSettingsAtom,
            long_offset: 0,
            long_length: 64 * 1024,
            delete: false,
            req_type: AnyPropertyType,
            out _,
            out int actualFormat,
            out nuint nitems,
            out _,
            out nint prop);

        if (status != 0 || prop == 0 || actualFormat != 8 || nitems == 0)
        {
            if (prop != 0)
            {
                NativeX11.XFree(prop);
            }

            return null;
        }

        try
        {
            int len = checked((int)nitems);
            unsafe
            {
                var bytes = new ReadOnlySpan<byte>((void*)prop, len);
                return ParseXSettingsString(bytes, key);
            }
        }
        finally
        {
            NativeX11.XFree(prop);
        }
    }

    private static int? TryGetXSettingsInt(nint display, nint xsettingsOwnerWindow, nint xsettingsSettingsAtom, string key)
    {
        if (display == 0 || xsettingsOwnerWindow == 0 || xsettingsSettingsAtom == 0)
        {
            return null;
        }

        const nint AnyPropertyType = 0;
        int status = NativeX11.XGetWindowProperty(
            display,
            xsettingsOwnerWindow,
            xsettingsSettingsAtom,
            long_offset: 0,
            long_length: 64 * 1024,
            delete: false,
            req_type: AnyPropertyType,
            out _,
            out int actualFormat,
            out nuint nitems,
            out _,
            out nint prop);

        if (status != 0 || prop == 0 || actualFormat != 8 || nitems == 0)
        {
            if (prop != 0)
            {
                NativeX11.XFree(prop);
            }

            return null;
        }

        try
        {
            int len = checked((int)nitems);
            unsafe
            {
                var bytes = new ReadOnlySpan<byte>((void*)prop, len);
                return ParseXSettingsInt(bytes, key);
            }
        }
        finally
        {
            NativeX11.XFree(prop);
        }
    }

    private static string? ParseXSettingsString(ReadOnlySpan<byte> data, string key)
    {
        if (data.Length < 12)
        {
            return null;
        }

        bool littleEndian = data[0] switch
        {
            (byte)'l' => true,
            (byte)'B' => false,
            _ => BitConverter.IsLittleEndian
        };

        int offset = 4;
        _ = ReadUInt32(data, ref offset, littleEndian); // serial
        uint count = ReadUInt32(data, ref offset, littleEndian);

        for (uint i = 0; i < count; i++)
        {
            if (offset + 4 > data.Length)
            {
                return null;
            }

            byte type = data[offset++];
            offset++; // pad
            ushort nameLen = ReadUInt16(data, ref offset, littleEndian);

            if (offset + nameLen > data.Length)
            {
                return null;
            }

            string name = Encoding.ASCII.GetString(data.Slice(offset, nameLen));
            offset += nameLen;
            offset = Align4(offset);

            _ = ReadUInt32(data, ref offset, littleEndian); // last_change_serial

            if (type == 0)
            {
                // int
                _ = ReadInt32(data, ref offset, littleEndian);
                continue;
            }

            if (type == 2)
            {
                // color: 4 * uint16
                offset += 8;
                continue;
            }

            if (type != 1)
            {
                return null;
            }

            uint strLen = ReadUInt32(data, ref offset, littleEndian);
            if (offset + strLen > data.Length)
            {
                return null;
            }

            if (string.Equals(name, key, StringComparison.Ordinal))
            {
                var raw = data.Slice(offset, checked((int)strLen));
                var value = Encoding.UTF8.GetString(raw);
                return value;
            }

            offset += checked((int)strLen);
            offset = Align4(offset);
        }

        return null;
    }

    private static int? ParseXSettingsInt(ReadOnlySpan<byte> data, string key)
    {
        if (data.Length < 12)
        {
            return null;
        }

        bool littleEndian = data[0] switch
        {
            (byte)'l' => true,
            (byte)'B' => false,
            _ => BitConverter.IsLittleEndian
        };

        int offset = 4;
        _ = ReadUInt32(data, ref offset, littleEndian); // serial
        uint count = ReadUInt32(data, ref offset, littleEndian);

        for (uint i = 0; i < count; i++)
        {
            if (offset + 4 > data.Length)
            {
                return null;
            }

            byte type = data[offset++];
            offset++; // pad
            ushort nameLen = ReadUInt16(data, ref offset, littleEndian);

            if (offset + nameLen > data.Length)
            {
                return null;
            }

            string name = Encoding.ASCII.GetString(data.Slice(offset, nameLen));
            offset += nameLen;
            offset = Align4(offset);

            _ = ReadUInt32(data, ref offset, littleEndian); // last_change_serial

            if (type == 0)
            {
                int value = ReadInt32(data, ref offset, littleEndian);
                if (string.Equals(name, key, StringComparison.Ordinal))
                {
                    return value;
                }
                continue;
            }

            if (type == 1)
            {
                // string
                uint strLen = ReadUInt32(data, ref offset, littleEndian);
                offset += checked((int)strLen);
                offset = Align4(offset);
                continue;
            }

            if (type == 2)
            {
                // color: 4 * uint16
                offset += 8;
                continue;
            }

            return null;
        }

        return null;
    }

    private static uint? ParseXSettingsDpi(ReadOnlySpan<byte> data)
    {
        if (data.Length < 12)
        {
            return null;
        }

        // XSETTINGS wire format: byte order ('l' or 'B'), 3 pad bytes, uint32 serial, uint32 n_settings, then entries.
        bool littleEndian = data[0] switch
        {
            (byte)'l' => true,
            (byte)'B' => false,
            _ => BitConverter.IsLittleEndian
        };

        int offset = 4;
        _ = ReadUInt32(data, ref offset, littleEndian); // serial
        uint count = ReadUInt32(data, ref offset, littleEndian);

        for (uint i = 0; i < count; i++)
        {
            if (offset + 4 > data.Length)
            {
                return null;
            }

            byte type = data[offset++];
            offset++; // pad
            ushort nameLen = ReadUInt16(data, ref offset, littleEndian);

            if (offset + nameLen > data.Length)
            {
                return null;
            }

            string name = Encoding.ASCII.GetString(data.Slice(offset, nameLen));
            offset += nameLen;
            offset = Align4(offset);

            _ = ReadUInt32(data, ref offset, littleEndian); // last_change_serial

            // XSettings types: 0=int, 1=string, 2=color
            if (type == 0)
            {
                int value = ReadInt32(data, ref offset, littleEndian);
                if (string.Equals(name, "Xft/DPI", StringComparison.Ordinal))
                {
                    // Value is in 1/1024 DPI units.
                    double dpi = value / 1024.0;
                    if (dpi <= 0)
                    {
                        return null;
                    }

                    return (uint)Math.Clamp((int)Math.Round(dpi), 48, 480);
                }
            }
            else if (type == 1)
            {
                uint strLen = ReadUInt32(data, ref offset, littleEndian);
                offset += checked((int)strLen);
                offset = Align4(offset);
            }
            else if (type == 2)
            {
                // 4 * uint16
                offset += 8;
            }
            else
            {
                return null;
            }
        }

        return null;
    }

    private static int Align4(int offset) => (offset + 3) & ~3;

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, ref int offset, bool littleEndian)
    {
        if (offset + 2 > data.Length)
        {
            throw new IndexOutOfRangeException();
        }

        ushort v = littleEndian
            ? (ushort)(data[offset] | (data[offset + 1] << 8))
            : (ushort)((data[offset] << 8) | data[offset + 1]);
        offset += 2;
        return v;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, ref int offset, bool littleEndian)
    {
        if (offset + 4 > data.Length)
        {
            throw new IndexOutOfRangeException();
        }

        uint v = littleEndian
            ? (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24))
            : (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
        offset += 4;
        return v;
    }

    private static int ReadInt32(ReadOnlySpan<byte> data, ref int offset, bool littleEndian)
        => unchecked((int)ReadUInt32(data, ref offset, littleEndian));
}
