using System.Runtime.InteropServices;

using Aprillz.MewUI.Diagnostics;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Resources;

using NativeX11 = Aprillz.MewUI.Native.X11;

namespace Aprillz.MewUI.Platform.Linux.X11;

internal sealed class X11WindowBackend : IWindowBackend
{
    private static readonly EnvDebugLogger ImeLogger = new("MEWUI_IME_DEBUG", "[X11][IME]");
    private static readonly EnvDebugLogger XI2Logger = new("MEWUI_XI2_DEBUG", "[X11][XI2]");

    private readonly X11PlatformHost _host;

    private bool _shown;
    private bool _disposed;
    private bool _cleanupDone;
    private bool _closedRaised;
    private nint _wmDeleteWindowAtom;
    private nint _wmProtocolsAtom;
    private nint _atomAtom;
    private nint _netWmWindowOpacityAtom;
    private nint _cardinalAtom;
    private nint _motifWmHintsAtom;
    private nint _xdndAwareAtom;
    private nint _xdndEnterAtom;
    private nint _xdndPositionAtom;
    private nint _xdndStatusAtom;
    private nint _xdndLeaveAtom;
    private nint _xdndTypeListAtom;
    private nint _xdndActionCopyAtom;
    private nint _xdndActionMoveAtom;
    private nint _xdndActionLinkAtom;
    private nint _xdndDropAtom;
    private nint _xdndFinishedAtom;
    private nint _xdndSelectionAtom;
    private nint _textUriListAtom;
    private nint _xdndSelectionPropertyAtom;
    private nint _xdndSourceWindow;
    private readonly List<nint> _xdndOfferedTypes = new();
    private ulong _xdndVersion;
    private int _xdndLastRootX;
    private int _xdndLastRootY;
    private nint _xdndLastDropTime;
    private bool _xdndEnterDispatched;
    private DragDropEffects _xdndLastEffect;
    private bool _allowDrop;
    private long _lastRenderTick;
    private X11GlxVisualInfo? _glxVisualInfo;

    private readonly ClickCountTracker _clickCountTracker = new();
    private readonly int[] _lastPressClickCounts = new int[5];
    private IconSource? _icon;
    private double _opacity = 1.0;
    private bool _allowsTransparency;
    private bool _enabled = true;
    private nint _currentCursor;
    private IX11InputMethod? _inputMethod;

    // XInput2 scroll handling. _xi2Opcode is the per-display extension opcode discovered
    // via XQueryExtension and used to filter GenericEvent cookies. Each scroll axis the
    // driver advertises is tracked separately so we can compute (newValue - lastValue)
    // valuator deltas correctly per device.
    private bool _xi2Enabled;
    private int _xi2Opcode;
    private readonly Dictionary<(int deviceId, int valuator), XI2ScrollAxis> _scrollAxes = new();
    // True when at least one master pointer has a scroll axis cached — only then is XI_Motion
    // guaranteed to deliver high-res scroll, so only then should legacy core wheel be suppressed.
    private bool _xi2MasterHasScrollAxis;

    private sealed class XI2ScrollAxis
    {
        public int ScrollType;          // XI2.XIScrollTypeVertical / Horizontal
        public double Increment;        // valuator units per notch (XIScrollClassInfo.increment)
        public double LastValue;        // previous raw valuator value
        public bool HasLastValue;
    }

    internal bool NeedsRender { get; private set; }

    public nint Handle { get; private set; }

    public nint Display { get; private set; }

    internal X11WindowBackend(X11PlatformHost host, Window window)
    {
        _host = host;
        Window = window;
    }

    internal Window Window { get; }

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

        Window.PerformLayout();

        SetClientSize(Window.Width, Window.Height);

        ApplyResolvedStartupPosition();

        _shown = true;
        NativeX11.XMapWindow(Display, Handle);
        NativeX11.XFlush(Display);
        ApplyResolvedStartupPosition();
        if (Window.WindowState != Controls.WindowState.Normal)
        {
            SetWindowState(Window.WindowState);
        }

