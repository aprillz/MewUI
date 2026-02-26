using System.Buffers;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

using Aprillz.MewUI.Input;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Platform.Win32;

[SupportedOSPlatform("windows")]
internal sealed class Win32WindowBackend : IWindowBackend
{
    private readonly Win32PlatformHost _host;
    internal Window Window { get; }

    private readonly ClickCountTracker _clickCountTracker = new();
    private readonly int[] _lastPressClickCounts = new int[5];
    private nint _hIconSmall;
    private nint _hIconBig;
    private bool _initialEraseDone;
    private double _opacity = 1.0;
    private bool _allowsTransparency;

    private readonly TextInputSuppression _textInputSuppression = new();

    internal bool NeedsRender { get; private set; }

    public nint Handle { get; private set; }

    private readonly Win32TitleBarThemeSynchronizer _titleBarThemeSync = new();

    internal Win32WindowBackend(Win32PlatformHost host, Window window)
    {
        _host = host;
        Window = window;
    }

    public void SetResizable(bool resizable)
    {
        if (Handle == 0)
        {
            return;
        }

        ApplyResizeMode();

        // Clamp the current window size if it violates the new constraints.
        var ws = Window.WindowSize;
        double curW = Window.Width;
        double curH = Window.Height;
        double clampedW = Math.Clamp(curW, ws.MinWidth, ws.MaxWidth);
        double clampedH = Math.Clamp(curH, ws.MinHeight, ws.MaxHeight);
        if (clampedW != curW || clampedH != curH)
        {
            SetClientSize(clampedW, clampedH);
        }
    }

    public void Show()
    {
        if (Handle != 0)
        {
            User32.ShowWindow(Handle, ShowWindowCommands.SW_SHOW);
            return;
        }

        CreateWindow();
        User32.ShowWindow(Handle, ShowWindowCommands.SW_SHOW);
        User32.UpdateWindow(Handle);
    }

    public void Hide()
    {
        if (Handle != 0)
        {
            User32.ShowWindow(Handle, ShowWindowCommands.SW_HIDE);
        }
    }

    public void Close()
    {
        if (Handle != 0)
        {
            // Route programmatic close through WM_CLOSE so Window.RaiseClosed() runs,
            // ensuring modal ownership state and ShowDialogAsync completion are handled.
            _ = User32.PostMessage(Handle, WindowMessages.WM_CLOSE, 0, 0);
        }
    }

    public void Invalidate(bool erase)
    {
        if (Handle == 0)
        {
            return;
        }

        // Request-driven rendering: coalesce multiple invalidations and let the platform host render pass present.
        NeedsRender = true;
        _host.RequestRender();
    }

    public void SetOpacity(double opacity)
    {
        _opacity = Math.Clamp(opacity, 0.0, 1.0);
        if (Handle == 0)
        {
            return;
        }

        EnsureLayeredStyleIfNeeded();
        ApplyOpacity();
        Invalidate(erase: false);
    }

    public void SetAllowsTransparency(bool allowsTransparency)
    {
        _allowsTransparency = allowsTransparency;
        if (Handle == 0)
        {
            return;
        }

        EnsureLayeredStyleIfNeeded();
        if (_allowsTransparency)
        {
            ValidateTransparencySupport();
        }
        else
        {
            ApplyOpacity();
        }
        Invalidate(erase: false);
    }

    public void SetTitle(string title)
    {
        if (Handle != 0)
        {
            User32.SetWindowText(Handle, title);
        }
    }

    public void SetIcon(IconSource? icon)
    {
        if (Handle == 0)
        {
            return;
        }

        DestroyIcons();

        if (icon == null)
        {
            ApplyIcons();
            return;
        }

        var dpiScale = Window.DpiScale <= 0 ? 1.0 : Window.DpiScale;
        int smallPx = Math.Max(16, (int)Math.Round(16 * dpiScale));
        int bigPx = Math.Max(32, (int)Math.Round(32 * dpiScale));

        _hIconSmall = TryCreateIcon(icon, smallPx);
        _hIconBig = TryCreateIcon(icon, bigPx);
        if (_hIconBig == 0 && _hIconSmall != 0)
        {
            _hIconBig = _hIconSmall;
        }
        else if (_hIconSmall == 0 && _hIconBig != 0)
        {
            _hIconSmall = _hIconBig;
        }

        ApplyIcons();
    }

    public void SetClientSize(double widthDip, double heightDip)
    {
        if (Handle == 0)
        {
            return;
        }

        uint dpi = Window.Dpi == 0 ? User32.GetDpiForWindow(Handle) : Window.Dpi;
        double dpiScale = dpi / 96.0;

        var rect = new RECT(0, 0, (int)Math.Round(widthDip * dpiScale), (int)Math.Round(heightDip * dpiScale));
        User32.AdjustWindowRectEx(ref rect, GetWindowStyle(), false, 0);
        User32.SetWindowPos(Handle, 0, 0, 0, rect.Width, rect.Height, 0x0002 | 0x0004); // SWP_NOMOVE | SWP_NOZORDER
    }

    public Point GetPosition()
    {
        if (Handle == 0 || !User32.GetWindowRect(Handle, out var r))
        {
            return default;
        }

        uint dpi = Window.Dpi == 0 ? User32.GetDpiForWindow(Handle) : Window.Dpi;
        double dpiScale = dpi / 96.0;
        if (dpiScale <= 0)
        {
            dpiScale = 1.0;
        }

        return new Point(r.left / dpiScale, r.top / dpiScale);
    }

