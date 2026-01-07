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
    private bool _running;
    private nint _display;

    public IMessageBoxService MessageBox => _messageBox;

    public IWindowBackend CreateWindowBackend(Window window) => new X11WindowBackend(this, window);

    public IUiDispatcher CreateDispatcher(nint windowHandle) => new LinuxUiDispatcher();

    internal void RegisterWindow(nint window, X11WindowBackend backend) => _windows[window] = backend;

    internal void UnregisterWindow(nint window)
    {
        _windows.Remove(window);
        if (_windows.Count == 0)
            _running = false;
    }

    public void Run(Application app, Window mainWindow)
    {
        _running = true;

        mainWindow.Show();

        var dispatcher = CreateDispatcher(mainWindow.Handle);
        app.Dispatcher = dispatcher;
        SynchronizationContext.SetSynchronizationContext(dispatcher as SynchronizationContext);

        // Very simple single-display loop (from the main window).
        if (!_windows.TryGetValue(mainWindow.Handle, out var mainBackend))
            throw new InvalidOperationException("X11 main window backend not registered.");

        nint display = mainBackend.Display;
        _display = display;
        while (_running)
        {
            try
            {
                // Drain pending events
                while (_running && NativeX11.XPending(display) != 0)
                {
                    NativeX11.XNextEvent(display, out var ev);
                    var window = GetEventWindow(ev);
                    if (window != 0 && _windows.TryGetValue(window, out var backend))
                        backend.ProcessEvent(ev);
                }

                dispatcher.ProcessWorkItems();

                // Coalesced rendering for all windows.
                foreach (var backend in _windows.Values.ToArray())
                    backend.RenderIfNeeded();
            }
            catch (Exception ex)
            {
                if (Application.TryHandleUiException(ex))
                    continue;

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

        return ev.type switch
        {
            KeyPress or KeyRelease => ev.xkey.window,
            ButtonPress or ButtonRelease => ev.xbutton.window,
            MotionNotify => ev.xmotion.window,
            DestroyNotify => ev.xdestroywindow.window,
            Expose => ev.xexpose.window,
            ConfigureNotify => ev.xconfigure.window,
            ClientMessage => ev.xclient.window,
            _ => 0
        };
    }

    public void Quit(Application app) => _running = false;

    public void DoEvents()
    {
        foreach (var backend in _windows.Values)
            backend.PumpEventsOnce();
    }

    public void Dispose()
    {
        foreach (var backend in _windows.Values.ToArray())
            backend.Dispose();
        _windows.Clear();

        if (_display != 0)
        {
            try { NativeX11.XCloseDisplay(_display); } catch { }
            _display = 0;
        }
    }
}
