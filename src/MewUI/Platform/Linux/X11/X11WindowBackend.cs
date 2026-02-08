using System.Runtime.InteropServices;

using Aprillz.MewUI.Input;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewUI.Resources;

using NativeX11 = Aprillz.MewUI.Native.X11;

namespace Aprillz.MewUI.Platform.Linux.X11;

internal sealed class X11WindowBackend : IWindowBackend
{
    private readonly X11PlatformHost _host;

    internal Window Window { get; }

    private bool _shown;
    private bool _disposed;
    private bool _cleanupDone;
    private bool _closedRaised;
    private nint _wmDeleteWindowAtom;
    private nint _wmProtocolsAtom;
    private long _lastRenderTick;

    private UIElement? _mouseOverElement;
    private UIElement? _capturedElement;
    private readonly ClickCountTracker _clickCountTracker = new();
    private readonly int[] _lastPressClickCounts = new int[5];
    private IconSource? _icon;

    internal bool NeedsRender { get; private set; }

    public nint Handle { get; private set; }

    public nint Display { get; private set; }

    internal X11WindowBackend(X11PlatformHost host, Window window)
    {
        _host = host;
        Window = window;
    }

    public void SetResizable(bool resizable)
    {
        if (Display == 0 || Handle == 0)
        {
            return;
        }

        ApplyResizeMode();
    }

    public void Show()
    {
        if (_shown)
        {
            return;
        }

        CreateWindow();
        _shown = true;
        NativeX11.XMapWindow(Display, Handle);
        NativeX11.XFlush(Display);
    }

    public void Hide()
    {
        // TODO: XUnmapWindow
    }

    public void Close()
    {
        // Cleanup immediately to avoid X errors from late render/layout passes
        // that may query window attributes after the server has destroyed the window.
        var handle = Handle;
        if (Display == 0 || handle == 0)
        {
            return;
        }

        RaiseClosedOnce();
        Cleanup(handle, destroyWindow: true);
    }

    public void Invalidate(bool erase)
    {
        if (Display == 0 || Handle == 0)
        {
            return;
        }

        // Coalesce invalidations; render will be performed by the platform host loop.
        NeedsRender = true;
        _host.RequestWake();
    }

    public void SetTitle(string title)
    {
        if (Display == 0 || Handle == 0)
        {
            return;
        }

        NativeX11.XStoreName(Display, Handle, title ?? string.Empty);
        NativeX11.XFlush(Display);
    }

    public void SetIcon(IconSource? icon)
    {
        _icon = icon;
        if (Display == 0 || Handle == 0)
        {
            return;
        }

        ApplyIcon();
    }

    public void SetClientSize(double widthDip, double heightDip)
    {
        // TODO: XResizeWindow
    }

    public void CaptureMouse(UIElement element)
    {
        // TODO: XGrabPointer
        _capturedElement = element;
        element.SetMouseCaptured(true);
    }

    public void ReleaseMouseCapture()
    {
        // TODO: XUngrabPointer
        if (_capturedElement != null)
        {
            _capturedElement.SetMouseCaptured(false);
            _capturedElement = null;
        }
    }

    public Point ClientToScreen(Point clientPointDip)
    {
        if (Display == 0 || Handle == 0)
        {
            throw new InvalidOperationException("Window is not initialized.");
        }

        int screen = NativeX11.XDefaultScreen(Display);
        nint root = NativeX11.XRootWindow(Display, screen);

        int x = (int)Math.Round(clientPointDip.X * Window.DpiScale);
        int y = (int)Math.Round(clientPointDip.Y * Window.DpiScale);

        NativeX11.XTranslateCoordinates(Display, Handle, root, x, y, out int rx, out int ry, out _);
        return new Point(rx, ry);
    }

    public Point ScreenToClient(Point screenPointPx)
    {
        if (Display == 0 || Handle == 0)
        {
            throw new InvalidOperationException("Window is not initialized.");
        }

        int screen = NativeX11.XDefaultScreen(Display);
        nint root = NativeX11.XRootWindow(Display, screen);

        int x = (int)Math.Round(screenPointPx.X);
        int y = (int)Math.Round(screenPointPx.Y);

        NativeX11.XTranslateCoordinates(Display, root, Handle, x, y, out int cx, out int cy, out _);
        return new Point(cx / Window.DpiScale, cy / Window.DpiScale);
    }