    public void SetPosition(double leftDip, double topDip)
    {
        if (Handle == 0)
        {
            return;
        }

        uint dpi = Window.Dpi == 0 ? User32.GetDpiForWindow(Handle) : Window.Dpi;
        double dpiScale = dpi / 96.0;
        if (dpiScale <= 0)
        {
            dpiScale = 1.0;
        }

        int x = (int)Math.Round(leftDip * dpiScale);
        int y = (int)Math.Round(topDip * dpiScale);

        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_NOACTIVATE = 0x0010;

        User32.SetWindowPos(Handle, 0, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }

    public void CaptureMouse()
    {
        if (Handle == 0)
        {
            return;
        }

        User32.SetCapture(Handle);
    }

    public void ReleaseMouseCapture()
    {
        User32.ReleaseCapture();
    }

    public nint ProcessMessage(uint msg, nint wParam, nint lParam)
    {
        switch (msg)
        {
            case WindowMessages.WM_NCHITTEST:
                if (_allowsTransparency)
                {
                    return HandleNcHitTest(lParam);
                }
                return User32.DefWindowProc(Handle, msg, wParam, lParam);

            case WindowMessages.WM_NCCREATE:
                return User32.DefWindowProc(Handle, msg, wParam, lParam);

            case WindowMessages.WM_CREATE:
                return 0;

            case WindowMessages.WM_DESTROY:
                HandleDestroy();
                return 0;

            case WindowMessages.WM_CLOSE:
                Window.RaiseClosed();
                User32.DestroyWindow(Handle);
                return 0;

            case WindowMessages.WM_CANCELMODE:
            case WindowMessages.WM_CAPTURECHANGED:
                // Capture can be revoked by the OS (e.g. deactivation). Keep UI state consistent.
                Window.ClearMouseCaptureState();
                return User32.DefWindowProc(Handle, msg, wParam, lParam);

            case WindowMessages.WM_PAINT:
                return HandlePaint();

            case WindowMessages.WM_ERASEBKGND:
                return HandleEraseBackground(wParam);

            case WindowMessages.WM_GETMINMAXINFO:
                return HandleGetMinMaxInfo(lParam);

            case WindowMessages.WM_SIZE:
                return HandleSize(lParam);

            case WindowMessages.WM_DPICHANGED:
                return HandleDpiChanged(wParam, lParam);

            case WindowMessages.WM_ACTIVATE:
                return HandleActivate(wParam);

            case WindowMessages.WM_LBUTTONDOWN:
                return HandleMouseButton(lParam, MouseButton.Left, isDown: true, isDoubleClickMessage: false);
            case WindowMessages.WM_LBUTTONDBLCLK:
                return HandleMouseButton(lParam, MouseButton.Left, isDown: true, isDoubleClickMessage: true);
            case WindowMessages.WM_LBUTTONUP:
                return HandleMouseButton(lParam, MouseButton.Left, isDown: false, isDoubleClickMessage: false);
            case WindowMessages.WM_RBUTTONDOWN:
                return HandleMouseButton(lParam, MouseButton.Right, isDown: true, isDoubleClickMessage: false);
            case WindowMessages.WM_RBUTTONDBLCLK:
                return HandleMouseButton(lParam, MouseButton.Right, isDown: true, isDoubleClickMessage: true);
            case WindowMessages.WM_RBUTTONUP:
                return HandleMouseButton(lParam, MouseButton.Right, isDown: false, isDoubleClickMessage: false);
            case WindowMessages.WM_MBUTTONDOWN:
                return HandleMouseButton(lParam, MouseButton.Middle, isDown: true, isDoubleClickMessage: false);
            case WindowMessages.WM_MBUTTONDBLCLK:
                return HandleMouseButton(lParam, MouseButton.Middle, isDown: true, isDoubleClickMessage: true);
            case WindowMessages.WM_MBUTTONUP:
                return HandleMouseButton(lParam, MouseButton.Middle, isDown: false, isDoubleClickMessage: false);

            case WindowMessages.WM_MOUSEMOVE:
                return HandleMouseMove(lParam);
            case WindowMessages.WM_MOUSELEAVE:
                return HandleMouseLeave();

            case WindowMessages.WM_MOUSEWHEEL:
                return HandleMouseWheel(wParam, lParam, isHorizontal: false);
            case WindowMessages.WM_MOUSEHWHEEL:
                return HandleMouseWheel(wParam, lParam, isHorizontal: true);

            case WindowMessages.WM_KEYDOWN:
            case WindowMessages.WM_SYSKEYDOWN:
                return HandleKeyDown(msg, wParam, lParam);
            case WindowMessages.WM_KEYUP:
            case WindowMessages.WM_SYSKEYUP:
                return HandleKeyUp(msg, wParam, lParam);

            case WindowMessages.WM_CHAR:
                return HandleChar(wParam);

            case WindowMessages.WM_IME_STARTCOMPOSITION:
                return HandleImeStartComposition();
            case WindowMessages.WM_IME_COMPOSITION:
                return HandleImeComposition(lParam);
            case WindowMessages.WM_IME_ENDCOMPOSITION:
                return HandleImeEndComposition();

            case WindowMessages.WM_SETFOCUS:
                User32.CreateCaret(Handle, 0, 1, 20);
                return 0;

            case WindowMessages.WM_KILLFOCUS:
                User32.DestroyCaret();
                return 0;

            case Win32Dispatcher.WM_INVOKE:
                (Window.ApplicationDispatcher as Win32Dispatcher)?.ProcessWorkItems();
                return 0;

            case WindowMessages.WM_TIMER:
                if ((Window.ApplicationDispatcher as Win32Dispatcher)?.ProcessTimer((nuint)wParam) == true)
                {
                    return 0;
                }

                return 0;

            default:
                return User32.DefWindowProc(Handle, msg, wParam, lParam);
        }
    }

    private void CreateWindow()
    {
        uint initialDpi = User32.GetDpiForSystem();
        Window.SetDpi(initialDpi);
        double dpiScale = Window.DpiScale;

        var rect = new RECT(0, 0, (int)(Window.Width * dpiScale), (int)(Window.Height * dpiScale));
        uint style = GetWindowStyle();
        User32.AdjustWindowRectEx(ref rect, style, false, 0);

        int x = 100;
        int y = 100;
        if (Window.StartupLocation == WindowStartupLocation.CenterScreen)
        {
            const int SM_CXSCREEN = 0;
            const int SM_CYSCREEN = 1;
            int screenW = User32.GetSystemMetrics(SM_CXSCREEN);
            int screenH = User32.GetSystemMetrics(SM_CYSCREEN);
            if (screenW > 0 && screenH > 0)
            {
                x = Math.Max(0, (screenW - rect.Width) / 2);
                y = Math.Max(0, (screenH - rect.Height) / 2);
            }
        }

        uint exStyle = 0;
        if (Window.AllowsTransparency || Window.Opacity < 0.999)
        {
            exStyle |= WindowStylesEx.WS_EX_LAYERED;
        }

        Handle = User32.CreateWindowEx(
            exStyle,
            Win32PlatformHost.WindowClassName,
            Window.Title,
            style,
            x,
            y,
            rect.Width,
            rect.Height,
            0,
            0,
            Kernel32.GetModuleHandle(null),
            0);

        if (Handle == 0)
        {
            throw new InvalidOperationException($"Failed to create window. Error: {Marshal.GetLastWin32Error()}");
        }

        _titleBarThemeSync.Initialize(Handle);
        _host.RegisterWindow(Handle, this);
        Window.AttachBackend(this);

        ApplyResizeMode();
        EnsureLayeredStyleIfNeeded();
        ValidateTransparencySupport();
        ApplyOpacity();

        uint actualDpi = User32.GetDpiForWindow(Handle);
        if (actualDpi != initialDpi)
        {
            var oldDpi = initialDpi;
            Window.SetDpi(actualDpi);
            Window.RaiseDpiChanged(oldDpi, actualDpi);
            SetClientSize(Window.Width, Window.Height);
            // Force layout recalculation with the correct DPI before first paint
            Window.PerformLayout();
        }

        User32.GetClientRect(Handle, out var clientRect);
        Window.SetClientSizeDip(clientRect.Width / Window.DpiScale, clientRect.Height / Window.DpiScale);
    }

    private nint HandleEraseBackground(nint hdc)
    {
        if (_allowsTransparency)
        {
            _initialEraseDone = true;
            return 1;
        }

        if (_initialEraseDone)
        {
            return 1;
        }

        if (hdc == 0 || Handle == 0)
        {
            return 1;
        }

        if (!User32.GetClientRect(Handle, out var rc))
        {
            return 1;
        }

        var theme = Window.ThemeInternal;
        var bg = Window.Background.A > 0 ? Window.Background : theme.Palette.WindowBackground;

        nint brush = Gdi32.CreateSolidBrush(bg.ToCOLORREF());
        try
        {
            Gdi32.FillRect(hdc, ref rc, brush);
        }
        finally
        {
            if (brush != 0)
            {
                Gdi32.DeleteObject(brush);
            }
        }

        _initialEraseDone = true;
        return 1;
    }

    // (system backdrop support removed)

    private void ApplyIcons()
    {
        // WM_SETICON: wParam=ICON_SMALL(0)/ICON_BIG(1), lParam=HICON
        const int ICON_SMALL = 0;
        const int ICON_BIG = 1;

        User32.SendMessage(Handle, WindowMessages.WM_SETICON, (nint)ICON_SMALL, _hIconSmall);
        User32.SendMessage(Handle, WindowMessages.WM_SETICON, (nint)ICON_BIG, _hIconBig);
    }

    private static nint TryCreateIcon(IconSource icon, int desiredSizePx)
    {
        var src = icon.Pick(desiredSizePx);
        if (src == null)
        {
            return 0;
        }

        if (!ImageDecoders.TryDecode(src.Data, out var bmp))
        {
            return 0;
        }

        return CreateHIconFromDecodedBitmap(bmp);
    }

    private static nint CreateHIconFromDecodedBitmap(DecodedBitmap bmp)
    {
        int w = Math.Max(1, bmp.WidthPx);
        int h = Math.Max(1, bmp.HeightPx);

        var bmi = BITMAPINFO.Create32bpp(w, h);
        nint bits;
        nint hbmColor = Gdi32.CreateDIBSection(0, ref bmi, usage: 0, out bits, 0, 0);
        if (hbmColor == 0 || bits == 0)
        {
            if (hbmColor != 0)
            {
                Gdi32.DeleteObject(hbmColor);
            }

            return 0;
        }

        Marshal.Copy(bmp.Data, 0, bits, bmp.Data.Length);

        // For 32-bpp icons, Windows primarily uses the alpha channel; provide a 1-bpp mask for compatibility.
        nint hbmMask = Gdi32.CreateBitmap(w, h, nPlanes: 1, nBitCount: 1, lpBits: 0);
        if (hbmMask == 0)
        {
            Gdi32.DeleteObject(hbmColor);
            return 0;
        }

        var info = new ICONINFO
        {
            fIcon = true,
            xHotspot = 0,
            yHotspot = 0,
            hbmMask = hbmMask,
            hbmColor = hbmColor,
        };

        nint hIcon = User32.CreateIconIndirect(ref info);
        Gdi32.DeleteObject(hbmColor);
        Gdi32.DeleteObject(hbmMask);
        return hIcon;
    }

    private void DestroyIcons()
    {
        var oldSmall = _hIconSmall;
        var oldBig = _hIconBig;
        _hIconSmall = 0;
        _hIconBig = 0;

        if (oldSmall != 0)
        {
            User32.DestroyIcon(oldSmall);
        }

        if (oldBig != 0 && oldBig != oldSmall)
        {
            User32.DestroyIcon(oldBig);
        }
    }

    private uint GetWindowStyle()
    {
        // Per-pixel transparency (UpdateLayeredWindow path) cannot use the default system non-client
        // chrome. Use a borderless popup-style window so the client area matches the window bounds;
        // otherwise UpdateLayeredWindow positioning and input alignment break because
        // client-origin != window-origin.
        if (Window.AllowsTransparency)
        {
            // IMPORTANT:
            // - Do NOT add WS_THICKFRAME here. Even with WS_POPUP, a sizing border makes the client
            //   origin differ from the window origin. Our layered-present path renders the client
            //   into a bitmap of client size, so any border would reintroduce hit-test/render offsets.
            // - If we later want native edge resizing, implement it via a custom hit test / sizing
            //   strategy instead of relying on the system non-client frame.
            return WindowStyles.WS_POPUP | WindowStyles.WS_SYSMENU;
        }

        {
            uint style = WindowStyles.WS_OVERLAPPEDWINDOW;
            if (!Window.WindowSize.IsResizable)
            {
                style &= ~(WindowStyles.WS_THICKFRAME | WindowStyles.WS_MAXIMIZEBOX);
            }

            return style;
        }
    }

    private void ApplyResizeMode()
    {
        const int GWL_STYLE = -16;
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_FRAMECHANGED = 0x0020;

        uint style = GetWindowStyle();
        User32.SetWindowLongPtr(Handle, GWL_STYLE, (nint)style);
        User32.SetWindowPos(Handle, 0, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    private void HandleDestroy()
    {
        _titleBarThemeSync.Dispose();
        DestroyIcons();
        User32.ReleaseCapture();
        Window.ClearMouseOverState();
        Window.ClearMouseCaptureState();
        Window.ReleaseWindowGraphicsResources(Handle);

        _host.UnregisterWindow(Handle);
        Window.DisposeVisualTree();
        Handle = 0;
    }

    private nint HandlePaint()
    {
        var ps = new PAINTSTRUCT();
        nint hdc = User32.BeginPaint(Handle, out ps);

        try
        {
            NeedsRender = false;
            if (_allowsTransparency)
            {
                // Layered windows are updated via UpdateLayeredWindow.
                RenderNowCore();
            }
            else
            {
                Window.RenderFrame(CreateWin32HdcSurface(hdc));
            }
        }
        finally
        {
            User32.EndPaint(Handle, ref ps);
        }

        return 0;
    }

    internal void RenderIfNeeded()
    {
        if (!NeedsRender || Handle == 0)
        {
            return;
        }

        NeedsRender = false;
        RenderNowCore();
    }

    internal void RenderNow()
    {
        if (Handle == 0)
        {
            return;
        }

        NeedsRender = false;
        RenderNowCore();
    }

    private void RenderNowCore()
    {
        if (_allowsTransparency)
        {
            RenderNowLayered();
            _ = User32.ValidateRect(Handle, 0);
            return;
        }

        // Render without relying on WM_PAINT. This matches the request-driven model (WPF-style coalescing).
        // If the window is in an invalid paint state (e.g., uncovered), ValidateRect prevents redundant WM_PAINT storms.

        nint hdc = User32.GetDC(Handle);
        if (hdc == 0)
        {
            return;
        }

        try
        {
            Window.RenderFrame(CreateWin32HdcSurface(hdc));
            _ = User32.ValidateRect(Handle, 0);
        }
        finally
        {
            _ = User32.ReleaseDC(Handle, hdc);
        }
    }

    private void RenderNowLayered()
    {
        if (Handle == 0)
        {
            return;
        }

        var clientSize = Window.ClientSize;
        int pixelWidth = (int)Math.Ceiling(clientSize.Width * Window.DpiScale);
        int pixelHeight = (int)Math.Ceiling(clientSize.Height * Window.DpiScale);
        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);

        if (Window.GraphicsFactory is not IWindowSurfacePresenter presenter)
        {
            return;
        }

        var surface = new Win32LayeredSurface(Handle, pixelWidth, pixelHeight, Window.DpiScale);
        _ = presenter.Present(Window, surface, _opacity);
    }

    private Win32HdcSurface CreateWin32HdcSurface(nint hdc)
    {
        var clientSize = Window.ClientSize;
        int pixelWidth = (int)Math.Ceiling(clientSize.Width * Window.DpiScale);
        int pixelHeight = (int)Math.Ceiling(clientSize.Height * Window.DpiScale);
        pixelWidth = Math.Max(1, pixelWidth);
        pixelHeight = Math.Max(1, pixelHeight);
        return new Win32HdcSurface(Handle, hdc, pixelWidth, pixelHeight, Window.DpiScale);
    }

    private sealed class Win32HdcSurface : IWin32HdcWindowSurface
    {
        public WindowSurfaceKind Kind => WindowSurfaceKind.Default;

        public nint Handle => Hwnd;

        public nint Hwnd { get; }

        public nint Hdc { get; }

        public int PixelWidth { get; }

        public int PixelHeight { get; }

        public double DpiScale { get; }

        public Win32HdcSurface(nint hwnd, nint hdc, int pixelWidth, int pixelHeight, double dpiScale)
        {
            Hwnd = hwnd;
            Hdc = hdc;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            DpiScale = dpiScale <= 0 ? 1.0 : dpiScale;
        }
    }

    private void EnsureLayeredStyleIfNeeded()
    {
        if (Handle == 0)
        {
            return;
        }

        const int GWL_EXSTYLE = -20;
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_FRAMECHANGED = 0x0020;

        bool needsLayered = _allowsTransparency || _opacity < 0.999;

        uint exStyle = (uint)User32.GetWindowLongPtr(Handle, GWL_EXSTYLE).ToInt64();
        bool hasLayered = (exStyle & WindowStylesEx.WS_EX_LAYERED) != 0;

        if (needsLayered == hasLayered)
        {
            return;
        }

        if (needsLayered)
        {
            exStyle |= WindowStylesEx.WS_EX_LAYERED;
        }
        else
        {
            exStyle &= ~WindowStylesEx.WS_EX_LAYERED;
        }

        User32.SetWindowLongPtr(Handle, GWL_EXSTYLE, (nint)exStyle);
        User32.SetWindowPos(Handle, 0, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOZORDER | SWP_FRAMECHANGED);
    }

    private void ApplyOpacity()
    {
        if (Handle == 0 || _allowsTransparency)
        {
            return;
        }

        // Global opacity for non-layered rendering.
        const uint LWA_ALPHA = 0x00000002;
        byte alpha = (byte)Math.Round(_opacity * 255.0);
        _ = User32.SetLayeredWindowAttributes(Handle, 0, alpha, LWA_ALPHA);
    }

    private void ValidateTransparencySupport()
    {
        if (!_allowsTransparency)
        {
            return;
        }

        if (Window.GraphicsFactory is not IWindowSurfacePresenter)
        {
            throw new PlatformNotSupportedException("Per-pixel transparency on Win32 requires a graphics backend that supports window surfaces.");
        }
    }

    private sealed class Win32LayeredSurface : IWin32LayeredWindowSurface
    {
        public WindowSurfaceKind Kind => WindowSurfaceKind.Layered;
        public nint Handle => Hwnd;
        public nint Hwnd { get; }
        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public double DpiScale { get; }

        public Win32LayeredSurface(nint hwnd, int pixelWidth, int pixelHeight, double dpiScale)
        {
            Hwnd = hwnd;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            DpiScale = dpiScale;
        }
    }

    private unsafe nint HandleGetMinMaxInfo(nint lParam)
    {
        var info = (MINMAXINFO*)lParam;

        uint dpi = Window.Dpi == 0 ? User32.GetDpiForWindow(Handle) : Window.Dpi;
        double dpiScale = dpi / 96.0;
        if (dpiScale <= 0) dpiScale = 1.0;

        uint style = GetWindowStyle();

        var ws = Window.WindowSize;
        double minW = ws.MinWidth;
        double minH = ws.MinHeight;
        double maxW = ws.MaxWidth;
        double maxH = ws.MaxHeight;

        if (minW > 0 || minH > 0)
        {
            var minRect = new RECT(0, 0,
                minW > 0 ? (int)Math.Ceiling(minW * dpiScale) : 0,
                minH > 0 ? (int)Math.Ceiling(minH * dpiScale) : 0);
            User32.AdjustWindowRectEx(ref minRect, style, false, 0);

            if (minW > 0) info->ptMinTrackSize.x = minRect.Width;
            if (minH > 0) info->ptMinTrackSize.y = minRect.Height;
        }

        if (!double.IsPositiveInfinity(maxW) || !double.IsPositiveInfinity(maxH))
        {
            var maxRect = new RECT(0, 0,
                !double.IsPositiveInfinity(maxW) ? (int)Math.Ceiling(maxW * dpiScale) : 0,
                !double.IsPositiveInfinity(maxH) ? (int)Math.Ceiling(maxH * dpiScale) : 0);
            User32.AdjustWindowRectEx(ref maxRect, style, false, 0);

            if (!double.IsPositiveInfinity(maxW)) info->ptMaxTrackSize.x = maxRect.Width;
            if (!double.IsPositiveInfinity(maxH)) info->ptMaxTrackSize.y = maxRect.Height;
        }

        return 0;
    }

    private nint HandleSize(nint lParam)
    {
        int widthPx = (short)(lParam.ToInt64() & 0xFFFF);
        int heightPx = (short)((lParam.ToInt64() >> 16) & 0xFFFF);

        Window.SetClientSizeDip(widthPx / Window.DpiScale, heightPx / Window.DpiScale);
        Window.PerformLayout();
        Window.Invalidate();

        Window.RaiseClientSizeChanged(widthPx / Window.DpiScale, heightPx / Window.DpiScale);
        return 0;
    }

    private nint HandleDpiChanged(nint wParam, nint lParam)
    {
        uint newDpi = (uint)(wParam.ToInt64() & 0xFFFF);
        uint oldDpi = Window.Dpi;
        Window.SetDpi(newDpi);

        var suggestedRect = Marshal.PtrToStructure<RECT>(lParam);
        User32.SetWindowPos(Handle, 0,
            suggestedRect.left, suggestedRect.top,
            suggestedRect.Width, suggestedRect.Height,
            0x0004 | 0x0010); // SWP_NOZORDER | SWP_NOACTIVATE

        // WM_SIZE is usually sent after SetWindowPos, but we need a consistent client size for the
        // layout pass we run below (otherwise a stale _clientSizeDip can cause a 1-frame "broken" layout).
        User32.GetClientRect(Handle, out var clientRect);
        Window.SetClientSizeDip(clientRect.Width / Window.DpiScale, clientRect.Height / Window.DpiScale);

        Window.RaiseDpiChanged(oldDpi, newDpi);
        Window.PerformLayout();
        Window.Invalidate();

        return 0;
    }

    private nint HandleActivate(nint wParam)
    {
        bool active = (wParam.ToInt64() & 0xFFFF) != 0;
        Window.SetIsActive(active);
        if (active)
        {
            Window.RaiseActivated();
        }
        else
        {
            Window.RaiseDeactivated();
        }

        return 0;
    }

    private nint HandleMouseMove(nint lParam)
    {
        var pos = GetMousePosition(lParam);
        var screenPos = ClientToScreen(pos);

        bool leftDown = (User32.GetKeyState(VirtualKeys.VK_LBUTTON) & 0x8000) != 0;
        bool rightDown = (User32.GetKeyState(VirtualKeys.VK_RBUTTON) & 0x8000) != 0;
        bool middleDown = (User32.GetKeyState(VirtualKeys.VK_MBUTTON) & 0x8000) != 0;
        WindowInputRouter.MouseMove(Window, pos, screenPos, leftDown, rightDown, middleDown);

        return 0;
    }

    private nint HandleMouseButton(nint lParam, MouseButton button, bool isDown, bool isDoubleClickMessage)
    {
        int xPx = (short)(lParam.ToInt64() & 0xFFFF);
        int yPx = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        var pos = new Point(xPx / Window.DpiScale, yPx / Window.DpiScale);
        var screenPos = ClientToScreen(pos);
        bool leftDown = (User32.GetKeyState(VirtualKeys.VK_LBUTTON) & 0x8000) != 0;
        bool rightDown = (User32.GetKeyState(VirtualKeys.VK_RBUTTON) & 0x8000) != 0;
        bool middleDown = (User32.GetKeyState(VirtualKeys.VK_MBUTTON) & 0x8000) != 0;

        int clickCount = 1;
        int buttonIndex = (int)button;
        if ((uint)buttonIndex < (uint)_lastPressClickCounts.Length)
        {
            if (isDown)
            {
                const int SM_CXDOUBLECLK = 36;
                const int SM_CYDOUBLECLK = 37;
                uint timeMs = unchecked((uint)User32.GetMessageTime());
                uint maxDelayMs = User32.GetDoubleClickTime();
                int maxDistX = User32.GetSystemMetrics(SM_CXDOUBLECLK);
                int maxDistY = User32.GetSystemMetrics(SM_CYDOUBLECLK);

                clickCount = _clickCountTracker.Update(button, xPx, yPx, timeMs, maxDelayMs, maxDistX, maxDistY);
                if (isDoubleClickMessage && clickCount < 2)
                {
                    clickCount = 2;
                }

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
            pos,
            screenPos,
            button,
            isDown,
            leftDown,
            rightDown,
            middleDown,
            clickCount);

        return 0;
    }

    private nint HandleMouseWheel(nint wParam, nint lParam, bool isHorizontal)
    {
        int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);

        var screenX = (short)(lParam.ToInt64() & 0xFFFF);
        var screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        var pt = new POINT(screenX, screenY);
        User32.ScreenToClient(Handle, ref pt);
        var pos = new Point(pt.x / Window.DpiScale, pt.y / Window.DpiScale);
        WindowInputRouter.MouseWheel(Window, pos, new Point(screenX, screenY), delta, isHorizontal);

        return 0;
    }

    private nint HandleMouseLeave()
    {
        WindowInputRouter.UpdateMouseOver(Window, null);
        return 0;
    }

    private nint HandleNcHitTest(nint lParam)
    {
        const int HTNOWHERE = 0;
        const int HTCLIENT = 1;

        if (Handle == 0)
        {
            return HTNOWHERE;
        }

        int screenX = (short)(lParam.ToInt64() & 0xFFFF);
        int screenY = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        var pt = new POINT(screenX, screenY);
        User32.ScreenToClient(Handle, ref pt);

        if (!User32.GetClientRect(Handle, out var clientRect))
        {
            return HTCLIENT;
        }

        if (pt.x >= 0 && pt.y >= 0 && pt.x < clientRect.Width && pt.y < clientRect.Height)
        {
            return HTCLIENT;
        }

        return HTNOWHERE;
    }

    private ModifierKeys GetModifierKeys()
    {
        var modifiers = ModifierKeys.None;

        if ((User32.GetKeyState(VirtualKeys.VK_CONTROL) & 0x8000) != 0)
        {
            modifiers |= ModifierKeys.Control;
        }

        if ((User32.GetKeyState(VirtualKeys.VK_SHIFT) & 0x8000) != 0)
        {
            modifiers |= ModifierKeys.Shift;
        }

        if ((User32.GetKeyState(VirtualKeys.VK_MENU) & 0x8000) != 0)
        {
            modifiers |= ModifierKeys.Alt;
        }

        return modifiers;
    }

    private nint HandleKeyDown(uint msg, nint wParam, nint lParam)
    {
        _textInputSuppression.ResetPerKeyDown();

        int platformKey = (int)wParam.ToInt64();
        bool isRepeat = ((lParam.ToInt64() >> 30) & 1) != 0;
        var modifiers = GetModifierKeys();

        // Let the OS handle Alt+F4 so it translates to WM_SYSCOMMAND/SC_CLOSE and our WM_CLOSE path runs.
        if (msg == WindowMessages.WM_SYSKEYDOWN &&
            modifiers.HasFlag(ModifierKeys.Alt) &&
            platformKey == VirtualKeys.VK_F4)
        {
            return User32.DefWindowProc(Handle, msg, wParam, lParam);
        }

        var args = new KeyEventArgs(MapKey(platformKey), platformKey, modifiers, isRepeat);
        Window.RaisePreviewKeyDown(args);
        if (args.Handled)
        {
            return 0;
        }

        WindowInputRouter.KeyDown(Window, args);

        // WPF-like Tab behavior:
        // - Always let the focused element see KeyDown first.
        // - Only perform focus navigation if the key is still unhandled.
        if (!args.Handled && args.Key == Key.Tab)
        {
            if (modifiers.HasFlag(ModifierKeys.Shift))
            {
                Window.FocusManager.MoveFocusPrevious();
            }
            else
            {
                Window.FocusManager.MoveFocusNext();
            }

            // Prevent a subsequent WM_CHAR '\t' from inserting a tab into the newly focused element.
            _textInputSuppression.SuppressNextFromHandledKeyDown(Key.Tab);
            return 0;
        }

        if (args.Handled)
        {
            // KeyDown-handled Enter/Tab should not also emit WM_CHAR text input.
            _textInputSuppression.SuppressNextFromHandledKeyDown(args.Key);
        }

        return args.Handled ? 0 : User32.DefWindowProc(Handle, msg, wParam, lParam);
    }

    private nint HandleKeyUp(uint msg, nint wParam, nint lParam)
    {
        int platformKey = (int)wParam.ToInt64();
        var modifiers = GetModifierKeys();

        var args = new KeyEventArgs(MapKey(platformKey), platformKey, modifiers);
        Window.RaisePreviewKeyUp(args);
        if (!args.Handled)
        {
            WindowInputRouter.KeyUp(Window, args);
        }

        Window.RequerySuggested();

        return args.Handled ? 0 : User32.DefWindowProc(Handle, msg, wParam, lParam);
    }

    private nint HandleChar(nint wParam)
    {
        char c = (char)wParam.ToInt64();

        if (_textInputSuppression.TryConsumeChar(c))
        {
            return 0;
        }

        if (c == '\b')
        {
            return 0;
        }

        if (char.IsControl(c) && c != '\r' && c != '\t')
        {
            return 0;
        }

        var args = new TextInputEventArgs(c.ToString());
        Window.RaisePreviewTextInput(args);
        if (!args.Handled)
        {
            if (Window.FocusManager.FocusedElement is ITextInputClient client)
            {
                client.HandleTextInput(args);
            }
        }

        return 0;
    }

    private nint HandleImeStartComposition()
    {
        var args = new TextCompositionEventArgs();
        Window.RaisePreviewTextCompositionStart(args);
        if (!args.Handled)
        {
            if (Window.FocusManager.FocusedElement is ITextCompositionClient client)
            {
                client.HandleTextCompositionStart(args);
            }
        }

        return 0;
    }

    private nint HandleImeComposition(nint lParam)
    {
        int flags = (int)lParam.ToInt64();

        if ((flags & Imm32.CompositionStringFlags.GCS_COMPSTR) != 0)
        {
            string comp = GetImeCompositionString(Imm32.CompositionStringFlags.GCS_COMPSTR);
            var args = new TextCompositionEventArgs(comp);
            Window.RaisePreviewTextCompositionUpdate(args);
            if (!args.Handled)
            {
                if (Window.FocusManager.FocusedElement is ITextCompositionClient client)
                {
                    client.HandleTextCompositionUpdate(args);
                }
            }
        }

        // Forward committed IME text explicitly.
        // Relying on WM_CHAR for IME commits is fragile because DefWindowProc is responsible for generating
        // those messages, and this backend intentionally intercepts WM_IME_COMPOSITION.
        if ((flags & Imm32.CompositionStringFlags.GCS_RESULTSTR) != 0)
        {
            string result = GetImeCompositionString(Imm32.CompositionStringFlags.GCS_RESULTSTR);
            if (!string.IsNullOrEmpty(result))
            {
                var ti = new TextInputEventArgs(result);
                Window.RaisePreviewTextInput(ti);
                if (!ti.Handled)
                {
                    if (Window.FocusManager.FocusedElement is ITextInputClient client)
                    {
                        client.HandleTextInput(ti);
                    }
                }
            }
        }

        return 0;
    }

    private nint HandleImeEndComposition()
    {
        var args = new TextCompositionEventArgs();
        Window.RaisePreviewTextCompositionEnd(args);
        if (!args.Handled)
        {
            if (Window.FocusManager.FocusedElement is ITextCompositionClient client)
            {
                client.HandleTextCompositionEnd(args);
            }
        }

        return 0;
    }

    private string GetImeCompositionString(int dwIndex)
    {
        nint himc = Imm32.ImmGetContext(Handle);
        if (himc == 0)
        {
            return string.Empty;
        }

        try
        {
            int byteCount = Imm32.ImmGetCompositionStringW(himc, dwIndex, 0, 0);
            if (byteCount <= 0)
            {
                return string.Empty;
            }

            // IMM returns UTF-16LE bytes.
            byteCount = Math.Min(byteCount, 1024 * 1024);
            byte[] rented = ArrayPool<byte>.Shared.Rent(byteCount);
            try
            {
                unsafe
                {
                    fixed (byte* p = rented)
                    {
                        _ = Imm32.ImmGetCompositionStringW(himc, dwIndex, (nint)p, byteCount);
                    }
                }

                return Encoding.Unicode.GetString(rented, 0, byteCount);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
        finally
        {
            Imm32.ImmReleaseContext(Handle, himc);
        }
    }

    private static Key MapKey(int vk)
    {
        // Function keys
        if (vk is >= VirtualKeys.VK_F1 and <= VirtualKeys.VK_F24)
        {
            return (Key)((int)Key.F1 + (vk - VirtualKeys.VK_F1));
        }

        // Digits (top row)
        if (vk is >= 0x30 and <= 0x39)
        {
            return (Key)((int)Key.D0 + (vk - 0x30));
        }

        // Letters
        if (vk is >= 0x41 and <= 0x5A)
        {
            return (Key)((int)Key.A + (vk - 0x41));
        }

        // Numpad digits
        if (vk is >= 0x60 and <= 0x69)
        {
            return (Key)((int)Key.NumPad0 + (vk - 0x60));
        }

        return vk switch
        {
            VirtualKeys.VK_BACK => Key.Backspace,
            VirtualKeys.VK_TAB => Key.Tab,
            VirtualKeys.VK_RETURN => Key.Enter,
            VirtualKeys.VK_ESCAPE => Key.Escape,
            VirtualKeys.VK_SPACE => Key.Space,

            VirtualKeys.VK_LEFT => Key.Left,
            VirtualKeys.VK_UP => Key.Up,
            VirtualKeys.VK_RIGHT => Key.Right,
            VirtualKeys.VK_DOWN => Key.Down,

            VirtualKeys.VK_INSERT => Key.Insert,
            VirtualKeys.VK_DELETE => Key.Delete,
            VirtualKeys.VK_HOME => Key.Home,
            VirtualKeys.VK_END => Key.End,
            VirtualKeys.VK_PRIOR => Key.PageUp,
            VirtualKeys.VK_NEXT => Key.PageDown,

            VirtualKeys.VK_ADD => Key.Add,
            VirtualKeys.VK_SUBTRACT => Key.Subtract,
            VirtualKeys.VK_MULTIPLY => Key.Multiply,
            VirtualKeys.VK_DIVIDE => Key.Divide,
            VirtualKeys.VK_DECIMAL => Key.Decimal,

            _ => Key.None
        };
    }

    private Point GetMousePosition(nint lParam)
    {
        int x = (short)(lParam.ToInt64() & 0xFFFF);
        int y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
        // lParam is in device pixels; convert to DIPs.
        return new Point(x / Window.DpiScale, y / Window.DpiScale);
    }

    private Point ClientToScreenInternal(Point clientPoint)
    {
        var pt = new POINT((int)(clientPoint.X * Window.DpiScale), (int)(clientPoint.Y * Window.DpiScale));
        User32.ClientToScreen(Handle, ref pt);
        return new Point(pt.x, pt.y);
    }

    public Point ClientToScreen(Point clientPointDip)
        => ClientToScreenInternal(clientPointDip);

    public Point ScreenToClient(Point screenPointPx)
    {
        var pt = new POINT((int)screenPointPx.X, (int)screenPointPx.Y);
        User32.ScreenToClient(Handle, ref pt);
        return new Point(pt.x / Window.DpiScale, pt.y / Window.DpiScale);
    }

    public void Dispose()
    {
        _titleBarThemeSync.Dispose();
        DestroyIcons();
        if (Handle != 0)
        {
            Close();
        }
    }

    public void SetEnabled(bool enabled)
    {
        if (Handle == 0)
        {
            return;
        }

        _ = User32.EnableWindow(Handle, enabled);

        if (!enabled)
        {
            // Keep managed UI state consistent while modal.
            Window.ClearMouseOverState();
            Window.ClearMouseCaptureState();
        }
    }

    public void Activate()
    {
        if (Handle == 0)
        {
            return;
        }

        _ = User32.SetForegroundWindow(Handle);
        _ = User32.SetFocus(Handle);
    }

    public void SetOwner(nint ownerHandle)
    {
        if (Handle == 0)
        {
            return;
        }

        const int GWL_HWNDPARENT = -8;
        User32.SetWindowLongPtr(Handle, GWL_HWNDPARENT, ownerHandle);
    }

    public void EnsureTheme(bool isDark)
    {
        _titleBarThemeSync.ApplyTheme(isDark);
    }
}