        // Some IM modules don't start preedit until the IC is focused.
        // Relying solely on FocusIn can miss cases where focus is already on the window when mapped.
        SetWindowActive(true);
        _inputMethod?.OnFocusIn();
    }

    public void Hide()
    {
        if (Display != 0 && Handle != 0)
        {
            NativeX11.XUnmapWindow(Display, Handle);
        }
    }

    public void BeginDragMove()
    {
        if (Display == 0 || Handle == 0)
        {
            return;
        }

        // Release any grab, then send _NET_WM_MOVERESIZE to the window manager
        NativeX11.XUngrabPointer(Display, 0);

        var moveResize = NativeX11.XInternAtom(Display, "_NET_WM_MOVERESIZE", false);
        if (moveResize == 0)
        {
            return;
        }

        // Query pointer for current position
        NativeX11.XQueryPointer(Display, NativeX11.XRootWindow(Display, 0),
            out _, out _, out int rootX, out int rootY, out _, out _, out _);

        var xev = new XEvent();
        unsafe
        {
            xev.xclient.type = 33; // ClientMessage
            xev.xclient.window = Handle;
            xev.xclient.message_type = moveResize;
            xev.xclient.format = 32;
            xev.xclient.data[0] = rootX;
            xev.xclient.data[1] = rootY;
            xev.xclient.data[2] = 8; // _NET_WM_MOVERESIZE_MOVE
            xev.xclient.data[3] = 1; // Button1
            xev.xclient.data[4] = 1; // source = normal app
        }
        NativeX11.XSendEvent(Display, NativeX11.XRootWindow(Display, 0),
            false, (nint)0x180000, ref xev);
        NativeX11.XFlush(Display);
    }

    public void SetWindowState(Controls.WindowState state)
    {
        if (Display == 0 || Handle == 0)
        {
            return;
        }

        switch (state)
        {
            case Controls.WindowState.Minimized:
                NativeX11.XIconifyWindow(Display, Handle, 0);
                break;

            case Controls.WindowState.Maximized:
                SendNetWmState(true, "_NET_WM_STATE_MAXIMIZED_HORZ", "_NET_WM_STATE_MAXIMIZED_VERT");
                break;

            case Controls.WindowState.Normal:
                SendNetWmState(false, "_NET_WM_STATE_MAXIMIZED_HORZ", "_NET_WM_STATE_MAXIMIZED_VERT");
                NativeX11.XMapWindow(Display, Handle);
                break;
        }
    }

    private uint _motifFunctions = 0x3F; // MWM_FUNC_ALL | RESIZE | MOVE | MINIMIZE | MAXIMIZE | CLOSE

    public void SetCanMinimize(bool value)
    {
        if (Display == 0 || Handle == 0)
        {
            return;
        }

        if (value)
        {
            _motifFunctions |= 0x08; // MWM_FUNC_MINIMIZE
        }
        else
        {
            _motifFunctions &= ~0x08u;
        }

        ApplyMotifHints();
    }

    public void SetCanMaximize(bool value)
    {
        if (Display == 0 || Handle == 0)
        {
            return;
        }

        if (value)
        {
            _motifFunctions |= 0x10; // MWM_FUNC_MAXIMIZE
        }
        else
        {
            _motifFunctions &= ~0x10u;
        }

        ApplyMotifHints();
    }

    private void ApplyMotifHints()
    {
        var atom = NativeX11.XInternAtom(Display, "_MOTIF_WM_HINTS", false);
        if (atom == 0)
        {
            return;
        }

        // _MOTIF_WM_HINTS: flags, functions, decorations, input_mode, status
        // flags = MWM_HINTS_FUNCTIONS (1)
        var hints = new long[] { 1, _motifFunctions, 0, 0, 0 };
        unsafe
        {
            fixed (long* p = hints)
            {
                NativeX11.XChangeProperty(Display, Handle, atom, atom,
                    32, 0 /* PropModeReplace */, (nint)p, 5);
            }
        }
    }

    public void SetTopmost(bool value)
    {
        if (Display == 0 || Handle == 0)
        {
            return;
        }

        SendNetWmState(value, "_NET_WM_STATE_ABOVE");
    }

    public void SetShowInTaskbar(bool value)
    {
        if (Display == 0 || Handle == 0)
        {
            return;
        }

        SendNetWmState(!value, "_NET_WM_STATE_SKIP_TASKBAR");
    }

    private void SendNetWmState(bool add, params string[] atoms)
    {
        var netWmState = NativeX11.XInternAtom(Display, "_NET_WM_STATE", false);
        if (netWmState == 0)
        {
            return;
        }

        foreach (var name in atoms)
        {
            var atom = NativeX11.XInternAtom(Display, name, false);
            if (atom == 0)
            {
                continue;
            }

            var xev = new XEvent();
            unsafe
            {
                xev.xclient.type = 33; // ClientMessage
                xev.xclient.window = Handle;
                xev.xclient.message_type = netWmState;
                xev.xclient.format = 32;
                xev.xclient.data[0] = add ? 1 : 0; // _NET_WM_STATE_ADD=1, _NET_WM_STATE_REMOVE=0
                xev.xclient.data[1] = atom;
            }
            NativeX11.XSendEvent(Display, NativeX11.XRootWindow(Display, 0),
                false, (nint)0x180000 /* SubstructureRedirect | SubstructureNotify */, ref xev);
        }
        NativeX11.XFlush(Display);
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

        if (!Window.RequestClose())
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
        if (Display == 0 || Handle == 0)
        {
            return;
        }

        double dpiScale = Window.DpiScale <= 0 ? 1.0 : Window.DpiScale;
        uint widthPx = (uint)Math.Max(1, (int)Math.Round(widthDip * dpiScale));
        uint heightPx = (uint)Math.Max(1, (int)Math.Round(heightDip * dpiScale));

        NativeX11.XResizeWindow(Display, Handle, widthPx, heightPx);
        Window.SetClientSizeDip(widthDip, heightDip);
        NativeX11.XFlush(Display);
    }

    public Point GetPosition()
    {
        if (Display == 0 || Handle == 0)
        {
            return default;
        }

        double dpiScale = Window.DpiScale <= 0 ? 1.0 : Window.DpiScale;
        int screen = NativeX11.XDefaultScreen(Display);
        nint root = NativeX11.XRootWindow(Display, screen);

        // Translate window origin (0,0) to root coordinates.
        NativeX11.XTranslateCoordinates(Display, Handle, root, 0, 0, out int rx, out int ry, out _);
        return new Point(rx / dpiScale, ry / dpiScale);
    }

    public void SetPosition(double leftDip, double topDip)
    {
        if (Display == 0 || Handle == 0)
        {
            return;
        }

        double dpiScale = Window.DpiScale <= 0 ? 1.0 : Window.DpiScale;
        int x = (int)Math.Round(leftDip * dpiScale);
        int y = (int)Math.Round(topDip * dpiScale);

        NativeX11.XMoveWindow(Display, Handle, x, y);
        NativeX11.XFlush(Display);
    }

    public void CaptureMouse()
    {
        // TODO: XGrabPointer
    }

    public void ReleaseMouseCapture()
    {
        // TODO: XUngrabPointer
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

        _allowsTransparency = Window.AllowsTransparency;

        int screen = NativeX11.XDefaultScreen(Display);
        nint root = NativeX11.XRootWindow(Display, screen);

        uint dpi = _host.GetDpiForWindow(0);
        Window.SetDpi(dpi);
        double dpiScale = Window.DpiScale;
        var initialClientSize = GetInitialClientSize();

        uint width = (uint)Math.Max(1, (int)Math.Round(initialClientSize.Width * dpiScale));
        uint height = (uint)Math.Max(1, (int)Math.Round(initialClientSize.Height * dpiScale));

        int x = 0;
        int y = 0;
        if (Window.StartupLocation == WindowStartupLocation.CenterScreen &&
            NativeX11.XGetWindowAttributes(Display, root, out var rootAttrs) != 0)
        {
            x = Math.Max(0, (rootAttrs.width - (int)width) / 2);
            y = Math.Max(0, (rootAttrs.height - (int)height) / 2);
        }

        // Choose a GLX visual for OpenGL rendering and create the window with that visual.
        // When transparency is requested, prefer a 32-bit ARGB visual via FBConfig.
        const int GLX_X_RENDERABLE = 0x8012;
        const int GLX_DRAWABLE_TYPE = 0x8010;
        const int GLX_RENDER_TYPE = 0x8011;
        const int GLX_WINDOW_BIT = 0x00000001;
        const int GLX_RGBA_BIT = 0x00000001;
        const int GLX_ALPHA_SIZE = 11;
        const int GLX_DEPTH_SIZE = 12;
        const int GLX_STENCIL_SIZE = 13;

        XVisualInfo visualInfo = default;
        bool usedFbConfig = false;
        bool hasVisualInfo = false;

        if (_allowsTransparency)
        {
            int[] fbAttribs =
            {
                GLX_X_RENDERABLE, 1,
                GLX_DRAWABLE_TYPE, GLX_WINDOW_BIT,
                GLX_RENDER_TYPE, GLX_RGBA_BIT,
                4,  // GLX_RGBA
                5,  // GLX_DOUBLEBUFFER
                8,  // GLX_RED_SIZE
                8,
                9,  // GLX_GREEN_SIZE
                8,
                10, // GLX_BLUE_SIZE
                8,
                GLX_ALPHA_SIZE,
                8,
                GLX_DEPTH_SIZE,
                24,
                GLX_STENCIL_SIZE,
                8,
                0
            };

            nint fbConfigs = LibGL.glXChooseFBConfig(Display, screen, fbAttribs, out int fbCount);
            if (fbConfigs != 0 && fbCount > 0)
            {
                XVisualInfo? best = null;
                int bestStencil = -1;

                for (int i = 0; i < fbCount; i++)
                {
                    nint fb = Marshal.ReadIntPtr(fbConfigs, i * IntPtr.Size);
                    if (fb == 0)
                    {
                        continue;
                    }

                    int alphaSize = 0;
                    _ = LibGL.glXGetFBConfigAttrib(Display, fb, GLX_ALPHA_SIZE, out alphaSize);
                    if (alphaSize < 8)
                    {
                        continue;
                    }

                    int depthSize = 0;
                    _ = LibGL.glXGetFBConfigAttrib(Display, fb, GLX_DEPTH_SIZE, out depthSize);

                    int stencilSize = 0;
                    _ = LibGL.glXGetFBConfigAttrib(Display, fb, GLX_STENCIL_SIZE, out stencilSize);
                    if (depthSize < 16 || stencilSize < 8)
                    {
                        continue;
                    }

                    nint viPtr = LibGL.glXGetVisualFromFBConfig(Display, fb);
                    if (viPtr == 0)
                    {
                        continue;
                    }

                    var vi = Marshal.PtrToStructure<XVisualInfo>(viPtr);
                    NativeX11.XFree(viPtr);

                    if (vi.depth != 32)
                    {
                        continue;
                    }

                    // Prefer configs with a stencil buffer for rounded clip and path fills.
                    if (best == null || stencilSize > bestStencil)
                    {
                        best = vi;
                        bestStencil = stencilSize;
                    }
                }

                NativeX11.XFree(fbConfigs);

                if (best.HasValue)
                {
                    visualInfo = best.Value;
                    usedFbConfig = true;
                    hasVisualInfo = true;
                }
            }
        }

        if (!usedFbConfig)
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
                GLX_ALPHA_SIZE,
                8,
                GLX_DEPTH_SIZE,
                24,
                GLX_STENCIL_SIZE,
                8,
                0
            };

            nint visualInfoPtr;
            unsafe
            {
                fixed (int* p = attribs)
                {
                    visualInfoPtr = LibGL.glXChooseVisual(Display, screen, (nint)p);
                }
            }

            if (visualInfoPtr == 0)
            {
                // Last resort: allow a visual without stencil (rounded clip may not work).
                int[] attribsNoStencil =
                {
                    4,  // GLX_RGBA
                    5,  // GLX_DOUBLEBUFFER
                    8,  // GLX_RED_SIZE
                    8,
                    9,  // GLX_GREEN_SIZE
                    8,
                    10, // GLX_BLUE_SIZE
                    8,
                    GLX_ALPHA_SIZE,
                    8,
                    GLX_DEPTH_SIZE,
                    24,
                    0
                };

                unsafe
                {
                    fixed (int* p = attribsNoStencil)
                    {
                        visualInfoPtr = LibGL.glXChooseVisual(Display, screen, (nint)p);
                    }
                }

                if (visualInfoPtr == 0)
                {
                    throw new InvalidOperationException("glXChooseVisual failed.");
                }
            }

            visualInfo = Marshal.PtrToStructure<XVisualInfo>(visualInfoPtr);
            NativeX11.XFree(visualInfoPtr);
            hasVisualInfo = true;
        }

        if (!hasVisualInfo)
        {
            throw new InvalidOperationException("Failed to select a suitable X11 visual.");
        }
        _glxVisualInfo = new X11GlxVisualInfo(
            visualInfo.visual,
            visualInfo.visualid,
            visualInfo.screen,
            visualInfo.depth,
            visualInfo.@class,
            visualInfo.red_mask,
            visualInfo.green_mask,
            visualInfo.blue_mask,
            visualInfo.colormap_size,
            visualInfo.bits_per_rgb);

        const int AllocNone = 0;
        const ulong CWBackPixel = 1UL << 1;
        const ulong CWBorderPixel = 1UL << 3;
        const ulong CWEventMask = 1UL << 11;
        const ulong CWColormap = 1UL << 13;

        long windowEventMask =
            X11EventMask.ExposureMask | X11EventMask.StructureNotifyMask |
            X11EventMask.KeyPressMask | X11EventMask.KeyReleaseMask |
            X11EventMask.ButtonPressMask | X11EventMask.ButtonReleaseMask |
            X11EventMask.PointerMotionMask | X11EventMask.FocusChangeMask |
            X11EventMask.PropertyChangeMask;

        var attrs = new XSetWindowAttributes
        {
            colormap = NativeX11.XCreateColormap(Display, root, visualInfo.visual, AllocNone),
            event_mask = (nint)windowEventMask,
        };

        ulong valueMask = CWEventMask | CWColormap;
        if (_allowsTransparency)
        {
            attrs.background_pixel = 0;
            attrs.border_pixel = 0;
            valueMask |= CWBackPixel | CWBorderPixel;
        }

        Handle = NativeX11.XCreateWindow(
            Display,
            root,
            x, y,
            width, height,
            0,
            visualInfo.depth,
            1, // InputOutput
            visualInfo.visual,
            valueMask,
            ref attrs);

        if (Handle == 0)
        {
            throw new InvalidOperationException("XCreateWindow failed.");
        }

        DiagLog.Write($"X11 window created: display=0x{Display.ToInt64():X} window=0x{Handle.ToInt64():X} {width}x{height}");

        _host.RegisterWindow(Handle, this);
        Window.AttachBackend(this);
        Window.SetClientSizeDip(initialClientSize.Width, initialClientSize.Height);

        SetTitle(Window.Title);
        ApplyIcon();

        // WM_DELETE_WINDOW
        _wmProtocolsAtom = NativeX11.XInternAtom(Display, "WM_PROTOCOLS", false);
        _wmDeleteWindowAtom = NativeX11.XInternAtom(Display, "WM_DELETE_WINDOW", false);
        _atomAtom = NativeX11.XInternAtom(Display, "ATOM", false);
        _netWmWindowOpacityAtom = NativeX11.XInternAtom(Display, "_NET_WM_WINDOW_OPACITY", true);
        _cardinalAtom = NativeX11.XInternAtom(Display, "CARDINAL", false);
        _motifWmHintsAtom = NativeX11.XInternAtom(Display, "_MOTIF_WM_HINTS", false);
        _xdndAwareAtom = NativeX11.XInternAtom(Display, "XdndAware", false);
        _xdndEnterAtom = NativeX11.XInternAtom(Display, "XdndEnter", false);
        _xdndPositionAtom = NativeX11.XInternAtom(Display, "XdndPosition", false);
        _xdndStatusAtom = NativeX11.XInternAtom(Display, "XdndStatus", false);
        _xdndLeaveAtom = NativeX11.XInternAtom(Display, "XdndLeave", false);
        _xdndTypeListAtom = NativeX11.XInternAtom(Display, "XdndTypeList", false);
        _xdndActionCopyAtom = NativeX11.XInternAtom(Display, "XdndActionCopy", false);
        _xdndActionMoveAtom = NativeX11.XInternAtom(Display, "XdndActionMove", false);
        _xdndActionLinkAtom = NativeX11.XInternAtom(Display, "XdndActionLink", false);
        _xdndDropAtom = NativeX11.XInternAtom(Display, "XdndDrop", false);
        _xdndFinishedAtom = NativeX11.XInternAtom(Display, "XdndFinished", false);
        _xdndSelectionAtom = NativeX11.XInternAtom(Display, "XdndSelection", false);
        _textUriListAtom = NativeX11.XInternAtom(Display, "text/uri-list", false);
        _xdndSelectionPropertyAtom = NativeX11.XInternAtom(Display, "MEWUI_XDND_SELECTION", false);
        if (_wmProtocolsAtom != 0 && _wmDeleteWindowAtom != 0)
        {
            NativeX11.XSetWMProtocols(Display, Handle, ref _wmDeleteWindowAtom, 1);
        }

        ApplyXdndAware();

        // Apply transparency-related window hints after atoms are initialized.
        if (_allowsTransparency)
        {
            SetAllowsTransparency(_allowsTransparency);
        }

        ApplyOpacity();
        ApplyResizeMode();
        ApplyToolWindowHints();

        _inputMethod = X11InputMethodFactory.Create(Display, Handle);
        if (_inputMethod is XimInputMethod xim && xim.TryGetFilterEvents(out var imeFilterEvents))
        {
            windowEventMask |= imeFilterEvents.ToInt64();
            NativeX11.XSelectInput(Display, Handle, (nint)windowEventMask);
            ImeLogger.Write($"XSelectInput updated with IME filter events: 0x{windowEventMask:X}");
        }
        if (_inputMethod != null)
        {
            _inputMethod.CommitText += OnImeCommitText;
            _inputMethod.PreeditChanged += OnImePreeditChanged;
        }

        TryInitializeXI2();

        NeedsRender = true;
    }

    /// <summary>
    /// Initializes XInput2 for high-resolution scroll. Falls back silently when the
    /// extension is unavailable — legacy button 4–7 wheel events continue to work.
    /// </summary>
    private void TryInitializeXI2()
    {
        try
        {
            if (!NativeX11.XQueryExtension(Display, "XInputExtension", out int opcode, out _, out _))
                return;

            int major = 2, minor = 1;
            if (XI2.XIQueryVersion(Display, ref major, ref minor) != 0)
                return;

            // Subscribe to XI_Motion only. Including XI_ButtonPress in the mask causes some
            // X servers to stop delivering core button events without reliably delivering XI
            // button events — we lose both. Core buttons 4-7 stay on the legacy path and are
            // suppressed in HandleButton when XI2 scroll axes are active to avoid doubling.
            const int evtypeBits = 8;
            int maskBytes = (XI2.XI_Motion / evtypeBits) + 1;

            unsafe
            {
                Span<byte> maskBuffer = stackalloc byte[maskBytes];
                maskBuffer.Clear();
                maskBuffer[XI2.XI_Motion / evtypeBits] |= 1 << (XI2.XI_Motion % evtypeBits);

                fixed (byte* maskPtr = maskBuffer)
                {
                    var maskInfo = new XIEventMask
                    {
                        deviceid = XI2.XIAllDevices,
                        mask_len = maskBytes,
                        mask = (nint)maskPtr,
                    };
                    if (XI2.XISelectEvents(Display, Handle, [maskInfo], 1) != 0)
                        return;
                }
            }

            CacheXI2ScrollAxes();
            _xi2Opcode = opcode;
            _xi2Enabled = true;
            XI2Logger.Write($"XInput2 enabled: opcode={opcode} scrollAxes={_scrollAxes.Count}");
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    /// <summary>
    /// Walks <c>XIQueryDevice(XIAllDevices)</c> and records every scroll axis advertised
    /// by attached devices. The cache is keyed by (deviceId, valuatorNumber) so
    /// <see cref="HandleXI2Motion"/> can look axes up directly from a valuator index.
    /// </summary>
    private void CacheXI2ScrollAxes()
    {
        _scrollAxes.Clear();
        _xi2MasterHasScrollAxis = false;

        nint devicesPtr = XI2.XIQueryDevice(Display, XI2.XIAllDevices, out int deviceCount);
        if (devicesPtr == 0 || deviceCount <= 0)
            return;

        try
        {
            unsafe
            {
                int deviceInfoSize = Marshal.SizeOf<XIDeviceInfo>();
                for (int i = 0; i < deviceCount; i++)
                {
                    var device = Marshal.PtrToStructure<XIDeviceInfo>(devicesPtr + i * deviceInfoSize);
                    if (device.num_classes <= 0 || device.classes == 0)
                        continue;

                    // classes is XIAnyClassInfo** — an array of pointers to per-class headers.
                    nint* classPtrs = (nint*)device.classes;
                    for (int c = 0; c < device.num_classes; c++)
                    {
                        nint classPtr = classPtrs[c];
                        if (classPtr == 0) continue;
                        if (Marshal.ReadInt32(classPtr) != XI2.XIScrollClass) continue;

                        var scrollInfo = Marshal.PtrToStructure<XIScrollClassInfo>(classPtr);
                        if (scrollInfo.increment == 0) continue;

                        _scrollAxes[(device.deviceid, scrollInfo.number)] = new XI2ScrollAxis
                        {
                            ScrollType = scrollInfo.scroll_type,
                            Increment = scrollInfo.increment,
                        };
                        if (device.use == XI2.XIMasterPointer)
                            _xi2MasterHasScrollAxis = true;
                    }
                }
            }
        }
        finally
        {
            XI2.XIFreeDeviceInfo(devicesPtr);
        }
    }

    // Caller (PlatformHost) has already fetched the cookie via XGetEventData and owns the
    // matching XFreeEventData — do not re-fetch or free here.
    private void HandleXI2GenericEvent(ref XEvent ev)
    {
        if (ev.xcookie.data == 0) return;
        if (ev.xcookie.evtype == XI2.XI_Motion)
            HandleXI2Motion(ev.xcookie.data);
    }

    // XISelectEvents(XI_Motion) suppresses core MotionNotify on this window, so this handler
    // must drive mouse-over/move in addition to extracting scroll valuator deltas.
    private unsafe void HandleXI2Motion(nint dataPtr)
    {
        if (dataPtr == 0) return;

        var dev = Marshal.PtrToStructure<XIDeviceEvent>(dataPtr);

        // XIAllDevices delivers each physical motion twice — once via the slave
        // (deviceid==sourceid) and once via the master mirror. Skip slaves.
        if (dev.deviceid == dev.sourceid)
            return;

        var pos = new Point(dev.event_x / Window.DpiScale, dev.event_y / Window.DpiScale);
        bool leftDown = (dev.mods.effective & (int)X11ModifierMask.Button1) != 0;
        bool middleDown = (dev.mods.effective & (int)X11ModifierMask.Button2) != 0;
        bool rightDown = (dev.mods.effective & (int)X11ModifierMask.Button3) != 0;

        WindowInputRouter.MouseMove(Window, pos, ClientToScreen(pos), leftDown: leftDown, rightDown: rightDown, middleDown: middleDown);

        if (dev.valuators.mask_len <= 0 || dev.valuators.mask == 0 || dev.valuators.values == 0)
            return;

        double notchesY = 0;
        double notchesX = 0;
        byte* maskPtr = (byte*)dev.valuators.mask;
        double* valuesPtr = (double*)dev.valuators.values;
        int valueIndex = 0;

        int totalBits = dev.valuators.mask_len * 8;
        for (int v = 0; v < totalBits; v++)
        {
            bool set = (maskPtr[v / 8] & (1 << (v % 8))) != 0;
            if (!set) continue;

            double newValue = valuesPtr[valueIndex++];

            if (_scrollAxes.TryGetValue((dev.deviceid, v), out var axis))
            {
                if (!axis.HasLastValue)
                {
                    axis.LastValue = newValue;
                    axis.HasLastValue = true;
                    continue;
                }

                double deltaUnits = newValue - axis.LastValue;
                axis.LastValue = newValue;

                double notches = -deltaUnits / axis.Increment;

                if (axis.ScrollType == XI2.XIScrollTypeVertical)
                    notchesY += notches;
                else if (axis.ScrollType == XI2.XIScrollTypeHorizontal)
                    notchesX += notches;
            }
        }

        if (notchesY == 0 && notchesX == 0)
            return;

        WindowInputRouter.MouseWheel(
            Window, pos, ClientToScreen(pos),
            new Vector(notchesX, notchesY),
            leftDown, rightDown, middleDown);
    }

    private void ApplyResolvedStartupPosition()
    {
        if (Display == 0 || Handle == 0)
        {
            return;
        }

        Point? targetPosition = Window.StartupLocation switch
        {
            WindowStartupLocation.Manual => Window.ResolvedStartupPosition,
            WindowStartupLocation.CenterOwner => ResolveCenterOwnerStartupPosition(),
            WindowStartupLocation.CenterScreen => ResolveCenterScreenStartupPosition(),
            _ => null,
        };

        if (targetPosition is not { } position)
        {
            return;
        }

        bool userSpecified = Window.StartupLocation == WindowStartupLocation.Manual;
        ApplyStartupPositionHints(position, userSpecified);
        SetPosition(position.X, position.Y);
    }

    private Point? ResolveCenterScreenStartupPosition()
    {
        int screen = NativeX11.XDefaultScreen(Display);
        nint root = NativeX11.XRootWindow(Display, screen);
        if (NativeX11.XGetWindowAttributes(Display, root, out var rootAttrs) == 0)
        {
            return null;
        }

        double dpiScale = Window.DpiScale <= 0 ? 1.0 : Window.DpiScale;
        double left = Math.Max(0, (rootAttrs.width - Math.Round(Window.Width * dpiScale)) / 2.0) / dpiScale;
        double top = Math.Max(0, (rootAttrs.height - Math.Round(Window.Height * dpiScale)) / 2.0) / dpiScale;
        return new Point(left, top);
    }

    private Point? ResolveCenterOwnerStartupPosition()
    {
        if (Window.Owner is not { } owner || owner.Handle == 0)
        {
            return Window.ResolvedStartupPosition;
        }

        var ownerPosition = owner.Position;
        var ownerSize = owner.ClientSize;
        return new Point(
            ownerPosition.X + ((ownerSize.Width - Window.Width) * 0.5),
            ownerPosition.Y + ((ownerSize.Height - Window.Height) * 0.5));
    }

    private void ApplyStartupPositionHints(Point positionDip, bool userSpecified)
    {
        var hints = new XSizeHints();
        double dpiScale = Window.DpiScale <= 0 ? 1.0 : Window.DpiScale;
        hints.x = (int)Math.Round(positionDip.X * dpiScale);
        hints.y = (int)Math.Round(positionDip.Y * dpiScale);
        hints.flags = userSpecified ? XSizeHintsFlags.USPosition : XSizeHintsFlags.PPosition;
        NativeX11.XSetWMNormalHints(Display, Handle, ref hints);
    }

    private Size GetInitialClientSize()
    {
        var windowSize = Window.WindowSize;
        var currentClientSize = Window.ClientSize;

        double width = double.IsNaN(windowSize.Width) ? Math.Max(1, currentClientSize.Width) : windowSize.Width;
        double height = double.IsNaN(windowSize.Height) ? Math.Max(1, currentClientSize.Height) : windowSize.Height;

        return new Size(Math.Max(1, width), Math.Max(1, height));
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

        var cardinalAtom = _cardinalAtom;
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

    private void ApplyOpacity()
    {
        if (Display == 0 || Handle == 0 || _netWmWindowOpacityAtom == 0 || _cardinalAtom == 0)
        {
            return;
        }

        // X11 opacity is a 32-bit CARDINAL (0..0xFFFFFFFF). Requires a compositor to take effect.
        if (_opacity >= 0.999)
        {
            NativeX11.XDeleteProperty(Display, Handle, _netWmWindowOpacityAtom);
            NativeX11.XFlush(Display);
            return;
        }

        uint value32 = (uint)Math.Round(_opacity * uint.MaxValue);
        unsafe
        {
            nuint value = value32;
            NativeX11.XChangeProperty(
                display: Display,
                window: Handle,
                property: _netWmWindowOpacityAtom,
                type: _cardinalAtom,
                format: 32,
                mode: 0, // PropModeReplace
                data: (nint)(&value),
                nelements: 1);
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

        if (!ImageDecoders.TryDecode(src.EncodedBytes.Span, out var bmp))
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
            double dpiScale = Window.DpiScale > 0 ? Window.DpiScale : 1.0;
            var ws = Window.WindowSize;
            double minW = ws.MinWidth;
            double minH = ws.MinHeight;
            double maxW = ws.MaxWidth;
            double maxH = ws.MaxHeight;

            if (minW > 0 || minH > 0)
            {
                hints.flags |= XSizeHintsFlags.PMinSize;
                hints.min_width = minW > 0 ? (int)Math.Ceiling(minW * dpiScale) : 1;
                hints.min_height = minH > 0 ? (int)Math.Ceiling(minH * dpiScale) : 1;
            }

            if (!double.IsPositiveInfinity(maxW) || !double.IsPositiveInfinity(maxH))
            {
                hints.flags |= XSizeHintsFlags.PMaxSize;
                hints.max_width = !double.IsPositiveInfinity(maxW) ? (int)Math.Ceiling(maxW * dpiScale) : 32767;
                hints.max_height = !double.IsPositiveInfinity(maxH) ? (int)Math.Ceiling(maxH * dpiScale) : 32767;
            }
        }

        NativeX11.XSetWMNormalHints(Display, Handle, ref hints);

        // Clamp the current window size if it violates the new constraints.
        if (hints.flags != 0)
        {
            double dpiScale = Window.DpiScale > 0 ? Window.DpiScale : 1.0;
            int curW = (int)Math.Round(Window.Width * dpiScale);
            int curH = (int)Math.Round(Window.Height * dpiScale);
            int clampedW = curW;
            int clampedH = curH;

            if ((hints.flags & XSizeHintsFlags.PMinSize) != 0)
            {
                clampedW = Math.Max(clampedW, hints.min_width);
                clampedH = Math.Max(clampedH, hints.min_height);
            }
            if ((hints.flags & XSizeHintsFlags.PMaxSize) != 0)
            {
                clampedW = Math.Min(clampedW, hints.max_width);
                clampedH = Math.Min(clampedH, hints.max_height);
            }

            if (clampedW != curW || clampedH != curH)
            {
                NativeX11.XResizeWindow(Display, Handle, (uint)Math.Max(1, clampedW), (uint)Math.Max(1, clampedH));
            }
        }

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
            ProcessEvent(ref ev);
        }
    }

    internal void ProcessEvent(ref XEvent ev)
    {
        if (Window.HasNativeMessageHandler)
        {
            unsafe
            {
                fixed (XEvent* p = &ev)
                {
                    var hookArgs = new X11NativeMessageEventArgs(ev.type, (nint)p);
                    if (Window.RaiseNativeMessage(hookArgs))
                    {
                        return;
                    }
                }
            }
        }

        const int Expose = 12;
        const int DestroyNotify = 17;
        const int ConfigureNotify = 22;
        const int ClientMessage = 33;
        const int KeyPress = 2;
        const int KeyRelease = 3;
        const int ButtonPress = 4;
        const int ButtonRelease = 5;
        const int MotionNotify = 6;
        const int FocusIn = 9;
        const int FocusOut = 10;
        const int SelectionNotify = 31;
        const int PropertyNotify = 28;

        if (!_enabled && (ev.type == KeyPress || ev.type == KeyRelease || ev.type == ButtonPress || ev.type == ButtonRelease || ev.type == MotionNotify))
        {
            return;
        }

        if (_xi2Enabled && ev.type == X11EventType.GenericEvent && ev.xcookie.extension == _xi2Opcode)
        {
            HandleXI2GenericEvent(ref ev);
            return;
        }

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
                var widthDip = cfg.width / Window.DpiScale;
                var heightDip = cfg.height / Window.DpiScale;
                Window.SetClientSizeDip(widthDip, heightDip);
                Window.PerformLayout();
                Window.Invalidate();
                Window.RaiseClientSizeChanged(widthDip, heightDip);
                NeedsRender = true;
                break;

            case ClientMessage:
                unsafe
                {
                    var client = ev.xclient;
                    if (_xdndEnterAtom != 0 && client.message_type == _xdndEnterAtom)
                    {
                        HandleXdndEnter(client);
                        break;
                    }

                    if (_xdndPositionAtom != 0 && client.message_type == _xdndPositionAtom)
                    {
                        HandleXdndPosition(client);
                        break;
                    }

                    if (_xdndDropAtom != 0 && client.message_type == _xdndDropAtom)
                    {
                        HandleXdndDrop(client);
                        break;
                    }

                    if (_xdndLeaveAtom != 0 && client.message_type == _xdndLeaveAtom)
                    {
                        HandleXdndLeave();
                        break;
                    }

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

            case SelectionNotify:
                HandleXdndSelection(ev.xselection);
                break;

            case DestroyNotify:
                // Ensure we unregister and release resources even if the window is destroyed externally.
                RaiseClosedOnce();
                Cleanup(ev.xdestroywindow.window, destroyWindow: false);
                break;

            case KeyPress:
            case KeyRelease:
                bool isKeyDown = ev.type == KeyPress;
                var imeResult = _inputMethod?.ProcessKeyEvent(ref ev, isKeyDown)
                    ?? new X11ImeProcessResult(Handled: false, ForwardKeyToApp: true, CommittedText: null);

                if (isKeyDown)
                {
                    ImeLogger.Write($"[X11Key] im={_inputMethod?.GetType().Name ?? "null"} handled={imeResult.Handled} fwd={imeResult.ForwardKeyToApp} text='{imeResult.CommittedText ?? "(null)"}'");
                }

                if (imeResult.ForwardKeyToApp)
                {
                    HandleKey(ev.xkey, isDown: isKeyDown, imeHandled: imeResult.Handled);
                }

                // Deliver committed text AFTER KeyDown routing (preserves Tab/Enter suppression).
                if (isKeyDown && imeResult.CommittedText != null)
                {
                    DeliverCommittedTextFromIme(imeResult.CommittedText);
                }
                UpdateImeCursorRect();
                break;

            case ButtonPress:
            case ButtonRelease:
                HandleButton(ev.xbutton, isDown: ev.type == ButtonPress);
                if (ev.type == ButtonRelease)
                {
                    UpdateImeCursorRect();
                }

                break;

            case MotionNotify:
                HandleMotion(ev.xmotion);
                break;

            case FocusIn:
                SetWindowActive(true);
                _inputMethod?.OnFocusIn();
                UpdateImeCursorRect();
                break;

            case FocusOut:
                SetWindowActive(false);
                _inputMethod?.OnFocusOut();
                break;

            case PropertyNotify:
                HandlePropertyNotify(ev.xproperty);
                break;
        }
    }

    private void HandlePropertyNotify(in XPropertyEvent e)
    {
        var netWmState = NativeX11.XInternAtom(Display, "_NET_WM_STATE", false);
        if (netWmState == 0 || e.atom != netWmState)
        {
            return;
        }

        var atoms = ReadAtomProperty(Handle, netWmState);
        var maxH = NativeX11.XInternAtom(Display, "_NET_WM_STATE_MAXIMIZED_HORZ", false);
        var maxV = NativeX11.XInternAtom(Display, "_NET_WM_STATE_MAXIMIZED_VERT", false);
        var hidden = NativeX11.XInternAtom(Display, "_NET_WM_STATE_HIDDEN", false);

        bool isMaximized = maxH != 0 && maxV != 0 && atoms.Contains(maxH) && atoms.Contains(maxV);
        bool isMinimized = hidden != 0 && atoms.Contains(hidden);

        var newState = isMinimized ? Controls.WindowState.Minimized
            : isMaximized ? Controls.WindowState.Maximized
            : Controls.WindowState.Normal;

        if (newState != Window.WindowState)
        {
            Window.SetWindowStateFromBackend(newState);
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

    private void UpdateImeCursorRect()
    {
        if (_inputMethod == null)
        {
            return;
        }

        if (Window.FocusManager.FocusedElement is not ITextCompositionClient client)
        {
            return;
        }

        try
        {
            int caretPos = (client is Controls.TextBase tb) ? tb.CaretPosition : client.CompositionStartIndex;
            var rect = client.GetCharRectInWindow(caretPos);

            if (rect.Width <= 0 && rect.Height <= 0)
            {
                return;
            }

            _inputMethod.UpdateCursorRect(rect);
        }
        catch
        {
        }
    }

    private void HandleKey(XKeyEvent e, bool isDown, bool imeHandled)
    {
        if (Window.Content == null)
        {
            return;
        }

        var ks = NativeX11.XLookupKeysym(ref e, 0).ToInt64();
        var key = MapKeysymToKey(ks);
        var args = new KeyEventArgs(key, platformKey: (int)ks, modifiers: GetModifiers(e.state), isRepeat: false);

        if (isDown)
        {
            const long XK_F4 = 0xFFC1;
            if (ks == XK_F4 && args.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                Window.Close();
                return;
            }

            Window.RaisePreviewKeyDown(args);
            if (!args.Handled)
            {
                WindowInputRouter.KeyDown(Window, args);
            }

            Window.ProcessKeyBindings(args);
            Window.ProcessAccessKeyDown(args);

            if (!args.Handled && args.Key == Key.Tab)
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

            // Text is delivered after HandleKey returns, from imeResult.CommittedText.
            // When no IM is active, we extract text from the key event here as fallback.
            if (_inputMethod == null &&
                !args.Modifiers.HasFlag(ModifierKeys.Control) &&
                !args.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                string? committed = XimInputMethod.LookupStringWithoutIc(ref e);
                if (committed != null)
                {
                    DeliverCommittedText(committed, args);
                }
            }
        }
        else
        {
            Window.RaisePreviewKeyUp(args);
            if (!args.Handled)
            {
                WindowInputRouter.KeyUp(Window, args);
            }

            Window.ProcessAccessKeyUp(args);
            Window.RequerySuggested();
        }
    }

    private void DeliverCommittedTextFromIme(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Filter control characters (except newlines).
        if (text.Length == 1 && char.IsControl(text[0]) && text[0] != '\r' && text[0] != '\n')
        {
            ImeLogger.Write($"[X11Deliver] filtered control char 0x{(int)text[0]:X}");
            return;
        }

        var ti = new TextInputEventArgs(text);
        Window.RaisePreviewTextInput(ti);
        if (ti.Handled)
        {
            ImeLogger.Write($"[X11Deliver] '{text}' handled by PreviewTextInput");
            return;
        }

        if (Window.FocusManager.FocusedElement is ITextInputClient client)
        {
            ImeLogger.Write($"[X11Deliver] '{text}' -> {client.GetType().Name}");
            client.HandleTextInput(ti);
        }
        else
        {
            ImeLogger.Write($"[X11Deliver] '{text}' -> NO ITextInputClient (focused={Window.FocusManager.FocusedElement?.GetType().Name ?? "null"})");
        }
    }

    private void OnImeCommitText(string text)
    {
        // For async commits (signals arriving outside ProcessKeyEvent).
        DeliverCommittedTextFromIme(text);
    }

    private void OnImePreeditChanged(X11PreeditState state)
    {
        if (Window.FocusManager.FocusedElement is not ITextCompositionClient client)
        {
            ImeLogger.Write($"[X11Preedit] text='{state.Text}' isEnd={state.IsEnd} -> NO ITextCompositionClient");
            return;
        }

        if (state.IsEnd)
        {
            ImeLogger.Write($"[X11Preedit] END composing={client.IsComposing}");
            if (client.IsComposing)
            {
                client.HandleTextCompositionEnd(new TextCompositionEventArgs());
            }
        }
        else if (string.IsNullOrEmpty(state.Text))
        {
            ImeLogger.Write($"[X11Preedit] EMPTY composing={client.IsComposing}");
            if (client.IsComposing)
            {
                client.HandleTextCompositionEnd(new TextCompositionEventArgs());
            }
        }
        else
        {
            if (!client.IsComposing)
            {
                ImeLogger.Write($"[X11Preedit] START+UPDATE '{state.Text}' -> {client.GetType().Name}");
                // Start initializes the composition range but does NOT insert text.
                // Follow up with Update so the preedit text actually appears.
                // This is critical for Korean input where CommitText + UpdatePreeditText
                // arrive in the same drain cycle: after the commit ends composition,
                // the next preedit arrives with IsComposing=false and needs both Start and Update.
                var args = new TextCompositionEventArgs(state.Text, state.Attributes);
                client.HandleTextCompositionStart(args);
                if (!args.Handled)
                {
                    client.HandleTextCompositionUpdate(new TextCompositionEventArgs(state.Text, state.Attributes));
                }
            }
            else
            {
                ImeLogger.Write($"[X11Preedit] UPDATE '{state.Text}' -> {client.GetType().Name}");
                client.HandleTextCompositionUpdate(new TextCompositionEventArgs(state.Text, state.Attributes));
            }
        }

        UpdateImeCursorRect();
    }

    private void DeliverCommittedText(string text, KeyEventArgs keyDownArgs)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        if (TextInputSuppression.ShouldSuppressCommittedText(keyDownArgs, text))
        {
            return;
        }

        if (text.Length == 1 && char.IsControl(text[0]) && text[0] != '\r' && text[0] != '\n')
        {
            return;
        }

        var ti = new TextInputEventArgs(text);
        Window.RaisePreviewTextInput(ti);
        if (!ti.Handled)
        {
            if (Window.FocusManager.FocusedElement is ITextInputClient client)
            {
                client.HandleTextInput(ti);
            }
        }
    }

    private void HandleButton(XButtonEvent e, bool isDown)
    {
        int xPx = e.x;
        int yPx = e.y;
        var pos = new Point(xPx / Window.DpiScale, yPx / Window.DpiScale);

        if (e.button is X11MouseButton.WheelUp or X11MouseButton.WheelDown
                     or X11MouseButton.WheelLeft or X11MouseButton.WheelRight)
        {
            if (!isDown)
            {
                return;
            }

            // When XI_Motion is delivering high-res scroll for a master pointer with scroll
            // axes, the X server still emits emulated core button 4-7 alongside it. Drop the
            // legacy path to avoid doubled wheel deltas. Gated on master (not any device) so
            // setups where only slaves expose scroll classes still get wheel via legacy.
            if (_xi2Enabled && _xi2MasterHasScrollAxis)
            {
                return;
            }

            var wheelElement = WindowInputRouter.HitTest(Window, pos);
            WindowInputRouter.UpdateMouseOver(Window, wheelElement);

            // X11 synthetic wheel buttons: each press = exactly one notch.
            // Sign convention matches MouseWheelEventArgs.Delta (+Y up, +X left).
            Vector delta = e.button switch
            {
                X11MouseButton.WheelUp => new Vector(0, +1.0),
                X11MouseButton.WheelDown => new Vector(0, -1.0),
                X11MouseButton.WheelLeft => new Vector(+1.0, 0),
                X11MouseButton.WheelRight => new Vector(-1.0, 0),
                _ => default,
            };

            bool leftDown = (e.state & X11ModifierMask.Button1) != 0;
            bool middleDown = (e.state & X11ModifierMask.Button2) != 0;
            bool rightDown = (e.state & X11ModifierMask.Button3) != 0;

            WindowInputRouter.MouseWheel(Window, pos, ClientToScreen(pos), delta, leftDown, rightDown, middleDown);

            return;
        }

        var btn = e.button switch
        {
            X11MouseButton.Left => MouseButton.Left,
            X11MouseButton.Middle => MouseButton.Middle,
            X11MouseButton.Right => MouseButton.Right,
            _ => MouseButton.Left,
        };

        bool left = (e.state & X11ModifierMask.Button1) != 0;
        bool middle = (e.state & X11ModifierMask.Button2) != 0;
        bool right = (e.state & X11ModifierMask.Button3) != 0;

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
                int maxDist = (int)Math.Round(4 * Window.DpiScale);
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

        var screenPos = ClientToScreen(pos);
        if (isDown && !Window.IsActive)
        {
            SetWindowActive(true);
        }

        WindowInputRouter.MouseButton(
            Window,
            pos,
            screenPos,
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
        var screenPos = ClientToScreen(pos);

        bool left = (e.state & X11ModifierMask.Button1) != 0;
        bool middle = (e.state & X11ModifierMask.Button2) != 0;
        bool right = (e.state & X11ModifierMask.Button3) != 0;

        WindowInputRouter.MouseMove(Window, pos, screenPos, leftDown: left, rightDown: right, middleDown: middle);
    }

    private unsafe void HandleXdndEnter(XClientMessageEvent client)
    {
        _xdndSourceWindow = (nint)client.data[0];
        _xdndVersion = ((ulong)client.data[1] >> 24) & 0xFF;
        _xdndOfferedTypes.Clear();
        _xdndEnterDispatched = false;
        _xdndLastEffect = DragDropEffects.None;

        bool usesTypeList = (client.data[1] & 1) != 0;
        if (usesTypeList)
        {
            foreach (var atom in ReadAtomProperty(_xdndSourceWindow, _xdndTypeListAtom))
            {
                _xdndOfferedTypes.Add(atom);
            }
        }
        else
        {
            for (int i = 2; i <= 4; i++)
            {
                if (client.data[i] != 0)
                {
                    _xdndOfferedTypes.Add((nint)client.data[i]);
                }
            }
        }
    }

    private unsafe void HandleXdndPosition(XClientMessageEvent client)
    {
        _xdndSourceWindow = (nint)client.data[0];
        long packed = client.data[2];
        _xdndLastRootX = (short)((packed >> 16) & 0xFFFF);
        _xdndLastRootY = (short)(packed & 0xFFFF);
        _xdndLastDropTime = (nint)client.data[3];

        // Build a lightweight (format-keys-only) IDataObject so element-level routing can decide accept
        // based on the offered types. The real payload becomes available only after XdndDrop + selection conversion.
        var args = BuildXdndPositionArgs();
        if (_xdndEnterDispatched)
        {
            WindowDragDropRouter.OnExternalDragOver(Window, args);
        }
        else
        {
            WindowDragDropRouter.OnExternalDragEnter(Window, args);
            _xdndEnterDispatched = true;
        }

        _xdndLastEffect = args.Accepted ? args.Effect : DragDropEffects.None;
        SendXdndStatus(_xdndLastEffect);
    }

    private void HandleXdndLeave()
    {
        if (_xdndEnterDispatched)
        {
            var args = BuildXdndPositionArgs();
            WindowDragDropRouter.OnExternalDragLeave(Window, args);
        }
        ResetXdndState();
    }

    private unsafe void HandleXdndDrop(XClientMessageEvent client)
    {
        _xdndSourceWindow = (nint)client.data[0];
        if (client.data[2] != 0)
        {
            _xdndLastDropTime = (nint)client.data[2];
        }

        if (!AcceptsXdndDrop())
        {
            SendXdndFinished(false);
            ResetXdndState();
            return;
        }

        _ = NativeX11.XConvertSelection(
            Display,
            _xdndSelectionAtom,
            _textUriListAtom,
            _xdndSelectionPropertyAtom,
            Handle,
            _xdndLastDropTime);
        NativeX11.XFlush(Display);
    }

    // Lightweight args for enter/over time: format keys present, no payload (X11 selection-conversion model).
    private DragEventArgs BuildXdndPositionArgs()
    {
        var emptyFormats = new Dictionary<string, object>(capacity: 1);
        if (_textUriListAtom != 0 && _xdndOfferedTypes.Contains(_textUriListAtom))
        {
            emptyFormats[StandardDataFormats.StorageItems] = Array.Empty<string>();
        }

        var data = new DataObject(emptyFormats);
        var position = TranslateRootToClient(_xdndLastRootX, _xdndLastRootY);
        return new DragEventArgs(
            data,
            new Point(position.x / Window.DpiScale, position.y / Window.DpiScale),
            new Point(_xdndLastRootX, _xdndLastRootY),
            DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
    }

    private void HandleXdndSelection(XSelectionEvent selection)
    {
        if (selection.requestor != Handle || selection.selection != _xdndSelectionAtom)
        {
            return;
        }

        bool success = false;
        try
        {
            if (selection.property == 0)
            {
                return;
            }

            var paths = ReadXdndPaths(selection.property);
            if (paths.Count == 0)
            {
                return;
            }

            var position = TranslateRootToClient(_xdndLastRootX, _xdndLastRootY);
            var data = new DataObject(new Dictionary<string, object>
            {
                [StandardDataFormats.StorageItems] = paths,
            });

            var args = new DragEventArgs(
                data,
                new Point(position.x / Window.DpiScale, position.y / Window.DpiScale),
                new Point(_xdndLastRootX, _xdndLastRootY),
                DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);

            var effect = WindowDragDropRouter.OnExternalDrop(Window, args);
            success = effect != DragDropEffects.None || args.Handled;
            _xdndLastEffect = effect;
        }
        finally
        {
            if (selection.property != 0)
            {
                NativeX11.XDeleteProperty(Display, Handle, selection.property);
            }

            SendXdndFinished(success);
            ResetXdndState();
        }
    }

    private bool AcceptsXdndDrop()
        => _textUriListAtom != 0 && _xdndOfferedTypes.Contains(_textUriListAtom);

    private unsafe void SendXdndStatus(DragDropEffects effect)
    {
        if (_xdndSourceWindow == 0 || _xdndStatusAtom == 0)
        {
            return;
        }

        // Pick a single action atom — Xdnd carries one action at a time.
        nint actionAtom = 0;
        if ((effect & DragDropEffects.Move) != 0) actionAtom = _xdndActionMoveAtom;
        else if ((effect & DragDropEffects.Copy) != 0) actionAtom = _xdndActionCopyAtom;
        else if ((effect & DragDropEffects.Link) != 0) actionAtom = _xdndActionLinkAtom;

        bool accept = actionAtom != 0;

        XEvent ev = default;
        ev.xclient.type = 33;
        ev.xclient.display = Display;
        ev.xclient.window = _xdndSourceWindow;
        ev.xclient.message_type = _xdndStatusAtom;
        ev.xclient.format = 32;
        ev.xclient.data[0] = Handle;
        ev.xclient.data[1] = accept ? 1 : 0;
        ev.xclient.data[2] = 0;
        ev.xclient.data[3] = 0;
        ev.xclient.data[4] = actionAtom;
        _ = NativeX11.XSendEvent(Display, _xdndSourceWindow, false, 0, ref ev);
        NativeX11.XFlush(Display);
    }

    private unsafe void SendXdndFinished(bool success)
    {
        if (_xdndSourceWindow == 0 || _xdndFinishedAtom == 0)
        {
            return;
        }

        XEvent ev = default;
        ev.xclient.type = 33;
        ev.xclient.display = Display;
        ev.xclient.window = _xdndSourceWindow;
        ev.xclient.message_type = _xdndFinishedAtom;
        ev.xclient.format = 32;
        ev.xclient.data[0] = Handle;
        ev.xclient.data[1] = success ? 1 : 0;
        ev.xclient.data[2] = success ? _xdndActionCopyAtom : 0;
        _ = NativeX11.XSendEvent(Display, _xdndSourceWindow, false, 0, ref ev);
        NativeX11.XFlush(Display);
    }

    private void ResetXdndState()
    {
        _xdndSourceWindow = 0;
        _xdndOfferedTypes.Clear();
        _xdndVersion = 0;
        _xdndLastRootX = 0;
        _xdndLastRootY = 0;
        _xdndLastDropTime = 0;
        _xdndEnterDispatched = false;
        _xdndLastEffect = DragDropEffects.None;
    }

    /// <inheritdoc/>
    public void SetAllowDrop(bool allow)
    {
        if (_allowDrop == allow) return;
        _allowDrop = allow;
        if (Handle != 0)
        {
            ApplyXdndAware();
        }
    }

    private void ApplyXdndAware()
    {
        if (Handle == 0 || _xdndAwareAtom == 0) return;
        if (_allowDrop)
        {
            if (_atomAtom == 0) return;
            unsafe
            {
                nint xdndVersion = (nint)5;
                NativeX11.XChangeProperty(Display, Handle, _xdndAwareAtom, _atomAtom, 32, 0, (nint)(&xdndVersion), 1);
            }
        }
        else
        {
            NativeX11.XDeleteProperty(Display, Handle, _xdndAwareAtom);
        }
    }

    private List<string> ReadXdndPaths(nint property)
    {
        const nint AnyPropertyType = 0;
        int status = NativeX11.XGetWindowProperty(
            Display,
            Handle,
            property,
            0,
            64 * 1024,
            false,
            AnyPropertyType,
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

            return [];
        }

        try
        {
            unsafe
            {
                var bytes = new ReadOnlySpan<byte>((void*)prop, checked((int)nitems));
                return ParseUriList(bytes);
            }
        }
        finally
        {
            NativeX11.XFree(prop);
        }
    }

    private List<nint> ReadAtomProperty(nint window, nint property)
    {
        const nint AnyPropertyType = 0;
        int status = NativeX11.XGetWindowProperty(
            Display,
            window,
            property,
            0,
            1024,
            false,
            AnyPropertyType,
            out _,
            out int actualFormat,
            out nuint nitems,
            out _,
            out nint prop);

        if (status != 0 || prop == 0 || actualFormat != 32 || nitems == 0)
        {
            if (prop != 0)
            {
                NativeX11.XFree(prop);
            }

            return [];
        }

        try
        {
            var result = new List<nint>(checked((int)nitems));
            unsafe
            {
                if (IntPtr.Size == 8)
                {
                    var atoms = new ReadOnlySpan<long>((void*)prop, checked((int)nitems));
                    foreach (var atom in atoms)
                    {
                        if (atom != 0)
                        {
                            result.Add((nint)atom);
                        }
                    }
                }
                else
                {
                    var atoms = new ReadOnlySpan<int>((void*)prop, checked((int)nitems));
                    foreach (var atom in atoms)
                    {
                        if (atom != 0)
                        {
                            result.Add((nint)atom);
                        }
                    }
                }
            }

            return result;
        }
        finally
        {
            NativeX11.XFree(prop);
        }
    }

    private (int x, int y) TranslateRootToClient(int rootX, int rootY)
    {
        int screen = NativeX11.XDefaultScreen(Display);
        nint root = NativeX11.XRootWindow(Display, screen);
        _ = NativeX11.XTranslateCoordinates(Display, root, Handle, rootX, rootY, out int clientX, out int clientY, out _);
        return (clientX, clientY);
    }

    private static List<string> ParseUriList(ReadOnlySpan<byte> bytes)
    {
        string text = System.Text.Encoding.UTF8.GetString(bytes);
        var result = new List<string>();
        foreach (var rawLine in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (rawLine.Length == 0 || rawLine[0] == '#')
            {
                continue;
            }

            if (!Uri.TryCreate(rawLine, UriKind.Absolute, out var uri) || !uri.IsFile)
            {
                continue;
            }

            string path = uri.LocalPath;
            if (!string.IsNullOrWhiteSpace(path))
            {
                result.Add(path);
            }
        }

        return result;
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
        // Function keys
        if (keysym is >= 0xFFBE and <= 0xFFD5) // XK_F1..XK_F24
        {
            return (Key)((int)Key.F1 + (int)(keysym - 0xFFBE));
        }

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
        if (_glxVisualInfo is not { } visualInfo)
        {
            return;
        }

        var client = Window.ClientSize;
        int pixelWidth = (int)Math.Max(1, Math.Ceiling(client.Width * Window.DpiScale));
        int pixelHeight = (int)Math.Max(1, Math.Ceiling(client.Height * Window.DpiScale));
        var surface = new X11GlxWindowSurface(Display, Handle, visualInfo, Window.DpiScale, pixelWidth, pixelHeight);
        Window.RenderFrame(surface);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        Window.ClearMouseOverState();
        Window.ClearMouseCaptureState();

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

        try
        {
            if (_currentCursor != 0)
            {
                NativeX11.XFreeCursor(Display, _currentCursor);
                _currentCursor = 0;
            }

            if (_inputMethod != null)
            {
                _inputMethod.CommitText -= OnImeCommitText;
                _inputMethod.PreeditChanged -= OnImePreeditChanged;
                _inputMethod.Dispose();
                _inputMethod = null;
            }
        }
        catch
        {
        }

        // Release graphics resources BEFORE XDestroyWindow. The GL teardown calls
        // glXMakeCurrent + GL.DeleteTextures to free the context's tracked textures and
        // text cache; both internally validate the bound drawable. If the X window is
        // already destroyed, glXMakeCurrent on the dead drawable triggers BadDrawable
        // (X_GetGeometry, opcode 14) on WSLg's GLX implementation, even though the
        // process otherwise exits cleanly. Destroying GL state first keeps the drawable
        // valid for the unbind call.
        try
        {
            Window.ReleaseWindowGraphicsResources(handle);
        }
        catch { }

        if (destroyWindow)
        {
            try { NativeX11.XDestroyWindow(Display, handle); }
            catch { }
        }

        try { _host.UnregisterWindow(handle); } catch { }
        try { Window.DisposeVisualTree(); } catch { }

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

    public void CenterOnOwner()
    {
        if (Display == 0 || Handle == 0 || Window.Owner is not { } ownerWindow || ownerWindow.Handle == 0)
        {
            return;
        }

        var ownerPos = ownerWindow.Position;
        var ownerSize = ownerWindow.ClientSize;
        double x = ownerPos.X + (ownerSize.Width - Window.Width) / 2;
        double y = ownerPos.Y + (ownerSize.Height - Window.Height) / 2;
        SetPosition(x, y);
    }

    public void EnsureTheme(bool isDark)
    {
    }

    private void SetWindowActive(bool active)
    {
        if (Window.IsActive == active)
        {
            return;
        }

        Window.SetIsActive(active);
        if (active)
        {
            Window.RaiseActivated();
        }
        else
        {
            Window.RaiseDeactivated();
        }
    }

    public void Activate()
    {
        if (Display == 0 || Handle == 0)
        {
            return;
        }

        try
        {
            NativeX11.XRaiseWindow(Display, Handle);
            if (NativeX11.XGetWindowAttributes(Display, Handle, out var attrs) != 0 && attrs.map_state == 2 /*IsViewable*/)
            {
                NativeX11.XSetInputFocus(Display, Handle, revert_to: 1, time: 0); // RevertToPointerRoot=1
            }
            NativeX11.XFlush(Display);
        }
        catch
        {
            // Best-effort.
        }
    }

    public void SetOwner(nint ownerHandle)
    {
        if (Display == 0 || Handle == 0)
        {
            return;
        }

        if (ownerHandle == 0)
        {
            return;
        }

        try
        {
            NativeX11.XSetTransientForHint(Display, Handle, ownerHandle);

            // EWMH modal hint — ONLY for true modal dialogs (ShowDialog). A plain owned window (Show(owner))
            // must not be modal, otherwise the WM/compositor (e.g. XWayland) dims the owner and treats it as a
            // dialog. Setting transient-for alone is enough to keep an owned window above its owner. Best-effort.
            if (Window.IsDialogWindow)
            {
                var netWmState = NativeX11.XInternAtom(Display, "_NET_WM_STATE", false);
                var netWmStateModal = NativeX11.XInternAtom(Display, "_NET_WM_STATE_MODAL", false);
                if (netWmState != 0 && netWmStateModal != 0)
                {
                    unsafe
                    {
                        nint data = netWmStateModal;
                        NativeX11.XChangeProperty(Display, Handle, netWmState, type: 4 /*ATOM*/, format: 32, mode: 0 /*Replace*/, (nint)(&data), nelements: 1);
                    }
                }
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    // Marks the window as a floating tool/utility window: _NET_WM_WINDOW_TYPE_UTILITY (thin WM decoration) +
    // skip-taskbar. Set as properties before the window is mapped (a _NET_WM_STATE ClientMessage only applies
    // once the WM manages the window). Owner/transient is set separately via SetOwner. Best-effort — WMs vary
    // in utility-type support, degrading to a normal (skip-taskbar) window. Mutually exclusive with transparency.
    private void ApplyToolWindowHints()
    {
        if (Display == 0 || Handle == 0 || !Window.IsToolWindow || _allowsTransparency)
        {
            return;
        }

        try
        {
            var windowType = NativeX11.XInternAtom(Display, "_NET_WM_WINDOW_TYPE", false);
            var windowTypeUtility = NativeX11.XInternAtom(Display, "_NET_WM_WINDOW_TYPE_UTILITY", false);
            if (windowType != 0 && windowTypeUtility != 0)
            {
                unsafe
                {
                    nint data = windowTypeUtility;
                    NativeX11.XChangeProperty(Display, Handle, windowType, type: 4 /*ATOM*/, format: 32, mode: 0 /*Replace*/, (nint)(&data), nelements: 1);
                }
            }

            var netWmState = NativeX11.XInternAtom(Display, "_NET_WM_STATE", false);
            var skipTaskbar = NativeX11.XInternAtom(Display, "_NET_WM_STATE_SKIP_TASKBAR", false);
            if (netWmState != 0 && skipTaskbar != 0)
            {
                unsafe
                {
                    nint data = skipTaskbar;
                    NativeX11.XChangeProperty(Display, Handle, netWmState, type: 4 /*ATOM*/, format: 32, mode: 0 /*Replace*/, (nint)(&data), nelements: 1);
                }
            }
        }
        catch
        {
            // Best-effort — WMs vary in utility-type support.
        }
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled)
        {
            Window.ClearMouseOverState();
            Window.ClearMouseCaptureState();
        }
    }

    public void SetOpacity(double opacity)
    {
        _opacity = Math.Clamp(opacity, 0.0, 1.0);
        ApplyOpacity();
    }

    // X11 WM removes all decorations (title bar + border + shadow) — not a true
    // "extend client area" like Win32/macOS. Reported as None so NativeCustomWindow
    // keeps the default title bar on X11.
    public WindowChromeCapabilities ChromeCapabilities =>
        WindowChromeCapabilities.None;

    public void SetAllowsTransparency(bool allowsTransparency)
    {
        _allowsTransparency = allowsTransparency;
        if (Display == 0 || Handle == 0 || _motifWmHintsAtom == 0)
        {
            return;
        }

        try
        {
            var hints = new MotifWmHints
            {
                flags = MotifFlags.Decorations,
                functions = 0,
                decorations = allowsTransparency ? 0u : 1u,
                inputMode = 0,
                status = 0
            };

            unsafe
            {
                NativeX11.XChangeProperty(
                    Display,
                    Handle,
                    _motifWmHintsAtom,
                    _motifWmHintsAtom,
                    format: 32,
                    mode: 0 /*Replace*/,
                    (nint)(&hints),
                    nelements: 5);
            }

            NativeX11.XFlush(Display);
        }
        catch
        {
            // Best-effort.
        }
    }

    public void SetCursor(CursorType cursorType)
    {
        if (Display == 0 || Handle == 0)
        {
            return;
        }

        // X11 standard cursor font shape constants (X11/cursorfont.h)
        const uint XC_left_ptr = 68;
        const uint XC_xterm = 152;
        const uint XC_watch = 150;
        const uint XC_crosshair = 34;
        const uint XC_sb_h_double_arrow = 108;
        const uint XC_sb_v_double_arrow = 116;
        const uint XC_fleur = 52;
        const uint XC_X_cursor = 0;
        const uint XC_hand2 = 60;
        const uint XC_question_arrow = 92;
        const uint XC_top_left_corner = 134;
        const uint XC_top_right_corner = 136;
        const uint XC_center_ptr = 22;

        uint shape = cursorType switch
        {
            CursorType.Arrow => XC_left_ptr,
            CursorType.IBeam => XC_xterm,
            CursorType.Wait => XC_watch,
            CursorType.Cross => XC_crosshair,
            CursorType.UpArrow => XC_center_ptr,
            CursorType.SizeNWSE => XC_top_left_corner,
            CursorType.SizeNESW => XC_top_right_corner,
            CursorType.SizeWE => XC_sb_h_double_arrow,
            CursorType.SizeNS => XC_sb_v_double_arrow,
            CursorType.SizeAll => XC_fleur,
            CursorType.No => XC_X_cursor,
            CursorType.Hand => XC_hand2,
            CursorType.Help => XC_question_arrow,
            _ => XC_left_ptr,
        };

        try
        {
            nint newCursor = NativeX11.XCreateFontCursor(Display, shape);
            if (newCursor != 0)
            {
                NativeX11.XDefineCursor(Display, Handle, newCursor);

                if (_currentCursor != 0)
                {
                    NativeX11.XFreeCursor(Display, _currentCursor);
                }

                _currentCursor = newCursor;
                NativeX11.XFlush(Display);
            }
        }
        catch
        {
            // Best-effort.
        }
    }

    public void SetImeMode(ImeMode mode)
    {
        if (_inputMethod == null)
        {
            return;
        }

        if (mode == ImeMode.Disabled)
        {
            _inputMethod.OnFocusOut();
        }
        else
        {
            _inputMethod.OnFocusIn();
        }
    }

    public void CancelImeComposition()
    {
        // Commit rather than reset — Reset() tells IBus to discard preedit,
        // which triggers HidePreeditText → EndTextCompositionInternal → text loss.
        // Instead, commit the current composition so text is preserved with undo.
        if (Window.FocusManager.FocusedElement is ITextCompositionClient { IsComposing: true } client)
        {
            if (client is Controls.TextBase tb)
            {
                tb.CommitTextCompositionInternal();
            }
            else
            {
                client.HandleTextCompositionEnd(new TextCompositionEventArgs());
            }
        }

        // Reset the IME after committing so it starts clean on next focus.
        _inputMethod?.Reset();
    }

    private sealed class X11GlxWindowSurface : IX11GlxWindowSurface
    {
        public nint Handle => Window;

        public int PixelWidth { get; }

        public int PixelHeight { get; }

        public double DpiScale { get; }

        // X11 needs both the Display* and the integer screen number to uniquely identify an
        // output. Display* goes into NativeHandle (and IdLow for structural equality);
        // VisualInfo.Screen rides in IdHigh as the secondary index. Core type stays platform-
        // neutral; the X11-specific packing lives here.
        public PlatformDisplayIdentity DisplayIdentity =>
            Display == 0 ? default : new PlatformDisplayIdentity((ulong)Display, VisualInfo.Screen, Display);

        public nint Display { get; }

        public nint Window { get; }

        public X11GlxVisualInfo VisualInfo { get; }

        public X11GlxWindowSurface(nint display, nint window, X11GlxVisualInfo visualInfo, double dpiScale, int pixelWidth, int pixelHeight)
        {
            Display = display;
            Window = window;
            VisualInfo = visualInfo;
            DpiScale = dpiScale <= 0 ? 1.0 : dpiScale;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct MotifWmHints
{
    public uint flags;
    public uint functions;
    public uint decorations;
    public int inputMode;
    public uint status;
}

internal static class MotifFlags
{
    public const uint Decorations = 1u << 1;
}