    private void CreateWindow()
    {
        Display = _host.Display;
        if (Display == 0)
        {
            throw new InvalidOperationException("X11 display not initialized.");
        }

        int screen = NativeX11.XDefaultScreen(Display);
        nint root = NativeX11.XRootWindow(Display, screen);

        uint dpi = _host.GetDpiForWindow(0);
        Window.SetDpi(dpi);
        double dpiScale = Window.DpiScale;

        uint width = (uint)Math.Max(1, (int)Math.Round(Window.Width * dpiScale));
        uint height = (uint)Math.Max(1, (int)Math.Round(Window.Height * dpiScale));

        int x = 0;
        int y = 0;
        if (Window.StartupLocation == WindowStartupLocation.CenterScreen &&
            NativeX11.XGetWindowAttributes(Display, root, out var rootAttrs) != 0)
        {
            x = Math.Max(0, (rootAttrs.width - (int)width) / 2);
            y = Math.Max(0, (rootAttrs.height - (int)height) / 2);
        }

        // Choose a GLX visual for OpenGL rendering and create the window with that visual.
        // This keeps it compatible with later Wayland/EGL abstraction (window owns the surface config).
        // Try MSAA first to reduce jaggies on filled primitives (RoundRect, Ellipse, etc).
        // GLX_SAMPLE_BUFFERS / GLX_SAMPLES are from GLX_ARB_multisample.
        const int GLX_SAMPLE_BUFFERS = 100000;
        const int GLX_SAMPLES = 100001;

        int[] attribsMsaa =
        {
            4,  // GLX_RGBA
            5,  // GLX_DOUBLEBUFFER
            8,  // GLX_RED_SIZE
            8,
            9,  // GLX_GREEN_SIZE
            8,
            10, // GLX_BLUE_SIZE
            8,
            11, // GLX_ALPHA_SIZE
            8,
            GLX_SAMPLE_BUFFERS, 1,
            GLX_SAMPLES, 4,
            0
        };

        nint visualInfoPtr;
        unsafe
        {
            fixed (int* p = attribsMsaa)
            {
                visualInfoPtr = LibGL.glXChooseVisual(Display, screen, (nint)p);
            }
        }

        if (visualInfoPtr == 0)
        {
            int[] attribs =
            {
                4,  // GLX_RGBA
                5,  // GLX_DOUBLEBUFFER
                8,  // GLX_RED_SIZE
                8,
                9,  // GLX_GREEN_SIZE
                8,
                10, // GLX_BLUE_SIZE
                8,
                11, // GLX_ALPHA_SIZE
                8,
                0
            };

            unsafe
            {
                fixed (int* p = attribs)
                {
                    visualInfoPtr = LibGL.glXChooseVisual(Display, screen, (nint)p);
                }
            }

            if (visualInfoPtr == 0)
            {
                throw new InvalidOperationException("glXChooseVisual failed.");
            }
        }

        var visualInfo = Marshal.PtrToStructure<XVisualInfo>(visualInfoPtr);
        NativeX11.XFree(visualInfoPtr);

        const int AllocNone = 0;
        const ulong CWEventMask = 1UL << 11;
        const ulong CWColormap = 1UL << 13;

        var attrs = new XSetWindowAttributes
        {
            colormap = NativeX11.XCreateColormap(Display, root, visualInfo.visual, AllocNone),
            event_mask = (nint)(X11EventMask.ExposureMask | X11EventMask.StructureNotifyMask |
                               X11EventMask.KeyPressMask | X11EventMask.KeyReleaseMask |
                               X11EventMask.ButtonPressMask | X11EventMask.ButtonReleaseMask |
                               X11EventMask.PointerMotionMask),
        };

        Handle = NativeX11.XCreateWindow(
            Display,
            root,
            x, y,
            width, height,
            0,
            visualInfo.depth,
            1, // InputOutput
            visualInfo.visual,
            CWEventMask | CWColormap,
            ref attrs);

        if (Handle == 0)
        {
            throw new InvalidOperationException("XCreateWindow failed.");
        }

        // Let the OpenGL backend create a matching GLX context later.
        OpenGLLinuxWindowInfoRegistry.RegisterVisual(Handle, visualInfo);
        DiagLog.Write($"X11 window created: display=0x{Display.ToInt64():X} window=0x{Handle.ToInt64():X} {width}x{height}");

        _host.RegisterWindow(Handle, this);
        Window.AttachBackend(this);
        Window.SetClientSizeDip(width / dpiScale, height / dpiScale);

        SetTitle(Window.Title);
        ApplyIcon();

        // WM_DELETE_WINDOW
        _wmProtocolsAtom = NativeX11.XInternAtom(Display, "WM_PROTOCOLS", false);
        _wmDeleteWindowAtom = NativeX11.XInternAtom(Display, "WM_DELETE_WINDOW", false);
        if (_wmProtocolsAtom != 0 && _wmDeleteWindowAtom != 0)
        {
            NativeX11.XSetWMProtocols(Display, Handle, ref _wmDeleteWindowAtom, 1);
        }

        ApplyResizeMode();

        NeedsRender = true;
    }

