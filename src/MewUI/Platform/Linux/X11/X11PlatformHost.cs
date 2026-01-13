using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Core;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Linux;
using NativeX11 = Aprillz.MewUI.Native.X11;

namespace Aprillz.MewUI.Platform.Linux.X11;

/// <summary>
/// Experimental Linux (X11) platform host.
/// </summary>
public sealed class X11PlatformHost : IPlatformHost
{
    private readonly Dictionary<nint, X11WindowBackend> _windows = new();
    private readonly IMessageBoxService _messageBox = new X11MessageBoxService();
    private readonly IFileDialogService _fileDialog = new X11FileDialogService();
    private readonly IClipboardService _clipboard = new NoClipboardService();
    private bool _running;
    private nint _display;
    private uint _systemDpi = 96u;
    private LinuxUiDispatcher? _dispatcher;
    private long _lastDpiPollTick;
    private nint _resourceManagerAtom;
    private nint _xsettingsAtom;
    private nint _xsettingsSelectionAtom;
    private nint _xsettingsOwnerWindow;
    private nint _rootWindow;

    public IMessageBoxService MessageBox => _messageBox;

    public IFileDialogService FileDialog => _fileDialog;

    public IClipboardService Clipboard => _clipboard;

    public IWindowBackend CreateWindowBackend(Window window) => new X11WindowBackend(this, window);

    public IUiDispatcher CreateDispatcher(nint windowHandle) => new LinuxUiDispatcher();

    public uint GetSystemDpi()
    {
        EnsureDisplay();
        return _systemDpi;
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

        EnsureDisplay();

        var previousContext = SynchronizationContext.Current;
        var dispatcher = CreateDispatcher(0);
        _dispatcher = dispatcher as LinuxUiDispatcher;
        app.Dispatcher = dispatcher;
        SynchronizationContext.SetSynchronizationContext(dispatcher as SynchronizationContext);

        // Show after dispatcher is ready so timers/postbacks work immediately (WPF-style dispatcher lifetime).
        mainWindow.Show();

        // Very simple single-display loop (from the main window).
        if (!_windows.TryGetValue(mainWindow.Handle, out var mainBackend))
        {
            throw new InvalidOperationException("X11 main window backend not registered.");
        }

        nint display = _display;
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

                _dispatcher?.ProcessWorkItems();

                // Coalesced rendering for all windows.
                foreach (var backend in _windows.Values.ToArray())
                {
                    backend.RenderIfNeeded();
                }
            }
            catch (Exception ex)
            {
                if (Application.TryHandleUiException(ex))
                {
                    continue;
                }

                Application.NotifyFatalUiException(ex);
                _running = false;
                break;
            }
            Thread.Sleep(1);
        }

        if (_display != 0)
        {
            try { NativeX11.XCloseDisplay(_display); } catch { }
            _display = 0;
        }
        _dispatcher = null;
        app.Dispatcher = null;
        SynchronizationContext.SetSynchronizationContext(previousContext);
    }

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
        if (_display == 0)
        {
            return;
        }

        while (NativeX11.XPending(_display) != 0)
        {
            NativeX11.XNextEvent(_display, out var ev);
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

        foreach (var backend in _windows.Values.ToArray())
        {
            backend.RenderIfNeeded();
        }
    }

    public void Dispose()
    {
        foreach (var backend in _windows.Values.ToArray())
        {
            backend.Dispose();
        }

        _windows.Clear();

        if (_display != 0)
        {
            try { NativeX11.XCloseDisplay(_display); } catch { }
            _display = 0;
        }
    }

    internal nint Display => _display;

    internal void EnsureDisplay()
    {
        if (_display != 0)
        {
            return;
        }

        _display = NativeX11.XOpenDisplay(0);
        if (_display == 0)
        {
            throw new InvalidOperationException("XOpenDisplay failed.");
        }

        int screen = NativeX11.XDefaultScreen(_display);
        _rootWindow = NativeX11.XRootWindow(_display, screen);

        _resourceManagerAtom = NativeX11.XInternAtom(_display, "RESOURCE_MANAGER", false);
        _xsettingsAtom = NativeX11.XInternAtom(_display, "_XSETTINGS_SETTINGS", false);
        _xsettingsSelectionAtom = NativeX11.XInternAtom(_display, "_XSETTINGS_S0", false);
        UpdateXsettingsOwner();

        // Listen for root property changes (RESOURCE_MANAGER / XSETTINGS) to refresh DPI.
        if (_rootWindow != 0)
        {
            NativeX11.XSelectInput(_display, _rootWindow, (nint)X11EventMask.PropertyChangeMask);
        }

        _systemDpi = TryGetXSettingsDpi(_display, _xsettingsOwnerWindow, _xsettingsAtom)
            ?? TryGetXftDpi(_display)
            ?? 96u;
        _lastDpiPollTick = Environment.TickCount64;
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

        uint? dpi = TryGetXSettingsDpi(_display, _xsettingsOwnerWindow, _xsettingsAtom);
        dpi ??= TryGetXftDpi(_display);
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
            }
            return;
        }

        if (_xsettingsOwnerWindow != 0 &&
            e.window == _xsettingsOwnerWindow &&
            _xsettingsAtom != 0 &&
            e.atom == _xsettingsAtom)
        {
            PollDpiChanges(force: true);
        }
    }

    private void UpdateXsettingsOwner()
    {
        if (_display == 0 || _xsettingsSelectionAtom == 0)
        {
            return;
        }

        var owner = NativeX11.XGetSelectionOwner(_display, _xsettingsSelectionAtom);
        if (owner == _xsettingsOwnerWindow)
        {
            return;
        }

        _xsettingsOwnerWindow = owner;
        if (_xsettingsOwnerWindow != 0)
        {
            // Subscribe to owner property changes for _XSETTINGS_SETTINGS.
            NativeX11.XSelectInput(_display, _xsettingsOwnerWindow, (nint)X11EventMask.PropertyChangeMask);
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
