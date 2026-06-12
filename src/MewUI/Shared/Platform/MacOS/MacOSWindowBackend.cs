using Aprillz.MewUI.Diagnostics;
using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Platform.MacOS;

internal sealed class MacOSWindowBackend : IWindowBackend
{
    // NOTE: Cocoa defines NSNotFound as NSIntegerMax (not NSUIntegerMax).
    private const ulong NSNotFound = (ulong)long.MaxValue;

    internal static readonly EnvDebugLogger ImeLogger = new("MEWUI_IME_DEBUG", "[MacOS][IME]");
    internal static readonly EnvDebugLogger ImeNativeLogger = new("MEWUI_IME_DEBUG_NATIVE", "[MacOS][IME]");

    private static string Truncate(string? text, int maxLen = 120)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= maxLen ? text : text.Substring(0, maxLen) + "…";
    }

    internal static string TruncateForImeLog(string? text, int maxLen = 120) => Truncate(text, maxLen);

    private readonly MacOSPlatformHost _host;
    private readonly Window _window;
    private nint _nsWindow;
    private nint _nsView;
    private nint _metalLayer;
    private bool _shown;
    private int _needsRender;
    private double _lastDpiScale = 1.0;
    private double _opacity = 1.0;
    private bool _allowsTransparency;
    private bool _allowDrop;
    private bool _leftDown;
    private bool _rightDown;
    private bool _middleDown;

    private enum ImeState
    {
        Disabled = 0,
        Ground = 1,
        Preedit = 2,
        Committed = 3,
    }

    // winit-style IME state tracking:
    // - keyDown first goes through interpretKeyEvents (NSTextInputClient).
    // - if that produced IME activity (preedit/commit), we suppress the app KeyDown for that key.
    // - if AppKit calls doCommandBySelector:, it means the key wasn't handled as "text" and should
    //   be forwarded to the app.
    private ImeState _imeState = ImeState.Ground;

    private bool _forwardKeyToAppThisKeyDown;
    private readonly HashSet<int> _forceKeyUps = new();
    private bool _enabled = true;
    private bool _closedRaised;
    private readonly int[] _lastPressClickCounts = new int[5];
    private int _reshapeRendering;
    private long _defaultWindowLevel;
    private ulong _defaultStyleMask;
    private bool? _lastMetalDisplaySyncEnabled;
    private bool? _lastMetalPresentsWithTransaction;

    private bool _imeHasMarkedText;
    private string _imeMarkedText = string.Empty;
    private DragEventArgs? _lastDragEventArgs;

    internal string ImeMarkedText => _imeMarkedText;

    internal Window Window => _window;

    private bool _isHandlingKeyDown;
    private string? _pendingKeyDownTextInput;

    private void UpdateMetalLayerDisplaySyncIfNeeded()
    {
        if (_metalLayer == 0 || !Application.IsRunning)
        {
            return;
        }

        bool enabled = Application.Current.RenderLoopSettings.VSyncEnabled;
        if (_lastMetalDisplaySyncEnabled.HasValue && _lastMetalDisplaySyncEnabled.Value == enabled)
        {
            return;
        }

        MacOSWindowInterop.SetMetalLayerDisplaySyncEnabled(_metalLayer, enabled);
        _lastMetalDisplaySyncEnabled = enabled;
    }

    private void UpdateMetalLayerPresentsWithTransactionIfNeeded()
    {
        if (_metalLayer == 0 || _nsView == 0)
        {
            return;
        }

        // presentsWithTransaction = true is required during live-resize so that the Metal
        // frame is presented inside AppKit's resize CA transaction, preventing the
        // "scaled cached frame" artifact. Outside of live-resize it must be false:
        // Apple's documentation states that presentsWithTransaction overrides
        // displaySyncEnabled — leaving it always-on defeats VSync pacing and causes the
        // render loop to run at 2x the display refresh rate instead of 1x.
        bool needed = MacOSWindowInterop.IsViewInLiveResize(_nsView);
        if (_lastMetalPresentsWithTransaction.HasValue && _lastMetalPresentsWithTransaction.Value == needed)
        {
            return;
        }

        MacOSWindowInterop.SetMetalLayerPresentsWithTransaction(_metalLayer, needed);
        _lastMetalPresentsWithTransaction = needed;
    }

    public MacOSWindowBackend(MacOSPlatformHost host, Window window)
    {
        _host = host;
        _window = window;
    }

    public nint Handle => _nsView;

    internal bool ImeHasMarkedText => _imeHasMarkedText;

    public void SetResizable(bool resizable)
    {
        if (_nsWindow == 0)
        {
            return;
        }

        const ulong NSWindowStyleMaskResizable = 8ul;
        ulong mask = MacOSWindowInterop.GetWindowStyleMask(_nsWindow);

        if (resizable)
        {
            mask |= NSWindowStyleMaskResizable;
        }
        else
        {
            mask &= ~NSWindowStyleMaskResizable;
        }

        MacOSWindowInterop.SetWindowStyleMask(_nsWindow, mask);

        MacOSWindowInterop.ApplyContentSizeConstraints(_nsWindow, _window);

        // Clamp the current window size if it violates the new constraints.
        var ws = _window.WindowSize;
        double curW = _window.Width;
        double curH = _window.Height;
        double clampedW = Math.Clamp(curW, ws.MinWidth, ws.MaxWidth);
        double clampedH = Math.Clamp(curH, ws.MinHeight, ws.MaxHeight);
        if (clampedW != curW || clampedH != curH)
        {
            MacOSWindowInterop.SetClientSize(_nsWindow, clampedW, clampedH);
        }
    }

    public void Show()
    {
        EnsureCreated();
        if (_nsWindow == 0)
        {
            throw new InvalidOperationException("NSWindow creation failed.");
        }

        if (_shown)
        {
            UpdateDpiIfNeeded();
            UpdateClientSizeIfNeeded(forceLayout: true);
            return;
        }

        UpdateDpiIfNeeded();

        _window.PerformLayout();

        _shown = true;

        if (_window.IsAlertWindow)
        {
            MacOSWindowInterop.SetAlertPanelAnimation(_nsWindow);
        }

        if (_window.IsOverlayWindow)
        {
            // Input-transparent overlay (drag preview): click-through (ignoresMouseEvents) + float above all +
            // order front WITHOUT making it key/main, so it never steals capture/focus from the drag source.
            MacOSWindowInterop.SetWindowEnabled(_nsWindow, false); // ignoresMouseEvents = true
            MacOSWindowInterop.SetWindowLevel(_nsWindow, 25);      // NSStatusWindowLevel: above normal/floating
            MacOSWindowInterop.OrderFrontWindow(_nsWindow);
        }
        else
        {
            MacOSWindowInterop.ShowWindow(_nsWindow);
        }

        if (_window.IsDialogWindow)
        {
            MacOSWindowInterop.HideDialogChromeButtons(_nsWindow);
        }

        if (_window.IsAlertWindow)
        {
            MacOSWindowInterop.HideCloseButton(_nsWindow);
        }

        if (_allowsTransparency && _window.IsOverlayWindow)
        {
            // Borderless mask = square corners (titled windows get rounded corners that would clip the overlay's
            // own content, e.g. a rounded chip). Transparency comes from the non-opaque layer, not the mask.
            MacOSWindowInterop.SetWindowStyleMask(_nsWindow, 0); // NSWindowStyleMaskBorderless
        }
        else if (_allowsTransparency)
        {
            MacOSWindowInterop.SetWindowStyleMask(_nsWindow, MacOSWindowInterop.TransparentStyleMask);
            MacOSWindowInterop.SetTitlebarForTransparency(_nsWindow, true);
            MacOSWindowInterop.HideDialogChromeButtons(_nsWindow);
            MacOSWindowInterop.HideCloseButton(_nsWindow);
        }
        else if (_extendTitleBarHeight > 0)
        {
            const ulong ExtendedStyleMask = 1ul | 2ul | 4ul | 8ul | (1ul << 15);
            MacOSWindowInterop.SetWindowStyleMask(_nsWindow, ExtendedStyleMask);
            MacOSWindowInterop.SetTitlebarForTransparency(_nsWindow, true);
            // setStyleMask: can alter the frame — restore the intended client size.
            ApplyRequestedClientSize();
        }

        ApplyResolvedStartupPlacement();
        UpdateClientSizeIfNeeded(forceLayout: true);
        ApplyResolvedStartupPlacement();
        _host.RegisterWindow(_nsWindow, this);

        if (_window.WindowState != Controls.WindowState.Normal)
        {
            SetWindowState(_window.WindowState);
        }
        Interlocked.Exchange(ref _needsRender, 1);
        _host.RequestRender();
    }

    public void Hide()
    {
        if (_nsWindow != 0)
        {
            ObjC.MsgSend_void_nint_nint(_nsWindow, ObjC.Sel("orderOut:"), 0);
        }
    }

    public void Close()
    {
        if (_nsWindow != 0)
        {
            if (_window.IsOverlayWindow)
            {
                // The overlay uses a borderless style mask (square corners), which has no
                // Closable bit, so performClose: would be ignored. Close it directly. windowWillClose still
                // fires RaiseClosedOnce.
                MacOSWindowInterop.CloseWindowImmediate(_nsWindow);
            }
            else
            {
                MacOSWindowInterop.CloseWindow(_nsWindow);
                // windowShouldClose → RequestClose, windowWillClose → RaiseClosedOnce
            }
        }
    }

    public void SetTopmost(bool value)
    {
        if (_nsWindow == 0)
        {
            return;
        }
        // NSFloatingWindowLevel = 3, NSNormalWindowLevel = 0
        ObjC.MsgSend_void_nint_int(_nsWindow, ObjC.Sel("setLevel:"), value ? 3 : 0);
    }

    public void SetCanMinimize(bool value)
    {
        if (_nsWindow == 0)
        {
            return;
        }

        // Toggle NSWindowStyleMaskMiniaturizable (1 << 2 = 4) in styleMask
        ulong mask = ObjC.MsgSend_ulong(_nsWindow, ObjC.Sel("styleMask"));
        const ulong NSWindowStyleMaskMiniaturizable = 4;
        mask = value ? (mask | NSWindowStyleMaskMiniaturizable) : (mask & ~NSWindowStyleMaskMiniaturizable);
        ObjC.MsgSend_void_nint_ulong(_nsWindow, ObjC.Sel("setStyleMask:"), mask);

        // Also enable/disable the miniaturize button
        var btn = ObjC.MsgSend_nint_ulong(_nsWindow, ObjC.Sel("standardWindowButton:"), 1);
        if (btn != 0)
        {
            ObjC.MsgSend_void_nint_bool(btn, ObjC.Sel("setEnabled:"), value);
        }
    }

    public void SetCanMaximize(bool value)
    {
        if (_nsWindow == 0)
        {
            return;
        }

        // Enable/disable the zoom button (standardWindowButton: 2)
        var btn = ObjC.MsgSend_nint_ulong(_nsWindow, ObjC.Sel("standardWindowButton:"), 2);
        if (btn != 0)
        {
            ObjC.MsgSend_void_nint_bool(btn, ObjC.Sel("setEnabled:"), value);
        }
    }

    public void SetShowInTaskbar(bool value)
    {
        if (_nsWindow == 0)
        {
            return;
        }

        // macOS doesn't have a direct ShowInTaskbar concept.
        // The closest approximation is toggling NSWindowCollectionBehaviorStationary.
        const ulong NSWindowCollectionBehaviorStationary = 1 << 4; // 16
        ulong behavior = ObjC.MsgSend_ulong(_nsWindow, ObjC.Sel("collectionBehavior"));
        behavior = value ? (behavior & ~NSWindowCollectionBehaviorStationary) : (behavior | NSWindowCollectionBehaviorStationary);
        ObjC.MsgSend_void_nint_ulong(_nsWindow, ObjC.Sel("setCollectionBehavior:"), behavior);
    }

    public void BeginDragMove()
    {
        if (_nsWindow == 0)
        {
            return;
        }

        // [NSApp currentEvent] → [window performWindowDragWithEvent:]
        var nsApp = ObjC.MsgSend_nint(ObjC.GetClass("NSApplication"), ObjC.Sel("sharedApplication"));
        if (nsApp == 0)
        {
            return;
        }

        var currentEvent = ObjC.MsgSend_nint(nsApp, ObjC.Sel("currentEvent"));
        if (currentEvent == 0)
        {
            return;
        }

        ObjC.MsgSend_void_nint_nint(_nsWindow, ObjC.Sel("performWindowDragWithEvent:"), currentEvent);
    }

    private NSRect _restoreFrame;

    public void SetWindowState(Controls.WindowState state)
    {
        if (_nsWindow == 0)
        {
            return;
        }

        switch (state)
        {
            case Controls.WindowState.Minimized:
                // MewUIWindow.miniaturize: override handles styleMask temporarily
                ObjC.MsgSend_void_nint_nint(_nsWindow, ObjC.Sel("miniaturize:"), 0);
                break;

            case Controls.WindowState.Maximized:
                if (_allowsTransparency || _extendTitleBarHeight > 0)
                {
                    // Manual maximize using screen visibleFrame
                    _restoreFrame = ObjC.MsgSend_rect(_nsWindow, ObjC.Sel("frame"));
                    var screen = ObjC.MsgSend_nint(_nsWindow, ObjC.Sel("screen"));
                    if (screen != 0)
                    {
                        var visibleFrame = ObjC.MsgSend_rect(screen, ObjC.Sel("visibleFrame"));
                        ObjC.MsgSend_void_nint_rect_bool(_nsWindow, ObjC.Sel("setFrame:display:"), visibleFrame, true);
                    }
                }
                else if (!IsZoomed())
                {
                    ObjC.MsgSend_void_nint_nint(_nsWindow, ObjC.Sel("zoom:"), 0);
                }
                break;

            case Controls.WindowState.Normal:
                if (ObjC.MsgSend_bool(_nsWindow, ObjC.Sel("isMiniaturized")))
                {
                    ObjC.MsgSend_void_nint_nint(_nsWindow, ObjC.Sel("deminiaturize:"), 0);
                    ObjC.MsgSend_void_nint_nint(_nsWindow, ObjC.Sel("makeKeyAndOrderFront:"), 0);
                    _window.Invalidate();
                }
                else if (_allowsTransparency || _extendTitleBarHeight > 0)
                {
                    // Restore from manual maximize
                    if (_restoreFrame.size.width > 0 && _restoreFrame.size.height > 0)
                    {
                        ObjC.MsgSend_void_nint_rect_bool(_nsWindow, ObjC.Sel("setFrame:display:"), _restoreFrame, true);
                    }
                }
                else if (IsZoomed())
                {
                    ObjC.MsgSend_void_nint_nint(_nsWindow, ObjC.Sel("zoom:"), 0);
                }
                break;
        }
    }

    private bool IsZoomed() => ObjC.MsgSend_bool(_nsWindow, ObjC.Sel("isZoomed"));

    public void Invalidate(bool erase)
    {
        if (_nsWindow == 0)
        {
            return;
        }

        // Coalesce invalidations.
        if (Interlocked.Exchange(ref _needsRender, 1) == 1)
        {
            return;
        }

        _host.RequestRender();
    }

    public void SetTitle(string title)
    {
        if (_nsWindow != 0)
        {
            MacOSWindowInterop.SetTitle(_nsWindow, title ?? string.Empty);
        }
    }

    public void SetIcon(IconSource? icon)
    { }

    public void SetClientSize(double widthDip, double heightDip)
    {
        if (_nsWindow != 0)
        {
            MacOSWindowInterop.SetClientSize(_nsWindow, widthDip, heightDip);
            _window.SetClientSizeDip(widthDip, heightDip);
        }
    }

    public Point GetPosition()
    {
        if (_nsWindow == 0)
        {
            return default;
        }

        var frame = MacOSWindowInterop.GetWindowFrame(_nsWindow);
        var screenFrame = GetPositioningScreenFrame();
        if (screenFrame.size.height <= 0)
        {
            return new Point(frame.origin.x, frame.origin.y);
        }

        double top = (screenFrame.origin.y + screenFrame.size.height) - (frame.origin.y + frame.size.height);
        return new Point(frame.origin.x, top);
    }

    public void SetPosition(double leftDip, double topDip)
    {
        if (_nsWindow == 0)
        {
            return;
        }

        var frame = MacOSWindowInterop.GetWindowFrame(_nsWindow);
        var screenFrame = GetPositioningScreenFrame();
        if (screenFrame.size.height <= 0)
        {
            MacOSWindowInterop.SetWindowPosition(_nsWindow, leftDip, topDip);
            return;
        }

        double cocoaTop = screenFrame.origin.y + screenFrame.size.height;
        double cocoaY = cocoaTop - topDip - frame.size.height;
        MacOSWindowInterop.SetWindowPosition(_nsWindow, leftDip, cocoaY);
    }

    public void CaptureMouse()
    { }

    public void ReleaseMouseCapture()
    { }

    // Cocoa screen coordinates are Y-up (origin at the screen's bottom-left). The framework's screen-pixel
    // contract is top-left, Y-down (see IPlatformHost.GetCursorScreenPosition), matching Window.MoveTo. These
    // helpers convert between the two so all macOS screen pixels honor the top-down contract.
    private Point CocoaScreenPointToTopLeftPx(NSPoint cocoaPoint)
    {
        double scale = _lastDpiScale > 0 ? _lastDpiScale : 1.0;
        var screenFrame = GetPositioningScreenFrame();
        double topY = (screenFrame.origin.y + screenFrame.size.height) - cocoaPoint.y;
        return new Point(cocoaPoint.x * scale, topY * scale);
    }

    private NSPoint TopLeftPxToCocoaScreenPoint(Point topLeftPx)
    {
        double scale = _lastDpiScale > 0 ? _lastDpiScale : 1.0;
        var screenFrame = GetPositioningScreenFrame();
        double cocoaY = (screenFrame.origin.y + screenFrame.size.height) - (topLeftPx.Y / scale);
        return new NSPoint(topLeftPx.X / scale, cocoaY);
    }

    public Point ClientToScreen(Point clientPointDip)
    {
        if (_nsWindow == 0)
        {
            return clientPointDip;
        }

        var client = _window.ClientSize;
        var windowPoint = new NSPoint(clientPointDip.X, client.Height - clientPointDip.Y);
        var screenPoint = MacOSWindowInterop.WindowConvertPointToScreen(_nsWindow, windowPoint);
        return CocoaScreenPointToTopLeftPx(screenPoint);
    }

    public Point ScreenToClient(Point screenPointPx)
    {
        if (_nsWindow == 0)
        {
            return screenPointPx;
        }

        var client = _window.ClientSize;
        var windowPoint = MacOSWindowInterop.WindowConvertPointFromScreen(
            _nsWindow,
            TopLeftPxToCocoaScreenPoint(screenPointPx));
        return new Point(windowPoint.x, client.Height - windowPoint.y);
    }

    public void CenterOnOwner()
    {
        if (_nsWindow == 0 || _window.Owner is not { } ownerWindow || ownerWindow.Handle == 0)
        {
            return;
        }

        var ownerFrame = MacOSWindowInterop.GetWindowFrame(MacOSWindowInterop.GetWindowFromView(ownerWindow.Handle));
        var frame = MacOSWindowInterop.GetWindowFrame(_nsWindow);
        double x = ownerFrame.origin.x + ((ownerFrame.size.width - frame.size.width) * 0.5);
        // macOS Y-up: 0.75 = upper bias (equivalent to 0.25 in Y-down systems)
        double y = ownerFrame.origin.y + ((ownerFrame.size.height - frame.size.height) * 0.75);
        MacOSWindowInterop.SetWindowPosition(_nsWindow, x, y);
    }

    public void EnsureTheme(bool isDark)
    {
        if (_nsWindow == 0)
        {
            return;
        }

        // Only override the window chrome when the app is not following the OS theme.
        if (!Application.IsRunning)
        {
            return;
        }

        ThemeVariant mode;
        try
        {
            mode = Application.Current.ThemeMode;
        }
        catch
        {
            return;
        }

        if (mode == ThemeVariant.System)
        {
            MacOSWindowInterop.ClearWindowAppearance(_nsWindow);
        }
        else
        {
            MacOSWindowInterop.SetWindowAppearance(_nsWindow, isDark);
        }
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;

        if (!enabled)
        {
            _window.ClearMouseOverState();
            _window.ClearMouseCaptureState();
        }
    }

    internal bool IsEnabled => _enabled;

    internal void NotifyInputWhenDisabled()
    {
        _window.NotifyInputWhenDisabled();
    }

    public void Activate()
    {
        if (_nsWindow == 0)
        {
            return;
        }

        MacOSWindowInterop.ActivateWindow(_nsWindow);
    }

    public void SetOwner(nint ownerHandle)
    {
        if (_nsWindow == 0)
        {
            return;
        }

        var ownerWindow = MacOSWindowInterop.GetWindowFromView(ownerHandle);
        if (ownerWindow == 0)
        {
            ownerWindow = ownerHandle;
        }

        MacOSWindowInterop.SetOwnerWindow(_nsWindow, ownerWindow);
        if (ownerWindow != 0)
        {
            long ownerLevel = MacOSWindowInterop.GetWindowLevel(ownerWindow);
            MacOSWindowInterop.SetWindowLevel(_nsWindow, ownerLevel + 1);
        }
        else
        {
            MacOSWindowInterop.SetWindowLevel(_nsWindow, _defaultWindowLevel);
        }
    }

    public void SetOpacity(double opacity)
    {
        _opacity = Math.Clamp(opacity, 0.0, 1.0);
        if (_nsWindow != 0)
        {
            MacOSWindowInterop.SetWindowOpacity(_nsWindow, _opacity);
        }
    }

    public void SetAllowsTransparency(bool allowsTransparency)
    {
        _allowsTransparency = allowsTransparency;
        if (_nsWindow != 0)
        {
            MacOSWindowInterop.SetWindowTransparency(_nsWindow, _nsView, _allowsTransparency);
            MacOSWindowInterop.SetWindowStyleMask(_nsWindow, _allowsTransparency ? MacOSWindowInterop.TransparentStyleMask : _defaultStyleMask);
            MacOSWindowInterop.SetTitlebarForTransparency(_nsWindow, _allowsTransparency);
            if (_metalLayer != 0)
            {
                MacOSWindowInterop.SetLayerOpaque(_metalLayer, !_allowsTransparency);
            }
        }
    }

    /// <inheritdoc/>
    public void SetAllowDrop(bool allow)
    {
        if (_allowDrop == allow) return;
        _allowDrop = allow;
        // If the NSView has been created, apply the change immediately; otherwise EnsureCreated picks it up.
        if (_nsView != 0)
        {
            if (allow) MacOSWindowInterop.RegisterForDragDrop(_nsView);
            else MacOSWindowInterop.UnregisterFromDragDrop(_nsView);
        }
    }

    internal double _extendTitleBarHeight;

    public void SetExtendClientAreaToTitleBar(double titleBarHeight)
    {
        _extendTitleBarHeight = titleBarHeight;
        if (_nsWindow == 0)
        {
            return;
        }

        // Do not change styleMask during fullscreen transitions — macOS will throw.
        var currentMask = MacOSWindowInterop.GetWindowStyleMask(_nsWindow);
        const ulong NSWindowStyleMaskFullScreen = 1ul << 14;
        if ((currentMask & NSWindowStyleMaskFullScreen) != 0)
        {
            return;
        }

        if (titleBarHeight > 0)
        {
            const ulong ExtendedStyleMask = 1ul | 2ul | 4ul | 8ul | (1ul << 15);
            MacOSWindowInterop.SetWindowStyleMask(_nsWindow, ExtendedStyleMask);
            MacOSWindowInterop.SetTitlebarForTransparency(_nsWindow, true);
            ApplyRequestedClientSize();
        }
        else
        {
            MacOSWindowInterop.SetWindowStyleMask(_nsWindow, _defaultStyleMask);
            MacOSWindowInterop.SetTitlebarForTransparency(_nsWindow, false);
        }

        Invalidate(erase: false);
    }

    public void SetWindowBorderColor(Color? color)
    {
        // macOS doesn't have a direct API for window border color.
        // The window frame color is determined by the system appearance.
    }

    public WindowChromeCapabilities ChromeCapabilities =>
        WindowChromeCapabilities.ExtendClientArea
        | WindowChromeCapabilities.NativeChromeButtons
        | WindowChromeCapabilities.NativeWindowBorder;

    public Thickness NativeChromeButtonInset
    {
        get
        {
            if (_extendTitleBarHeight <= 0 || _nsWindow == 0)
            {
                return default;
            }

            // In fullscreen, traffic light buttons are auto-hidden — no inset needed.
            var mask = MacOSWindowInterop.GetWindowStyleMask(_nsWindow);
            const ulong NSWindowStyleMaskFullScreen = 1ul << 14;
            if ((mask & NSWindowStyleMaskFullScreen) != 0)
            {
                return default;
            }

            // Read the frame of the zoom button (index 2, rightmost traffic light).
            var zoomBtn = ObjC.MsgSend_nint_ulong(_nsWindow, ObjC.Sel("standardWindowButton:"), 2);
            if (zoomBtn == 0)
            {
                return new Thickness(70, 0, 0, 0); // fallback
            }

            var frame = ObjC.MsgSend_rect(zoomBtn, ObjC.Sel("frame"));
            double rightEdge = frame.origin.x + frame.size.width + 8;
            return new Thickness(rightEdge, 0, 0, 0);
        }
    }

    public void SetCursor(CursorType cursorType)
    {
        MacOSWindowInterop.SetCursor(cursorType);
    }

    public void SetImeMode(ImeMode mode)
    { }

    public void CancelImeComposition()
    {
        if (_imeHasMarkedText)
        {
            ImeUnmarkText();
        }
    }

    public void Dispose()
    {
        _window.ClearMouseOverState();
        _window.ClearMouseCaptureState();
        if (_nsWindow != 0)
        {
            _host.UnregisterWindow(_nsWindow);
            MacOSWindowInterop.UnregisterWindowCloseTarget(_nsWindow);
            MacOSWindowInterop.UnregisterTextInputTarget(_nsView);
            if (_metalLayer != 0)
            {
                MacOSWindowInterop.UnregisterMetalLayerTarget(_metalLayer);
            }
            MacOSWindowInterop.ReleaseWindow(_nsWindow);
            _nsWindow = 0;
            _nsView = 0;
            _metalLayer = 0;
        }
    }

    private DragEventArgs CreateDragEventArgs(IReadOnlyList<string> paths, NSPoint windowPoint)
    {
        var client = _window.ClientSize;
        var localPoint = new Point(windowPoint.x, client.Height - windowPoint.y);
        var screenPoint = MacOSWindowInterop.WindowConvertPointToScreen(_nsWindow, windowPoint);

        var data = new DataObject(new Dictionary<string, object>
        {
            [StandardDataFormats.StorageItems] = paths,
        });

        return new DragEventArgs(
            data,
            localPoint,
            CocoaScreenPointToTopLeftPx(screenPoint));
    }

    internal ulong HandleNativeDragEnter(IReadOnlyList<string> paths, NSPoint windowPoint)
    {
        if (paths.Count == 0)
        {
            return 0;
        }

        var args = CreateDragEventArgs(paths, windowPoint);
        _lastDragEventArgs = args;
        WindowDragDropRouter.OnExternalDragEnter(_window, args);
        return args.Accepted ? (ulong)args.Effect : 0;
    }

    internal ulong HandleNativeDragOver(IReadOnlyList<string> paths, NSPoint windowPoint)
    {
        if (paths.Count == 0)
        {
            return 0;
        }

        var args = CreateDragEventArgs(paths, windowPoint);
        _lastDragEventArgs = args;
        WindowDragDropRouter.OnExternalDragOver(_window, args);
        return args.Accepted ? (ulong)args.Effect : 0;
    }

    internal void HandleNativeDragLeave()
    {
        if (_lastDragEventArgs is { } args)
        {
            WindowDragDropRouter.OnExternalDragLeave(_window, args);
        }

        _lastDragEventArgs = null;
    }

    internal bool HandleNativeDrop(IReadOnlyList<string> paths, NSPoint windowPoint)
    {
        if (paths.Count == 0)
        {
            return false;
        }

        var args = CreateDragEventArgs(paths, windowPoint);
        _lastDragEventArgs = args;
        var effect = WindowDragDropRouter.OnExternalDrop(_window, args);
        return effect != DragDropEffects.None || args.Handled;
    }

    private void EnsureCreated()
    {
        if (_nsWindow != 0)
        {
            return;
        }

        MacOSInterop.EnsureApplicationInitialized();
        _allowsTransparency = _window.AllowsTransparency;
        var initialClientSize = GetInitialClientSize();
        _nsWindow = MacOSWindowInterop.CreateWindow(
            title: _window.Title ?? "MewUI",
            widthDip: initialClientSize.Width,
            heightDip: initialClientSize.Height,
            allowsTransparency: _allowsTransparency,
            isDialog: _window.IsDialogWindow,
            isToolWindow: _window.IsToolWindow);

        if (_nsWindow != 0)
        {
            // CAMetalLayer is the only supported surface — render from the layer's draw cycle to
            // avoid AppKit's "stretch last frame" behavior during live-resize. (The NSOpenGLView
            // legacy fallback was removed once the macOS backend stabilized on Metal.)
            var (view, layer) = MacOSWindowInterop.AttachMetalLayerView(_nsWindow, _window.Width, _window.Height);
            if (view != 0 && (Math.Abs(initialClientSize.Width - _window.Width) > 0.01 || Math.Abs(initialClientSize.Height - _window.Height) > 0.01))
            {
                MacOSWindowInterop.SetViewFrame(view, initialClientSize.Width, initialClientSize.Height);
            }
            _nsView = view;
            _metalLayer = layer;
            MacOSWindowInterop.RegisterTextInputTarget(_nsView, this);
            if (_allowDrop) MacOSWindowInterop.RegisterForDragDrop(_nsView);
            MacOSWindowInterop.RegisterMetalLayerTarget(_metalLayer, this);
            MacOSWindowInterop.SetFirstResponder(_nsWindow, _nsView);
            if (_metalLayer != 0)
            {
                MacOSWindowInterop.SetLayerOpaque(_metalLayer, !_allowsTransparency);
            }
            UpdateMetalLayerDisplaySyncIfNeeded();

            // Establish initial DPI once we have a view/screen.
            UpdateDpiIfNeeded(force: true);
            ApplyRequestedClientSize();
            UpdateClientSizeIfNeeded(forceLayout: true);
            ApplyResolvedStartupPlacement();

            _defaultWindowLevel = MacOSWindowInterop.GetWindowLevel(_nsWindow);
            _defaultStyleMask = MacOSWindowInterop.GetWindowStyleMask(_nsWindow);
            MacOSWindowInterop.SetWindowOpacity(_nsWindow, _opacity);
            MacOSWindowInterop.SetWindowTransparency(_nsWindow, _nsView, _allowsTransparency);
            MacOSWindowInterop.SetWindowStyleMask(_nsWindow, _allowsTransparency ? MacOSWindowInterop.TransparentStyleMask : _defaultStyleMask);
            MacOSWindowInterop.SetTitlebarForTransparency(_nsWindow, _allowsTransparency);
            MacOSWindowInterop.RegisterWindowCloseTarget(_nsWindow, this);

            // Apply extended client area if set before window creation.
            if (_extendTitleBarHeight > 0)
            {
                ulong extMask = 1ul | 2ul | 4ul | (1ul << 15);
                if (_window.WindowSize.IsResizable) extMask |= 8ul;
                MacOSWindowInterop.SetWindowStyleMask(_nsWindow, extMask);
                MacOSWindowInterop.SetTitlebarForTransparency(_nsWindow, true);
            }
        }
    }

    private void ApplyRequestedClientSize()
    {
        if (_nsWindow == 0)
        {
            return;
        }

        var requestedSize = GetInitialClientSize();
        MacOSWindowInterop.SetClientSize(_nsWindow, requestedSize.Width, requestedSize.Height);
        _window.SetClientSizeDip(requestedSize.Width, requestedSize.Height);
    }

    private Size GetInitialClientSize()
    {
        var ws = _window.WindowSize;
        var current = _window.ClientSize;

        double width = !double.IsNaN(ws.Width) ? ws.Width : Math.Max(1, current.Width);
        double height = !double.IsNaN(ws.Height) ? ws.Height : Math.Max(1, current.Height);

        return new Size(Math.Max(1, width), Math.Max(1, height));
    }

    private void ApplyResolvedStartupPlacement()
    {
        if (_nsWindow == 0)
        {
            return;
        }

        switch (_window.StartupLocation)
        {
            case WindowStartupLocation.CenterScreen:
                MacOSWindowInterop.CenterWindow(_nsWindow);
                return;

            case WindowStartupLocation.Manual:
                if (_window.ResolvedStartupPosition is { } manualPosition)
                {
                    SetPosition(manualPosition.X, manualPosition.Y);
                }
                return;

            case WindowStartupLocation.CenterOwner:
                if (_window.Owner is { } ownerWindow && ownerWindow.Handle != 0)
                {
                    var ownerFrame = MacOSWindowInterop.GetWindowFrame(MacOSWindowInterop.GetWindowFromView(ownerWindow.Handle));
                    var frame = MacOSWindowInterop.GetWindowFrame(_nsWindow);
                    double x = ownerFrame.origin.x + ((ownerFrame.size.width - frame.size.width) * 0.5);
                    double y = ownerFrame.origin.y + ((ownerFrame.size.height - frame.size.height) * 0.75);
                    MacOSWindowInterop.SetWindowPosition(_nsWindow, x, y);
                }
                return;
        }
    }

    internal void RaiseClosedOnce()
    {
        if (_closedRaised)
        {
            return;
        }

        _closedRaised = true;
        try
        {
            _window.RaiseClosed();
        }
        catch
        {
            // Never let exceptions escape the native callback.
        }
    }

    private NSRect GetPositioningScreenFrame()
    {
        if (_nsWindow != 0)
        {
            var screenFrame = MacOSWindowInterop.GetScreenFrame(_nsWindow);
            if (screenFrame.size.width > 0 && screenFrame.size.height > 0)
            {
                return screenFrame;
            }
        }

        return MacOSInterop.GetMainScreenFrame();
    }

    private void UpdateDpiIfNeeded(bool force = false)
    {
        if (_nsView == 0)
        {
            return;
        }

        double scale = MacOSInterop.GetBackingScaleFactorForView(_nsView);
        if (scale <= 0)
        {
            scale = 1.0;
        }

        if (!force && Math.Abs(scale - _lastDpiScale) < 0.001)
        {
            return;
        }

        _lastDpiScale = scale;
        uint newDpi = (uint)Math.Max(1, (int)Math.Round(96.0 * scale));
        uint oldDpi = _window.Dpi;
        if (oldDpi == newDpi)
        {
            return;
        }

        _window.SetDpi(newDpi);
        _window.RaiseDpiChanged(oldDpi, newDpi);

        // Ensure text/layout are recomputed at the new scale before the next frame.
        UpdateClientSizeIfNeeded(forceLayout: true);
    }

    private bool UpdateClientSizeIfNeeded(bool forceLayout = false, bool requestRender = true)
    {
        if (_nsView == 0)
        {
            return false;
        }

        var bounds = MacOSInterop.GetViewBounds(_nsView);
        double widthDip = Math.Max(1, bounds.size.width);
        double heightDip = Math.Max(1, bounds.size.height);

        var old = _window.ClientSize;
        if (!forceLayout &&
            Math.Abs(old.Width - widthDip) < 0.01 &&
            Math.Abs(old.Height - heightDip) < 0.01)
        {
            return false;
        }

        if (_metalLayer != 0)
        {
            MacOSWindowInterop.UpdateMetalLayerDrawableSize(_metalLayer, widthDip, heightDip, _lastDpiScale);
        }

        _window.SetClientSizeDip(widthDip, heightDip);
        _window.RaiseClientSizeChanged(widthDip, heightDip);

        _window.PerformLayout();
        if (requestRender)
        {
            _window.Invalidate();
        }

        // Detect zoom state changes (e.g. green traffic light button) and sync with MewUI WindowState.
        SyncZoomState();

        return true;
    }

    private void SyncZoomState()
    {
        if (_nsWindow == 0)
        {
            return;
        }

        // Skip during fullscreen transitions.
        var mask = MacOSWindowInterop.GetWindowStyleMask(_nsWindow);
        const ulong NSWindowStyleMaskFullScreen = 1ul << 14;
        if ((mask & NSWindowStyleMaskFullScreen) != 0)
        {
            return;
        }

        bool zoomed = IsZoomed();
        var currentState = _window.WindowState;

        if (zoomed && currentState != Controls.WindowState.Maximized)
        {
            _window.SetWindowStateFromBackend(Controls.WindowState.Maximized);
        }
        else if (!zoomed && currentState == Controls.WindowState.Maximized
                 && !ObjC.MsgSend_bool(_nsWindow, ObjC.Sel("isMiniaturized")))
        {
            _window.SetWindowStateFromBackend(Controls.WindowState.Normal);
        }
    }

    internal void ProcessNSEvent(nint ev)
    {
        if (ev == 0 || _nsWindow == 0)
        {
            return;
        }

        // Ignore internal wake events posted by the dispatcher (application-defined, no window).
        int type = MacOSInterop.GetEventType(ev);
        if (type == 15 && MacOSInterop.GetEventWindow(ev) == 0)
        {
            return;
        }

        if (_window.HasNativeMessageHandler)
        {
            var hookArgs = new MacOSNativeMessageEventArgs(ev, type);
            if (_window.RaiseNativeMessage(hookArgs))
            {
                return;
            }
        }

        if (!_enabled)
        {
            _window.NotifyInputWhenDisabled();
            return;
        }

        // Ensure we have up-to-date client size for coordinate transforms.
        _ = UpdateClientSizeIfNeeded();

        var loc = MacOSInterop.GetEventLocationInWindow(ev);
        var client = _window.ClientSize;
        var pos = new Point(loc.x, client.Height - loc.y);

        var screenPos = ClientToScreen(pos);
        _window.UpdateLastMousePosition(pos, screenPos);

        switch (type)
        {
            // Mouse moved / dragged
            case 5:  // NSEventTypeMouseMoved
            case 6:  // NSEventTypeLeftMouseDragged
            case 7:  // NSEventTypeRightMouseDragged
            case 27: // NSEventTypeOtherMouseDragged
                if (ShouldIgnoreMouseEvent(ev, pos, client, allowOutsideWhileCaptured: true))
                {
                    return;
                }
                HandleMouseMove(pos, screenPos);
                break;

            // Mouse down/up
            case 1:  // NSEventTypeLeftMouseDown
                HandleMouseButton(ev, pos, screenPos, MouseButton.Left, isDown: true);
                break;

            case 2:  // NSEventTypeLeftMouseUp
                HandleMouseButton(ev, pos, screenPos, MouseButton.Left, isDown: false);
                break;

            case 3:  // NSEventTypeRightMouseDown
                HandleMouseButton(ev, pos, screenPos, MouseButton.Right, isDown: true);
                break;

            case 4:  // NSEventTypeRightMouseUp
                HandleMouseButton(ev, pos, screenPos, MouseButton.Right, isDown: false);
                break;

            case 25: // NSEventTypeOtherMouseDown
                HandleMouseButton(ev, pos, screenPos, MapOtherMouseButton(ev), isDown: true);
                break;

            case 26: // NSEventTypeOtherMouseUp
                HandleMouseButton(ev, pos, screenPos, MapOtherMouseButton(ev), isDown: false);
                break;

            case 9: // NSEventTypeMouseExited
                WindowInputRouter.UpdateMouseOver(_window, null);
                break;

            case 22: // NSEventTypeScrollWheel
                if (ShouldIgnoreMouseEvent(ev, pos, client, allowOutsideWhileCaptured: true))
                {
                    return;
                }
                HandleMouseWheel(ev, pos, screenPos);
                break;

            case 10: // NSEventTypeKeyDown
                HandleKeyDown(ev);
                break;

            case 11: // NSEventTypeKeyUp
                HandleKeyUp(ev);
                break;
        }
    }

    private bool ShouldIgnoreMouseEvent(nint ev, Point pos, Size client, bool allowOutsideWhileCaptured)
    {
        var evWindow = MacOSInterop.GetEventWindow(ev);
        bool isCaptured = _window.HasMouseCapture || _leftDown || _rightDown || _middleDown;

        if (evWindow != 0 && evWindow != _nsWindow && !isCaptured)
        {
            return true;
        }

        if (!allowOutsideWhileCaptured || !isCaptured)
        {
            bool inside = pos.X >= 0 && pos.Y >= 0 && pos.X < client.Width && pos.Y < client.Height;
            if (!inside)
            {
                WindowInputRouter.UpdateMouseOver(_window, null);
                return true;
            }
        }

        return false;
    }

    private void HandleMouseMove(Point pos, Point screenPos)
    {
        WindowInputRouter.MouseMove(_window, pos, screenPos, _leftDown, _rightDown, _middleDown);
    }

    private void HandleMouseButton(nint ev, Point pos, Point screenPos, MouseButton button, bool isDown)
    {
        // Keep our view as first responder so key input / NSTextInputClient (IME) continues to work
        // after mouse interaction. AppKit can move first responder to internal views during activation.
        if (isDown && _nsWindow != 0 && _nsView != 0)
        {
            MacOSWindowInterop.SetFirstResponder(_nsWindow, _nsView);
        }

        int clickCount;
        int buttonIndex = (int)button;
        if (isDown)
        {
            clickCount = MacOSInterop.GetEventClickCount(ev);
            if ((uint)buttonIndex < (uint)_lastPressClickCounts.Length)
            {
                _lastPressClickCounts[buttonIndex] = clickCount <= 0 ? 1 : clickCount;
            }
        }
        else
        {
            clickCount = (uint)buttonIndex < (uint)_lastPressClickCounts.Length ? _lastPressClickCounts[buttonIndex] : 1;
            if (clickCount <= 0)
            {
                clickCount = 1;
            }
        }

        switch (button)
        {
            case MouseButton.Left:
                _leftDown = isDown;
                break;

            case MouseButton.Right:
                _rightDown = isDown;
                break;

            case MouseButton.Middle:
                _middleDown = isDown;
                break;
        }

        WindowInputRouter.MouseButton(
            _window,
            pos,
            screenPos,
            button,
            isDown,
            _leftDown,
            _rightDown,
            _middleDown,
            clickCount);
    }

    // Trackpad point-delta → notch normalization.
    // Matches Avalonia AvnView.mm (precise speed=50) for macOS-native feel.
    // Backend-private — never references Theme or any input-layer constant.
    private const double TrackpadPointsPerNotch = 50.0;

    private void HandleMouseWheel(nint ev, Point pos, Point screenPos)
    {
        // Prefer high-precision deltas when available, but fall back to legacy deltaX/deltaY for devices
        // where scrollingDelta returns 0.
        double dy = MacOSInterop.GetEventScrollingDeltaY(ev);
        double dx = MacOSInterop.GetEventScrollingDeltaX(ev);
        if (dy == 0)
        {
            dy = MacOSInterop.GetEventDeltaY(ev);
        }
        if (dx == 0)
        {
            dx = MacOSInterop.GetEventDeltaX(ev);
        }

        bool precise = MacOSInterop.GetEventHasPreciseScrollingDeltas(ev);

        // Convert to MouseWheelEventArgs.Delta convention (notches, +Y = up, +X = left).
        // NSEvent's scrollingDelta already uses the "+Y = scroll up, +X = scroll left" sign
        // convention so no sign flip is needed — only unit normalization.
        double notchesY;
        double notchesX;
        if (precise)
        {
            // Trackpad / Magic Mouse: scrollingDelta is in points (DIPs).
            notchesY = dy / TrackpadPointsPerNotch;
            notchesX = dx / TrackpadPointsPerNotch;
        }
        else
        {
            // Classic mouse wheel: scrollingDelta is already in line/notch units (≈1.0 per notch).
            notchesY = dy;
            notchesX = dx;
        }

        if (notchesY == 0 && notchesX == 0)
        {
            return;
        }

        WindowInputRouter.MouseWheel(
            _window, pos, screenPos,
            new Vector(notchesX, notchesY),
            _leftDown, _rightDown, _middleDown);
    }

    private void HandleKeyDown(nint ev)
    {
        int platformKey = MacOSInterop.GetEventKeyCode(ev);
        var modifiers = GetModifierKeys(ev);
        var key = MapKey(ev, platformKey);

        var oldImeState = _imeState;
        _forwardKeyToAppThisKeyDown = false;
        _pendingKeyDownTextInput = null;

        _isHandlingKeyDown = true;
        try
        {
            // Route through NSTextInputClient so IME/dead-keys AND plain text input can be delivered via insertText/setMarkedText.
            // This must not be gated on PreviewKeyDown handling: containers may handle key events (e.g. shortcuts),
            // but IME still needs the native key events to finalize composition and deliver committed text.
            if (_nsWindow != 0 && _nsView != 0)
            {
                MacOSWindowInterop.SetFirstResponder(_nsWindow, _nsView);
                MacOSWindowInterop.InterpretKeyEvent(_nsView, ev);
                ImeLogger.Write($"InterpretKeyEvent view=0x{_nsView:x} window=0x{_nsWindow:x} imeHasMarked={_imeHasMarkedText} imeState={_imeState}");
            }

            // Shortcuts (Ctrl/Cmd chords) must be routed as key events even while IME is composing.
            // Otherwise, common shortcuts like Ctrl+Z/C/V will stop working during preedit.
            // This mirrors typical Cocoa behavior: text services should not "eat" modifier chords that
            // don't represent text input.
            bool isShortcutChord = (modifiers.HasFlag(ModifierKeys.Control) || modifiers.HasFlag(ModifierKeys.Meta)) &&
                                   key != Key.None;
            if (isShortcutChord)
            {
                _forwardKeyToAppThisKeyDown = true;
                _forceKeyUps.Add(platformKey);
            }

            bool hadImeInput = _imeState switch
            {
                ImeState.Committed => true,
                ImeState.Preedit => true,
                _ => oldImeState != _imeState,
            };

            // Allow normal key processing after commit, but still treat this keyDown as IME-related.
            if (_imeState == ImeState.Committed)
            {
                _imeState = ImeState.Ground;
            }

            // winit behavior: only forward KeyDown when IME didn't handle it, OR when doCommandBySelector requested it.
            if (hadImeInput && !_forwardKeyToAppThisKeyDown)
            {
                ImeLogger.Write($"KeyDown suppressed (IME handled). oldImeState={oldImeState} imeState={_imeState}");
                return;
            }

            var args = new KeyEventArgs(key, platformKey, modifiers, isRepeat: false);
            _window.RaisePreviewKeyDown(args);

            ImeLogger.Write($"KeyDown ev=0x{ev:x} keyCode=0x{platformKey:x} key={key} mods={modifiers} handled(preview)={args.Handled} focused={_window.FocusManager.FocusedElement?.GetType().Name ?? "null"} imeState={_imeState} forwardToApp={_forwardKeyToAppThisKeyDown}");

            if (args.Handled)
            {
                return;
            }

            WindowInputRouter.KeyDown(_window, args);
            _window.ProcessKeyBindings(args);
            _window.ProcessAccessKeyDown(args);

            // WPF-like Tab behavior:
            // - Always let the focused element see KeyDown first.
            // - Only perform focus navigation if the key is still unhandled.
            //
            // When IME composition is active, many IMEs use Tab to navigate candidates,
            // so we must not steal it.
            if (!args.Handled && args.Key == Key.Tab && !_imeHasMarkedText)
            {
                if (modifiers.HasFlag(ModifierKeys.Shift))
                {
                    _window.FocusManager.MoveFocusPrevious();
                }
                else
                {
                    _window.FocusManager.MoveFocusNext();
                }

                _pendingKeyDownTextInput = null;
                return;
            }

            // If insertText delivered a Tab/newline during this keyDown, defer it until after KeyDown routing.
            // This allows KeyDown handlers (e.g. AcceptTab/AcceptReturn) to suppress text input consistently.
            if (_pendingKeyDownTextInput is { Length: > 0 } pending)
            {
                if (args.Handled)
                {
                    ImeLogger.Write($"  pending insertText suppressed by handled KeyDown. pending='{Truncate(pending)}'");
                    return;
                }

                var textArgs = new TextInputEventArgs(pending);
                _window.RaisePreviewTextInput(textArgs);
                if (!textArgs.Handled)
                {
                    if (_window.FocusManager.FocusedElement is ITextInputClient client)
                    {
                        client.HandleTextInput(textArgs);
                    }
                }

                ImeLogger.Write($"  pending insertText emitted TextInput handled={textArgs.Handled}");
            }
        }
        finally
        {
            _isHandlingKeyDown = false;
            _pendingKeyDownTextInput = null;
        }
    }

    private void HandleKeyUp(nint ev)
    {
        int platformKey = MacOSInterop.GetEventKeyCode(ev);
        if (!_forceKeyUps.Remove(platformKey) && _imeState is not (ImeState.Ground or ImeState.Disabled))
        {
            ImeLogger.Write($"KeyUp suppressed keyCode=0x{platformKey:x} imeState={_imeState}");
            return;
        }

        var modifiers = GetModifierKeys(ev);
        var key = MapKey(ev, platformKey);

        var args = new KeyEventArgs(key, platformKey, modifiers, isRepeat: false);
        _window.RaisePreviewKeyUp(args);
        if (args.Handled)
        {
            return;
        }

        WindowInputRouter.KeyUp(_window, args);
        _window.ProcessAccessKeyUp(args);
    }

    internal void ImeSetMarkedText(string? text)
        => ImeSetMarkedText(text, new NSRange(NSNotFound, 0));

    internal void ImeSetMarkedText(string? text, NSRange replacementRange)
    {
        text ??= string.Empty;
        ImeLogger.Write($"setMarkedText len={text.Length} text='{Truncate(text)}' repl=({replacementRange.location},{replacementRange.length}) imeHasMarked(before)={_imeHasMarkedText} focused={_window.FocusManager.FocusedElement?.GetType().Name ?? "null"}");

        // Some IMEs clear preedit by calling setMarkedText("") instead of unmarkText.
        // Treat empty marked text as "composition ended".
        if (text.Length == 0)
        {
            ImeUnmarkText();
            return;
        }

        if (!_imeHasMarkedText)
        {
            // If the platform provides a replacement range, align our selection/caret so the IME composition
            // replaces the correct portion of the document.
            // (AppKit's NSRange is UTF-16 based, which matches .NET string indexing.)
            if (replacementRange.location != NSNotFound && _window.FocusManager.FocusedElement is Controls.TextBase tb2)
            {
                int start = (int)replacementRange.location;
                int end = start + (int)replacementRange.length;
                tb2.SetSelectionRangeForPlatform(start, end);
            }

            _imeHasMarkedText = true;
            _imeMarkedText = string.Empty;
            _imeState = ImeState.Preedit;

            var startArgs = new TextCompositionEventArgs();
            _window.RaisePreviewTextCompositionStart(startArgs);
            if (!startArgs.Handled)
            {
                if (_window.FocusManager.FocusedElement is ITextCompositionClient client)
                {
                    client.HandleTextCompositionStart(startArgs);
                }
            }
        }

        _imeMarkedText = text;
        _imeState = ImeState.Preedit;
        var updateArgs = new TextCompositionEventArgs(text);
        _window.RaisePreviewTextCompositionUpdate(updateArgs);
        if (!updateArgs.Handled)
        {
            if (_window.FocusManager.FocusedElement is ITextCompositionClient client)
            {
                client.HandleTextCompositionUpdate(updateArgs);
            }
        }

        if (_window.FocusManager.FocusedElement is Controls.TextBase tb)
        {
            ImeLogger.Write($"  TextBase composingStart={tb.CompositionStartIndex} composingLen={tb.CompositionLength} caret={tb.CaretPosition} textLen={tb.TextLengthInternal}");
            try
            {
                int textLen = tb.TextLengthInternal;
                int compStart = Math.Max(0, tb.CompositionStartIndex);
                int compLen = Math.Max(0, tb.CompositionLength);

                string compText = (compLen > 0 && compStart + compLen <= textLen)
                    ? tb.GetTextSubstringInternal(compStart, compLen)
                    : string.Empty;

                int tailLen = Math.Min(32, textLen);
                string tail = tailLen > 0 ? tb.GetTextSubstringInternal(textLen - tailLen, tailLen) : string.Empty;

                var (selStart, selEnd) = tb.SelectionRange;
                ImeLogger.Write($"    TextBase selection=({selStart},{selEnd}) compText='{Truncate(compText)}' tail='{Truncate(tail)}'");
            }
            catch
            {
            }
        }
    }

    internal void ImeUnmarkText()
    {
        ImeLogger.Write($"unmarkText imeHasMarked(before)={_imeHasMarkedText} markedLen={_imeMarkedText.Length} focused={_window.FocusManager.FocusedElement?.GetType().Name ?? "null"}");
        if (!_imeHasMarkedText)
        {
            return;
        }

        // unmarkText means "accept the current preedit as committed text".
        // Use CommitTextCompositionInternal (which records undo) instead of
        // EndTextCompositionInternal (which removes the text and loses it).
        if (_window.FocusManager.FocusedElement is Controls.TextBase tb && tb.IsComposing)
        {
            var endArgs = new TextCompositionEventArgs(_imeMarkedText);
            _window.RaisePreviewTextCompositionEnd(endArgs);
            if (!endArgs.Handled)
            {
                tb.CommitTextCompositionInternal();
            }
        }
        else
        {
            var endArgs = new TextCompositionEventArgs(_imeMarkedText);
            _window.RaisePreviewTextCompositionEnd(endArgs);
            if (!endArgs.Handled)
            {
                if (_window.FocusManager.FocusedElement is ITextCompositionClient client)
                {
                    client.HandleTextCompositionEnd(endArgs);
                }
            }
        }

        _imeHasMarkedText = false;
        _imeMarkedText = string.Empty;
        _imeState = ImeState.Ground;
    }

    internal void ImeInsertText(string? text)
        => ImeInsertText(text, new NSRange(NSNotFound, 0));

    internal void ImeInsertText(string? text, NSRange replacementRange)
    {
        text ??= string.Empty;
        ImeLogger.Write($"insertText len={text.Length} text='{Truncate(text)}' repl=({replacementRange.location},{replacementRange.length}) imeHasMarked={_imeHasMarkedText} focused={_window.FocusManager.FocusedElement?.GetType().Name ?? "null"}");
        if (text.Length == 0)
        {
            return;
        }

        // IME commit: AppKit typically calls insertText while we still have marked text (setMarkedText path).
        if (_imeHasMarkedText && _window.FocusManager.FocusedElement is Controls.TextBase tb)
        {
            if (!string.Equals(text, _imeMarkedText, StringComparison.Ordinal))
            {
                ImeSetMarkedText(text);
            }

            var endArgs = new TextCompositionEventArgs(text);
            _window.RaisePreviewTextCompositionEnd(endArgs);
            if (!endArgs.Handled)
            {
                tb.CommitTextCompositionInternal();
            }

            _imeHasMarkedText = false;
            _imeMarkedText = string.Empty;
            _imeState = ImeState.Committed;
            ImeLogger.Write("  insertText handled as IME commit (no TextInput emitted).");
            return;
        }

        // If the platform provides a replacement range, align our selection/caret so the inserted text
        // replaces the intended portion of the document.
        if (replacementRange.location != NSNotFound && _window.FocusManager.FocusedElement is Controls.TextBase tbReplace)
        {
            int start = (int)replacementRange.location;
            int end = start + (int)replacementRange.length;
            tbReplace.SetSelectionRangeForPlatform(start, end);
        }

        // Cocoa routes plain text input through insertText during keyDown handling.
        if (_isHandlingKeyDown && !_imeHasMarkedText && _imeState == ImeState.Ground)
        {
            var normalized = TextInputEventArgs.NormalizeText(text);
            if (normalized is "\t" or "\n")
            {
                _pendingKeyDownTextInput = normalized;
                ImeLogger.Write($"  insertText buffered for post-KeyDown dispatch. pending='{Truncate(normalized)}'");
                return;
            }
        }

        // Filter out non-text control characters.
        bool hasPrintable = false;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (!char.IsControl(c) || c == '\n' || c == '\r' || c == '\t')
            {
                hasPrintable = true;
                break;
            }
        }

        if (!hasPrintable)
        {
            return;
        }

        var textArgs = new TextInputEventArgs(text);
        _window.RaisePreviewTextInput(textArgs);
        if (!textArgs.Handled)
        {
            if (_window.FocusManager.FocusedElement is ITextInputClient client)
            {
                client.HandleTextInput(textArgs);
            }
        }
        ImeLogger.Write($"  insertText emitted TextInput handled={textArgs.Handled}");
    }

    internal void ImeDoCommandBySelector(nint selector)
    {
        // winit:
        // - if the text was just committed, ignore the command selector to avoid double-send (notably Enter/Space).
        // - otherwise forward key to app for this keyDown.
        if (_imeState == ImeState.Committed)
        {
            return;
        }

        _forwardKeyToAppThisKeyDown = true;

        // If we are in preedit (setMarkedText path), commit the current composition so the TextBase
        // undo stack stays consistent, then reset IME state for key-up reporting.
        if (_imeHasMarkedText && _imeState == ImeState.Preedit)
        {
            if (_window.FocusManager.FocusedElement is Controls.TextBase tb && tb.IsComposing)
            {
                tb.CommitTextCompositionInternal();
            }
            _imeHasMarkedText = false;
            _imeMarkedText = string.Empty;
            _imeState = ImeState.Ground;
        }
    }

    private static MouseButton MapOtherMouseButton(nint ev)
    {
        int n = MacOSInterop.GetEventButtonNumber(ev);
        return n switch
        {
            0 => MouseButton.Left,
            1 => MouseButton.Right,
            2 => MouseButton.Middle,
            3 => MouseButton.XButton1,
            4 => MouseButton.XButton2,
            _ => MouseButton.Middle
        };
    }

    private static ModifierKeys GetModifierKeys(nint ev)
    {
        // NSEventModifierFlags:
        // Shift (1<<17), Control (1<<18), Option/Alt (1<<19), Command (1<<20).
        ulong flags = MacOSInterop.GetEventModifierFlags(ev);
        var m = ModifierKeys.None;
        if ((flags & (1ul << 17)) != 0)
        {
            m |= ModifierKeys.Shift;
        }

        if ((flags & (1ul << 18)) != 0)
        {
            m |= ModifierKeys.Control;
        }

        if ((flags & (1ul << 19)) != 0)
        {
            m |= ModifierKeys.Alt;
        }

        if ((flags & (1ul << 20)) != 0)
        {
            m |= ModifierKeys.Meta;
        }

        return m;
    }

    private static Key MapKey(nint ev, int keyCode)
    {
        // Map physical macOS virtual key codes first so shortcuts (Ctrl/Cmd+Z/C/V/...) work even when
        // NSEvent characters are suppressed/modified by modifiers.
        return keyCode switch
        {
            // Letters (kVK_ANSI_*)
            0x00 => Key.A,
            0x01 => Key.S,
            0x02 => Key.D,
            0x03 => Key.F,
            0x04 => Key.H,
            0x05 => Key.G,
            0x06 => Key.Z,
            0x07 => Key.X,
            0x08 => Key.C,
            0x09 => Key.V,
            0x0B => Key.B,
            0x0C => Key.Q,
            0x0D => Key.W,
            0x0E => Key.E,
            0x0F => Key.R,
            0x10 => Key.Y,
            0x11 => Key.T,
            0x12 => Key.D1,
            0x13 => Key.D2,
            0x14 => Key.D3,
            0x15 => Key.D4,
            0x16 => Key.D6,
            0x17 => Key.D5,
            0x19 => Key.D9,
            0x1A => Key.D7,
            0x1C => Key.D8,
            0x1D => Key.D0,
            0x1F => Key.O,
            0x20 => Key.U,
            0x22 => Key.I,
            0x23 => Key.P,
            0x25 => Key.L,
            0x26 => Key.J,
            0x28 => Key.K,
            0x2D => Key.N,
            0x2E => Key.M,

            0x24 => Key.Enter,      // kVK_Return
            0x30 => Key.Tab,        // kVK_Tab
            0x31 => Key.Space,      // kVK_Space
            0x33 => Key.Backspace,  // kVK_Delete (backspace)
            0x35 => Key.Escape,     // kVK_Escape
            0x75 => Key.Delete,     // kVK_ForwardDelete

            0x7B => Key.Left,
            0x7C => Key.Right,
            0x7D => Key.Down,
            0x7E => Key.Up,

            0x73 => Key.Home,
            0x77 => Key.End,
            0x74 => Key.PageUp,
            0x79 => Key.PageDown,

            // Function keys (kVK_F1..kVK_F12). Apple key codes are non-contiguous.
            0x7A => Key.F1,
            0x78 => Key.F2,
            0x63 => Key.F3,
            0x76 => Key.F4,
            0x60 => Key.F5,
            0x61 => Key.F6,
            0x62 => Key.F7,
            0x64 => Key.F8,
            0x65 => Key.F9,
            0x6D => Key.F10,
            0x67 => Key.F11,
            0x6F => Key.F12,
            _ => MapKeyFromCharacters(ev)
        };
    }

    private static Key MapKeyFromCharacters(nint ev)
    {
        var text = MacOSInterop.GetEventCharactersIgnoringModifiers(ev);
        if (string.IsNullOrEmpty(text) || text.Length != 1)
        {
            return Key.None;
        }

        char c = text[0];
        if (c >= '0' && c <= '9')
        {
            return (Key)((int)Key.D0 + (c - '0'));
        }

        if (c >= 'a' && c <= 'z')
        {
            c = (char)(c - 32);
        }

        if (c >= 'A' && c <= 'Z')
        {
            return (Key)((int)Key.A + (c - 'A'));
        }

        return Key.None;
    }

    internal bool NeedsRender
    {
        get => Volatile.Read(ref _needsRender) != 0;
    }

    internal void RenderIfNeeded()
    {
        if (Interlocked.Exchange(ref _needsRender, 0) == 0)
        {
            return;
        }

        RenderNow();
    }

    internal void RenderNow()
    {
        if (_nsView == 0)
        {
            return;
        }

        UpdateDpiIfNeeded();
        // If we're rendering right now, avoid scheduling another render from size-change invalidations.
        UpdateClientSizeIfNeeded(requestRender: false);
        UpdateMetalLayerDisplaySyncIfNeeded();
        UpdateMetalLayerPresentsWithTransactionIfNeeded();

        if (_metalLayer != 0)
        {
            // For CAMetalLayer-backed views, render from AppKit's display cycle (displayLayer:).
            // During live-resize, forcing a synchronous display can race with the resize transaction and reintroduce
            // "scaled cached frame" artifacts. Mark the view as needing display and let AppKit drive displayLayer:.
            if (MacOSWindowInterop.IsViewInLiveResize(_nsView))
            {
                // IMPORTANT: Mark the CAMetalLayer (not the NSView) so AppKit will call displayLayer:
                // on our layer delegate. Marking the view alone may not trigger displayLayer:.
                MacOSWindowInterop.SetLayerNeedsDisplay(_metalLayer);
            }
            else
            {
                // IMPORTANT: Force a synchronous layer display so input-driven invalidations are visible
                // immediately (mouse over/scroll/animations). Displaying the NSView does not reliably
                // invoke the CAMetalLayer delegate.
                MacOSWindowInterop.DisplayLayerIfNeeded(_metalLayer);
            }
        }
    }

    private MacOSMetalSurface CreateMetalSurface()
    {
        var clientSize = _window.ClientSize;
        int pixelWidth = (int)Math.Max(1, Math.Ceiling(clientSize.Width * _window.DpiScale));
        int pixelHeight = (int)Math.Max(1, Math.Ceiling(clientSize.Height * _window.DpiScale));
        var screen = MacOSWindowInterop.GetWindowScreen(_nsWindow);
        return new MacOSMetalSurface(_nsView, _metalLayer, screen, pixelWidth, pixelHeight, _window.DpiScale);
    }

    internal void RenderFromMetalLayerDisplay(nint layer)
    {
        if (_nsView == 0 || _metalLayer == 0 || layer != _metalLayer)
        {
            return;
        }

        UpdateMetalLayerDisplaySyncIfNeeded();
        UpdateMetalLayerPresentsWithTransactionIfNeeded();

        // Avoid re-entrant displayLayer-triggered renders.
        if (Interlocked.Exchange(ref _reshapeRendering, 1) != 0)
        {
            return;
        }

        try
        {
            UpdateDpiIfNeeded();
            // Force layout to align with live-resize updates, but do not schedule another render.
            UpdateClientSizeIfNeeded(forceLayout: true, requestRender: false);
            _window.RenderFrame(CreateMetalSurface());
        }
        finally
        {
            Volatile.Write(ref _reshapeRendering, 0);
        }
    }

    private sealed class MacOSMetalSurface : IMacOSMetalWindowSurface
    {
        public nint Handle => MetalLayer;

        public int PixelWidth { get; }

        public int PixelHeight { get; }

        public double DpiScale { get; }

        // NSScreen* identifies the display on macOS. Both NativeHandle and IdLow carry the
        // pointer value so structural equality holds across copies; Core type stays platform-
        // neutral and the platform translation lives here.
        public PlatformDisplayIdentity DisplayIdentity =>
            Screen == 0 ? default : new PlatformDisplayIdentity((ulong)Screen, 0, Screen);

        public nint View { get; }

        public nint MetalLayer { get; }

        public nint Screen { get; }

        public MacOSMetalSurface(nint view, nint metalLayer, nint screen, int pixelWidth, int pixelHeight, double dpiScale)
        {
            View = view;
            MetalLayer = metalLayer;
            Screen = screen;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            DpiScale = dpiScale <= 0 ? 1.0 : dpiScale;
        }
    }
}