    private unsafe void ApplyIcon()
    {
        if (Display == 0 || Handle == 0)
        {
            return;
        }

        var iconAtom = NativeX11.XInternAtom(Display, "_NET_WM_ICON", false);
        if (iconAtom == 0)
        {
            return;
        }

        if (_icon == null)
        {
            NativeX11.XDeleteProperty(Display, Handle, iconAtom);
            NativeX11.XFlush(Display);
            return;
        }

        var cardinalAtom = NativeX11.XInternAtom(Display, "CARDINAL", false);
        if (cardinalAtom == 0)
        {
            return;
        }

        var dpiScale = Window.DpiScale <= 0 ? 1.0 : Window.DpiScale;
        int smallPx = Math.Max(16, (int)Math.Round(16 * dpiScale));
        int bigPx = Math.Max(32, (int)Math.Round(32 * dpiScale));

        var payload = new List<uint>(capacity: 16);
        AppendNetWmIconPayload(payload, _icon, smallPx);
        if (bigPx != smallPx)
        {
            AppendNetWmIconPayload(payload, _icon, bigPx);
        }

        if (payload.Count == 0)
        {
            NativeX11.XDeleteProperty(Display, Handle, iconAtom);
            NativeX11.XFlush(Display);
            return;
        }

        var data = payload.ToArray();
        fixed (uint* p = data)
        {
            NativeX11.XChangeProperty(
                display: Display,
                window: Handle,
                property: iconAtom,
                type: cardinalAtom,
                format: 32,
                mode: 0, // PropModeReplace
                data: (nint)p,
                nelements: data.Length);
        }

        NativeX11.XFlush(Display);
    }

