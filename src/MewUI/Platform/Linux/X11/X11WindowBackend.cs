using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Elements;
using System.Runtime.InteropServices;
using Aprillz.MewUI.Native;
using NativeX11 = Aprillz.MewUI.Native.X11;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Primitives;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewUI.Core;

namespace Aprillz.MewUI.Platform.Linux.X11;

internal sealed class X11WindowBackend : IWindowBackend
{
    private readonly X11PlatformHost _host;
    internal Window Window { get; }

    private bool _shown;
    private bool _disposed;
    private nint _wmDeleteWindowAtom;
    private bool _needsRender;
    private long _lastRenderTick;
    private UIElement? _mouseOverElement;
    private UIElement? _capturedElement;

    public nint Handle { get; private set; }
    public nint Display { get; private set; }

    internal X11WindowBackend(X11PlatformHost host, Window window)
    {
        _host = host;
        Window = window;
    }

    public void Show()
    {
        if (_shown)
            return;

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
        if (Display != 0 && Handle != 0)
            NativeX11.XDestroyWindow(Display, Handle);
    }

    public void Invalidate(bool erase)
    {
        if (Display == 0 || Handle == 0)
            return;

        // Coalesce invalidations; render will be performed by the platform host loop.
        _needsRender = true;
    }

    public void SetTitle(string title)
    {
        if (Display == 0 || Handle == 0)
            return;
        NativeX11.XStoreName(Display, Handle, title ?? string.Empty);
        NativeX11.XFlush(Display);
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

    private void CreateWindow()
    {
        Display = NativeX11.XOpenDisplay(0);
        if (Display == 0)
            throw new InvalidOperationException("XOpenDisplay failed.");

        int screen = NativeX11.XDefaultScreen(Display);
        nint root = NativeX11.XRootWindow(Display, screen);

        uint width = (uint)Math.Max(1, (int)Math.Round(Window.Width));
        uint height = (uint)Math.Max(1, (int)Math.Round(Window.Height));

        // Choose a GLX visual for OpenGL rendering and create the window with that visual.
        // This keeps it compatible with later Wayland/EGL abstraction (window owns the surface config).
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

        nint visualInfoPtr;
        unsafe
        {
            fixed (int* p = attribs)
                visualInfoPtr = LibGL.glXChooseVisual(Display, screen, (nint)p);
        }

        if (visualInfoPtr == 0)
            throw new InvalidOperationException("glXChooseVisual failed.");

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
            0, 0,
            width, height,
            0,
            visualInfo.depth,
            1, // InputOutput
            visualInfo.visual,
            CWEventMask | CWColormap,
            ref attrs);

        if (Handle == 0)
            throw new InvalidOperationException("XCreateWindow failed.");

        // Let the OpenGL backend create a matching GLX context later.
        OpenGLLinuxWindowInfoRegistry.RegisterVisual(Handle, visualInfo);
        DiagLog.Write($"X11 window created: display=0x{Display.ToInt64():X} window=0x{Handle.ToInt64():X} {width}x{height}");

        _host.RegisterWindow(Handle, this);
        Window.AttachBackend(this);
        Window.SetDpi(96);
        Window.SetClientSizeDip(width, height);

        SetTitle(Window.Title);

        // WM_DELETE_WINDOW
        _wmDeleteWindowAtom = NativeX11.XInternAtom(Display, "WM_DELETE_WINDOW", false);
        if (_wmDeleteWindowAtom != 0)
            NativeX11.XSetWMProtocols(Display, Handle, ref _wmDeleteWindowAtom, 1);

        Window.RaiseLoaded();
        _needsRender = true;
    }

    internal void PumpEventsOnce()
    {
        if (Display == 0)
            return;
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
                    _needsRender = true;
                break;

            case ConfigureNotify:
                var cfg = ev.xconfigure;
                Window.SetClientSizeDip(cfg.width, cfg.height);
                _needsRender = true;
                break;

            case ClientMessage:
                unsafe
                {
                    var client = ev.xclient;
                    if (_wmDeleteWindowAtom != 0 && client.data[0] == (long)_wmDeleteWindowAtom)
                    {
                        Window.RaiseClosed();
                        Dispose();
                    }
                }
                break;

            case DestroyNotify:
                // Ensure we unregister and release resources even if the window is destroyed externally.
                Window.RaiseClosed();
                Dispose();
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
        if (!_needsRender || Handle == 0 || Display == 0)
            return;

        // Simple throttle to reduce CPU/GPU pressure on software-rendered VMs.
        long now = Environment.TickCount64;
        if (now - _lastRenderTick < 16)
            return;
        _lastRenderTick = now;

        _needsRender = false;
        Render();
    }