    private static void AppendNetWmIconPayload(List<uint> dst, IconSource icon, int desiredSizePx)
    {
        var src = icon.Pick(desiredSizePx);
        if (src == null)
        {
            return;
        }

        if (!ImageDecoders.TryDecode(src.Data, out var bmp))
        {
            return;
        }

        int w = Math.Max(1, bmp.WidthPx);
        int h = Math.Max(1, bmp.HeightPx);
        dst.Add((uint)w);
        dst.Add((uint)h);

        var pixels = bmp.Data;
        int idx = 0;
        int pixelCount = checked(w * h);
        for (int i = 0; i < pixelCount; i++)
        {
            byte b = pixels[idx++];
            byte g = pixels[idx++];
            byte r = pixels[idx++];
            byte a = pixels[idx++];
            dst.Add(((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b);
        }
    }

    private void ApplyResizeMode()
    {
        if (Display == 0 || Handle == 0)
        {
            return;
        }

        var hints = new XSizeHints();
        if (!Window.WindowSize.IsResizable)
        {
            hints.flags = XSizeHintsFlags.PMinSize | XSizeHintsFlags.PMaxSize;
            hints.min_width = (int)Math.Max(1, Math.Round(Window.Width * Window.DpiScale));
            hints.min_height = (int)Math.Max(1, Math.Round(Window.Height * Window.DpiScale));
            hints.max_width = hints.min_width;
            hints.max_height = hints.min_height;
        }
        else
        {
            hints.flags = 0;
        }

        NativeX11.XSetWMNormalHints(Display, Handle, ref hints);
        NativeX11.XFlush(Display);
    }

    internal void PumpEventsOnce()
    {
        if (Display == 0)
        {
            return;
        }

        while (NativeX11.XPending(Display) != 0)
        {
            NativeX11.XNextEvent(Display, out var ev);
            ProcessEvent(ev);
        }
    }

    internal void ProcessEvent(XEvent ev)
    {
        const int Expose = 12;
        const int DestroyNotify = 17;
        const int ConfigureNotify = 22;
        const int ClientMessage = 33;
        const int KeyPress = 2;
        const int KeyRelease = 3;
        const int ButtonPress = 4;
        const int ButtonRelease = 5;
        const int MotionNotify = 6;

        switch (ev.type)
        {
            case Expose:
                // X11 can deliver multiple expose events; render once for the last in the batch.
                if (ev.xexpose.count == 0)
                {
                    NeedsRender = true;
                }

                break;

            case ConfigureNotify:
                var cfg = ev.xconfigure;
                Window.SetClientSizeDip(cfg.width / Window.DpiScale, cfg.height / Window.DpiScale);
                NeedsRender = true;
                break;

            case ClientMessage:
                unsafe
                {
                    var client = ev.xclient;
                    if (_wmProtocolsAtom != 0 &&
                        _wmDeleteWindowAtom != 0 &&
                        client.message_type == _wmProtocolsAtom &&
                        client.format == 32 &&
                        (nint)client.data[0] == _wmDeleteWindowAtom)
                    {
                        // Ask the window to close; it will destroy the X11 window.
                        // Cleanup happens on DestroyNotify.
                        Window.Close();
                    }
                }
                break;

            case DestroyNotify:
                // Ensure we unregister and release resources even if the window is destroyed externally.
                RaiseClosedOnce();
                Cleanup(ev.xdestroywindow.window, destroyWindow: false);
                break;

            case KeyPress:
            case KeyRelease:
                HandleKey(ev.xkey, isDown: ev.type == KeyPress);
                break;

            case ButtonPress:
            case ButtonRelease:
                HandleButton(ev.xbutton, isDown: ev.type == ButtonPress);
                break;

            case MotionNotify:
                HandleMotion(ev.xmotion);
                break;
        }
    }

    internal void RenderIfNeeded()
    {
        if (!NeedsRender)
        {
            return;
        }

        // If the window is not renderable (already closed/destroyed), clear the flag
        // to avoid keeping the platform host loop hot.
        if (Handle == 0 || Display == 0)
        {
            NeedsRender = false;
            return;
        }

        // Simple throttle to reduce CPU/GPU pressure on software-rendered VMs.
        long now = Environment.TickCount64;
        if (now - _lastRenderTick < 16)
        {
            return;
        }

        _lastRenderTick = now;

        NeedsRender = false;
        RenderNowCore();
    }

    internal int GetRenderDelayMs()
    {
        if (!NeedsRender)
        {
            return int.MaxValue;
        }

        long now = Environment.TickCount64;
        long elapsed = now - _lastRenderTick;
        if (elapsed >= 16)
        {
            return 0;
        }

        return (int)Math.Clamp(16 - elapsed, 0, 16);
    }

    internal void RenderNow()
    {
        if (Handle == 0 || Display == 0)
        {
            return;
        }

        NeedsRender = false;
        RenderNowCore();
    }

    private void RenderNowCore()
    {
        Render();
    }

    private void HandleKey(XKeyEvent e, bool isDown)
    {
        if (Window.Content == null)
        {
            return;
        }

        // KeyDown/Up (keysym based).
        var ks = NativeX11.XLookupKeysym(ref e, 0).ToInt64();
        var key = MapKeysymToKey(ks);
        var args = new KeyEventArgs(key, platformKey: (int)ks, modifiers: GetModifiers(e.state), isRepeat: false);

        if (isDown)
        {
            // Many window managers send WM_DELETE_WINDOW for Alt+F4, but not all do (especially for secondary windows).
            // Handle it ourselves to ensure consistent close behavior.
            const long XK_F4 = 0xFFC1;
            if (ks == XK_F4 && args.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                Window.Close();
                return;
            }

            Window.RaisePreviewKeyDown(args);
            if (!args.Handled)
            {
                if (args.Key == Key.Tab)
                {
                    if (args.Modifiers.HasFlag(ModifierKeys.Shift))
                    {
                        Window.FocusManager.MoveFocusPrevious();
                    }
                    else
                    {
                        Window.FocusManager.MoveFocusNext();
                    }

                    args.Handled = true;
                    return;
                }

                Window.FocusManager.FocusedElement?.RaiseKeyDown(args);
            }

            // Text input after key handling (best-effort).
            if (!args.Handled)
            {
                Span<byte> buf = stackalloc byte[64];
                unsafe
                {
                    fixed (byte* p = buf)
                    {
                        NativeX11.XLookupString(ref e, p, buf.Length, out _, out _);
                    }
                }

                if (buf[0] != 0)
                {
                    int len = buf.IndexOf((byte)0);
                    if (len < 0)
                    {
                        len = buf.Length;
                    }

                    string s = System.Text.Encoding.UTF8.GetString(buf[..len]);
                    if (!string.IsNullOrEmpty(s))
                    {
                        // Filter control characters so Tab doesn't get inserted into TextBox.
                        // (Tab is handled above for focus navigation)
                        if (s.Length == 1 && char.IsControl(s[0]) && s[0] != '\r' && s[0] != '\n')
                        {
                            return;
                        }

                        var ti = new TextInputEventArgs(s);
                        Window.RaisePreviewTextInput(ti);
                        if (!ti.Handled)
                        {
                            Window.FocusManager.FocusedElement?.RaiseTextInput(ti);
                        }
                    }
                }
            }
        }
        else
        {
            Window.RaisePreviewKeyUp(args);
            if (!args.Handled)
            {
                Window.FocusManager.FocusedElement?.RaiseKeyUp(args);
            }

            Window.RequerySuggested();
        }
    }

    private void HandleButton(XButtonEvent e, bool isDown)
    {
        int xPx = e.x;
        int yPx = e.y;
        var pos = new Point(xPx / Window.DpiScale, yPx / Window.DpiScale);

        // X11 button: 1 left, 2 middle, 3 right, 4/5 wheel.
        if (e.button == 4 || e.button == 5)
        {
            if (!isDown)
            {
                return;
            }

            var wheelElement = WindowInputRouter.HitTest(Window, _capturedElement, pos);
            WindowInputRouter.UpdateMouseOver(Window, ref _mouseOverElement, wheelElement);

            int delta = e.button == 4 ? 120 : -120;
            bool leftDown = (e.state & (1u << 8)) != 0;
            bool middleDown = (e.state & (1u << 9)) != 0;
            bool rightDown = (e.state & (1u << 10)) != 0;
            WindowInputRouter.MouseWheel(Window, pos, pos, delta, isHorizontal: false, leftDown, rightDown, middleDown);

            return;
        }

        var btn = e.button switch
        {
            1 => MouseButton.Left,
            2 => MouseButton.Middle,
            3 => MouseButton.Right,
            _ => MouseButton.Left
        };

        bool left = (e.state & (1u << 8)) != 0;
        bool middle = (e.state & (1u << 9)) != 0;
        bool right = (e.state & (1u << 10)) != 0;

        // Include the current transition.
        switch (btn)
        {
            case MouseButton.Left:
                left = isDown;
                break;
            case MouseButton.Middle:
                middle = isDown;
                break;
            case MouseButton.Right:
                right = isDown;
                break;
        }

        int clickCount = 1;
        int buttonIndex = (int)btn;
        if ((uint)buttonIndex < (uint)_lastPressClickCounts.Length)
        {
            if (isDown)
            {
                const uint defaultMaxDelayMs = 500;
                int maxDist = (int)System.Math.Round(4 * Window.DpiScale);
                clickCount = _clickCountTracker.Update(btn, xPx, yPx, unchecked((uint)e.time), defaultMaxDelayMs, maxDist, maxDist);
                _lastPressClickCounts[buttonIndex] = clickCount;
            }
            else
            {
                clickCount = _lastPressClickCounts[buttonIndex];
                if (clickCount <= 0)
                {
                    clickCount = 1;
                }
            }
        }

        WindowInputRouter.MouseButton(
            Window,
            ref _mouseOverElement,
            _capturedElement,
            pos,
            pos,
            btn,
            isDown,
            leftDown: left,
            rightDown: right,
            middleDown: middle,
            clickCount: clickCount);
    }

    private void HandleMotion(XMotionEvent e)
    {
        var pos = new Point(e.x / Window.DpiScale, e.y / Window.DpiScale);

        bool left = (e.state & (1u << 8)) != 0;
        bool middle = (e.state & (1u << 9)) != 0;
        bool right = (e.state & (1u << 10)) != 0;

        WindowInputRouter.MouseMove(Window, ref _mouseOverElement, _capturedElement, pos, pos, leftDown: left, rightDown: right, middleDown: middle);
    }

    internal void NotifyDpiChanged(uint oldDpi, uint newDpi)
    {
        if (Display == 0 || Handle == 0 || oldDpi == newDpi)
        {
            return;
        }

        Window.SetDpi(newDpi);
        Window.RaiseDpiChanged(oldDpi, newDpi);

        if (NativeX11.XGetWindowAttributes(Display, Handle, out var attrs) != 0)
        {
            Window.SetClientSizeDip(attrs.width / Window.DpiScale, attrs.height / Window.DpiScale);
        }

        // Force layout recalculation with the correct DPI
        Window.PerformLayout();
        NeedsRender = true;
    }

    private static Key MapKeysymToKey(long keysym)
    {
        // Minimal mapping for navigation/editing.
        // KeySyms for ASCII letters/numbers are their Unicode code points
        // (e.g. 'A' == 0x41, 'a' == 0x61, '0' == 0x30).
        if (keysym is >= 0x41 and <= 0x5A)
        {
            return (Key)((int)Key.A + (int)(keysym - 0x41));
        }

        if (keysym is >= 0x61 and <= 0x7A)
        {
            return (Key)((int)Key.A + (int)(keysym - 0x61));
        }

        if (keysym is >= 0x30 and <= 0x39)
        {
            return (Key)((int)Key.D0 + (int)(keysym - 0x30));
        }

        return keysym switch
        {
            0xFF08 => Key.Backspace,
            0xFF09 => Key.Tab,
            0xFF0D => Key.Enter,
            0xFF1B => Key.Escape,
            0xFF50 => Key.Home,
            0xFF51 => Key.Left,
            0xFF52 => Key.Up,
            0xFF53 => Key.Right,
            0xFF54 => Key.Down,
            0xFF55 => Key.PageUp,
            0xFF56 => Key.PageDown,
            0xFF57 => Key.End,
            0xFFFF => Key.Delete,
            _ => Key.None
        };
    }

    private static ModifierKeys GetModifiers(uint x11State)
    {
        // X11 state masks (X.h)
        const uint ShiftMask = 1u << 0;
        const uint ControlMask = 1u << 2;
        const uint Mod1Mask = 1u << 3; // usually Alt
        // const uint Mod4Mask = 1u << 6; // usually Super/Win (ignored for now)

        ModifierKeys modifiers = ModifierKeys.None;
        if ((x11State & ShiftMask) != 0)
        {
            modifiers |= ModifierKeys.Shift;
        }

        if ((x11State & ControlMask) != 0)
        {
            modifiers |= ModifierKeys.Control;
        }

        if ((x11State & Mod1Mask) != 0)
        {
            modifiers |= ModifierKeys.Alt;
        }

        return modifiers;
    }

    private void Render()
    {
        if (Handle == 0 || Display == 0)
        {
            return;
        }

        Window.PerformLayout();
        Window.RenderFrame(Display);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _mouseOverElement?.SetMouseOver(false);
        _mouseOverElement = null;

        if (_capturedElement != null)
        {
            _capturedElement.SetMouseCaptured(false);
            _capturedElement = null;
        }

        RaiseClosedOnce();
        Cleanup(Handle, destroyWindow: true);

        // Display lifetime is managed by the platform host (shared across windows).
    }

    private void Cleanup(nint handle, bool destroyWindow)
    {
        if (_cleanupDone)
        {
            return;
        }

        _cleanupDone = true;

        if (handle == 0 || Display == 0)
        {
            return;
        }

        if (destroyWindow)
        {
            try { NativeX11.XDestroyWindow(Display, handle); }
            catch { }
        }

        try
        {
            if (Window.GraphicsFactory is Rendering.IWindowResourceReleaser releaser)
            {
                releaser.ReleaseWindowResources(handle);
            }
        }
        catch { }

        try { _host.UnregisterWindow(handle); } catch { }
        try { Window.DisposeVisualTree(); } catch { }
        try { OpenGLLinuxWindowInfoRegistry.Unregister(handle); } catch { }

        if (Handle == handle)
        {
            Handle = 0;
        }
    }

    private void RaiseClosedOnce()
    {
        if (_closedRaised)
        {
            return;
        }

        _closedRaised = true;
        try { Window.RaiseClosed(); } catch { }
    }

    public void EnsureTheme(bool isDark)
    {
    }
}