    private void HandleKey(XKeyEvent e, bool isDown)
    {
        if (Window.Content == null)
            return;

        // KeyDown/Up (keysym based).
        var ks = NativeX11.XLookupKeysym(ref e, 0).ToInt64();
        var key = MapKeysymToKey(ks);
        var args = new KeyEventArgs(key, platformKey: (int)ks, modifiers: GetModifiers(e.state), isRepeat: false);

        if (isDown)
        {
            Window.RaisePreviewKeyDown(args);
            if (!args.Handled)
            {
                if (args.Key == Key.Tab)
                {
                    if (args.Modifiers.HasFlag(ModifierKeys.Shift))
                        Window.FocusManager.MoveFocusPrevious();
                    else
                        Window.FocusManager.MoveFocusNext();

                    args.Handled = true;
                    return;
                }

                Window.FocusManager.FocusedElement?.OnKeyDown(args);
            }

            // Text input after key handling (best-effort).
            if (!args.Handled)
            {
                var buf = new byte[64];
                NativeX11.XLookupString(ref e, buf, buf.Length, out _, out _);
                if (buf[0] != 0)
                {
                    string s = System.Text.Encoding.UTF8.GetString(buf).TrimEnd('\0');
                    if (!string.IsNullOrEmpty(s))
                    {
                        // Filter control characters so Tab doesn't get inserted into TextBox.
                        // (Tab is handled above for focus navigation)
                        if (s.Length == 1 && char.IsControl(s[0]) && s[0] != '\r' && s[0] != '\n')
                            return;

                        var ti = new TextInputEventArgs(s);
                        Window.RaisePreviewTextInput(ti);
                        if (!ti.Handled)
                            Window.FocusManager.FocusedElement?.OnTextInput(ti);
                    }
                }
            }
        }
        else
        {
            Window.RaisePreviewKeyUp(args);
            if (!args.Handled)
                Window.FocusManager.FocusedElement?.OnKeyUp(args);
        }
    }

    private void HandleButton(XButtonEvent e, bool isDown)
    {
        var pos = new Point(e.x, e.y);

        if (isDown)
            Window.ClosePopupsIfClickOutside(pos);

        var element = _capturedElement ?? Window.HitTest(pos);
        if (element == null)
            return;

        if (element != _mouseOverElement)
        {
            _mouseOverElement?.SetMouseOver(false);
            element.SetMouseOver(true);
            _mouseOverElement = element;
        }

        // X11 button: 1 left, 2 middle, 3 right, 4/5 wheel.
        if (e.button == 4 || e.button == 5)
        {
            if (!isDown)
                return;
            int delta = e.button == 4 ? 120 : -120;
            element.OnMouseWheel(new MouseWheelEventArgs(pos, pos, delta, isHorizontal: false));
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
        if (btn == MouseButton.Left) left = isDown;
        if (btn == MouseButton.Middle) middle = isDown;
        if (btn == MouseButton.Right) right = isDown;

        var args = new MouseEventArgs(pos, pos, btn, leftButton: left, rightButton: right, middleButton: middle);

        if (isDown)
            element.OnMouseDown(args);
        else
            element.OnMouseUp(args);
    }

    private void HandleMotion(XMotionEvent e)
    {
        var pos = new Point(e.x, e.y);
        var element = _capturedElement ?? Window.HitTest(pos);

        if (element != _mouseOverElement)
        {
            _mouseOverElement?.SetMouseOver(false);
            element?.SetMouseOver(true);
            _mouseOverElement = element;
        }

        if (element == null)
            return;

        bool left = (e.state & (1u << 8)) != 0;
        bool middle = (e.state & (1u << 9)) != 0;
        bool right = (e.state & (1u << 10)) != 0;

        var args = new MouseEventArgs(pos, pos, MouseButton.Left, leftButton: left, rightButton: right, middleButton: middle);
        element.OnMouseMove(args);
    }

    private static Key MapKeysymToKey(long keysym)
    {
        // Minimal mapping for navigation/editing.
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
        if ((x11State & ShiftMask) != 0) modifiers |= ModifierKeys.Shift;
        if ((x11State & ControlMask) != 0) modifiers |= ModifierKeys.Control;
        if ((x11State & Mod1Mask) != 0) modifiers |= ModifierKeys.Alt;
        return modifiers;
    }

    private void Render()
    {
        if (Handle == 0 || Display == 0)
            return;

        Window.PerformLayout();
        Window.RenderFrame(Display);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        _mouseOverElement?.SetMouseOver(false);
        _mouseOverElement = null;

        if (_capturedElement != null)
        {
            _capturedElement.SetMouseCaptured(false);
            _capturedElement = null;
        }

        if (Handle != 0 && Display != 0)
        {
            if (Window.GraphicsFactory is Rendering.IWindowResourceReleaser releaser)
                releaser.ReleaseWindowResources(Handle);

            _host.UnregisterWindow(Handle);
            Window.DisposeVisualTree();
            OpenGLLinuxWindowInfoRegistry.Unregister(Handle);
            NativeX11.XDestroyWindow(Display, Handle);
            Handle = 0;
        }

        // Display lifetime is managed by the platform host (shared across windows).
    }
}
