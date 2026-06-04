using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Framebuffer;
using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Platform.Framebuffer;

internal sealed class FramebufferWindowBackend : IWindowBackend
{
    private readonly FramebufferPlatformHost _host;
    private readonly FramebufferGraphicsFactory _factory;
    private FramebufferRenderSurface? _surface;
    private bool _visible;
    private bool _disposed;
    private int _needsRender = 1;
    private double _widthDip;
    private double _heightDip;
    private bool _touchDown;
    private Point _lastTouchPoint;
    private readonly ClickCountTracker _clickCountTracker = new();
    private readonly int[] _lastPressClickCounts = new int[5];
    private FramebufferInputPump? _inputPump;
    private EvdevTouchDevice? _polledTouchDevice;
    private TouchState _polledTouchState;
    private bool _lastPolledTouchDown;
    private bool _tapTracking;
    private bool _tapCanceled;
    private Point _tapStartPoint;
    private Point _tapLastPoint;
    private long _lastPolledMoveDispatchMs;

    public FramebufferWindowBackend(FramebufferPlatformHost host, Window window)
    {
        _host = host;
        Window = window;
        _factory = Application.DefaultGraphicsFactory as FramebufferGraphicsFactory
            ?? throw new InvalidOperationException("FramebufferPlatform requires FramebufferBackend.");

        var fb = _factory.GetOrOpenFramebuffer();
        _widthDip = fb.PixelWidth / _factory.Options.DpiScale;
        _heightDip = fb.PixelHeight / _factory.Options.DpiScale;
    }

    public Window Window { get; }

    public bool NeedsRender => Volatile.Read(ref _needsRender) != 0 && _visible && !_disposed;

    public Size ClientSizeDip => new(_widthDip, _heightDip);

    public nint Handle => 1;

    public void SetResizable(bool resizable)
    {
    }

    public void Show()
    {
        var fb = _factory.GetOrOpenFramebuffer();
        _widthDip = fb.PixelWidth / _factory.Options.DpiScale;
        _heightDip = fb.PixelHeight / _factory.Options.DpiScale;
        Window.SetClientSizeDip(_widthDip, _heightDip);
        Window.RaiseClientSizeChanged(_widthDip, _heightDip);
        _surface?.Dispose();
        _surface = new FramebufferRenderSurface(
            fb.PixelWidth,
            fb.PixelHeight,
            _factory.Options.DpiScale,
            RenderPixelFormat.Bgra8888Premultiplied,
            SurfaceCapabilities.Premultiplied | SurfaceCapabilities.Alpha);
        _visible = true;
        if (_factory.Options.EnableTouchInput)
        {
            if (_factory.Options.PollTouchOnUiThread)
            {
                TryOpenPolledTouchDevice();
            }
            else
            {
                _inputPump ??= new FramebufferInputPump(this, _factory.Options);
            }
        }
        Invalidate(false);
    }

    public void Hide()
    {
        _visible = false;
    }

    public void Close()
    {
        if (!Window.RequestClose())
        {
            return;
        }

        Dispose();
        Window.RaiseClosed();
    }

    public void Invalidate(bool erase)
        => Volatile.Write(ref _needsRender, 1);

    public void RenderIfNeeded()
    {
        PollTouchInput();
        if (Interlocked.Exchange(ref _needsRender, 0) != 0)
        {
            RenderNow();
        }
    }

    public void RenderNow()
    {
        if (!_visible || _disposed)
        {
            return;
        }

        PollTouchInput();
        var surface = _surface ?? throw new InvalidOperationException("Framebuffer surface is not initialized.");
        Window.PerformLayout();
        Window.RenderFrameToSurface(surface);
        _factory.Present(surface);
    }

    public void SetTitle(string title)
    {
    }

    public void SetIcon(IconSource? icon)
    {
    }

    public void SetClientSize(double widthDip, double heightDip)
    {
        Window.SetClientSizeDip(_widthDip, _heightDip);
    }

    public Point GetPosition() => Point.Zero;

    public void SetPosition(double leftDip, double topDip)
    {
    }

    public void CaptureMouse()
    {
    }

    public void ReleaseMouseCapture()
    {
    }

    public Point ClientToScreen(Point clientPointDip) => clientPointDip;

    public Point ScreenToClient(Point screenPointPx) => screenPointPx;

    internal void DispatchInput(Action action)
    {
        if (_disposed)
        {
            return;
        }

        var dispatcher = Window.ApplicationDispatcher;
        if (dispatcher is null || dispatcher.IsOnUIThread)
        {
            action();
            return;
        }

        dispatcher.BeginInvoke(DispatcherPriority.Input, action);
    }

    internal void RouteTouch(Point positionDip, bool isDown)
    {
        if (!_visible || _disposed)
        {
            return;
        }

        positionDip = ClampToClient(positionDip);
        var screenPos = ClientToScreen(positionDip);
        if (_factory.Options.LogTouchInput)
        {
            Console.WriteLine($"[MewUI.Framebuffer] route touch ({positionDip.X:0.0},{positionDip.Y:0.0}) down={isDown} wasDown={_touchDown}");
        }

        if (isDown)
        {
            if (!_touchDown)
            {
                _touchDown = true;
                _lastTouchPoint = positionDip;
                int clickCount = _clickCountTracker.Update(
                    MouseButton.Left,
                    (int)Math.Round(positionDip.X * Window.DpiScale),
                    (int)Math.Round(positionDip.Y * Window.DpiScale),
                    unchecked((uint)Environment.TickCount),
                    maxDelayMs: 500,
                    maxDistX: (int)Math.Round(4 * Window.DpiScale),
                    maxDistY: (int)Math.Round(4 * Window.DpiScale));
                _lastPressClickCounts[(int)MouseButton.Left] = clickCount;

                WindowInputRouter.MouseButton(
                    Window,
                    positionDip,
                    screenPos,
                    MouseButton.Left,
                    isDown: true,
                    leftDown: true,
                    rightDown: false,
                    middleDown: false,
                    clickCount);
            }
            else
            {
                _lastTouchPoint = positionDip;
                WindowInputRouter.MouseMove(Window, positionDip, screenPos, leftDown: true, rightDown: false, middleDown: false);
            }

            Invalidate(false);
            return;
        }

        if (!_touchDown)
        {
            return;
        }

        _touchDown = false;
        _lastTouchPoint = positionDip;

        int releaseClickCount = _lastPressClickCounts[(int)MouseButton.Left];
        if (releaseClickCount <= 0)
        {
            releaseClickCount = 1;
        }

        WindowInputRouter.MouseButton(
            Window,
            positionDip,
            screenPos,
            MouseButton.Left,
            isDown: false,
            leftDown: false,
            rightDown: false,
            middleDown: false,
            clickCount: releaseClickCount);
        Invalidate(false);
    }

    public void EnsureTheme(bool isDark)
    {
    }

    public void CenterOnOwner()
    {
    }

    public void Activate()
    {
    }

    public void SetOwner(nint ownerHandle)
    {
    }

    public void SetEnabled(bool enabled)
    {
    }

    public void SetOpacity(double opacity)
    {
    }

    public void SetAllowsTransparency(bool allowsTransparency)
    {
    }

    public void SetCursor(CursorType cursorType)
    {
    }

    public void SetImeMode(Input.ImeMode mode)
    {
    }

    public void CancelImeComposition()
    {
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _visible = false;
        _inputPump?.Dispose();
        _inputPump = null;
        _polledTouchDevice?.Dispose();
        _polledTouchDevice = null;
        _surface?.Dispose();
        _surface = null;
        _host.Unregister(this);
    }

    internal void PollTouchInput()
    {
        if (!_factory.Options.PollTouchOnUiThread || _disposed || !_visible)
        {
            return;
        }

        var device = _polledTouchDevice;
        if (device is null)
        {
            return;
        }

        Span<EvdevTouchDevice.InputEvent> events = stackalloc EvdevTouchDevice.InputEvent[32];
        for (int batch = 0; batch < 8; batch++)
        {
            if (device.Poll(0) <= 0)
            {
                return;
            }

            int count = device.Read(events);
            for (int i = 0; i < count; i++)
            {
                ProcessPolledTouchEvent(device, events[i]);
            }
        }
    }

    private void TryOpenPolledTouchDevice()
    {
        if (_polledTouchDevice is not null)
        {
            return;
        }

        _polledTouchDevice = EvdevTouchDevice.TryOpen(_factory.Options);
        if (_polledTouchDevice is null)
        {
            Console.WriteLine("[MewUI.Framebuffer] Touch input disabled: no evdev touch device found.");
            return;
        }

        var device = _polledTouchDevice;
        Console.WriteLine($"[MewUI.Framebuffer] Touch input: ui-poll {device.Path} ({device.Name}) X={device.XRange.Minimum}..{device.XRange.Maximum} Y={device.YRange.Minimum}..{device.YRange.Maximum}");
    }

    private void ProcessPolledTouchEvent(EvdevTouchDevice device, EvdevTouchDevice.InputEvent ev)
    {
        if (ev.IsMtSlot)
        {
            _polledTouchState.CurrentSlot = ev.value;
            return;
        }

        if (ev.IsMtTrackingId)
        {
            if (_polledTouchState.CurrentSlot == 0)
            {
                _polledTouchState.Touching = ev.value >= 0;
                _polledTouchState.ContactKnown = true;
            }
            return;
        }

        if (ev.IsTouchButton)
        {
            _polledTouchState.Touching = ev.value != 0;
            _polledTouchState.ContactKnown = true;
            return;
        }

        if (ev.IsMtX || ev.IsAbsX)
        {
            if (!ev.IsMtX || _polledTouchState.CurrentSlot == 0)
            {
                _polledTouchState.RawX = ev.value;
                _polledTouchState.PositionKnown = _polledTouchState.RawY.HasValue;
            }
            return;
        }

        if (ev.IsMtY || ev.IsAbsY)
        {
            if (!ev.IsMtY || _polledTouchState.CurrentSlot == 0)
            {
                _polledTouchState.RawY = ev.value;
                _polledTouchState.PositionKnown = _polledTouchState.RawX.HasValue;
            }
            return;
        }

        if (!ev.IsSynReport || !_polledTouchState.PositionKnown)
        {
            return;
        }

        bool touching = _polledTouchState.ContactKnown ? _polledTouchState.Touching : _polledTouchState.Touching || _polledTouchState.RawX.HasValue;
        var point = MapToWindow(device, _polledTouchState.RawX!.Value, _polledTouchState.RawY!.Value);
        if (_factory.Options.TouchTapOnly)
        {
            ProcessPolledTap(point, touching);
            return;
        }

        if (touching == _lastPolledTouchDown)
        {
            if (!touching || _factory.Options.TouchClickOnly)
            {
                return;
            }

            int throttleMs = _factory.Options.TouchMoveThrottleMs;
            if (throttleMs > 0)
            {
                long now = Environment.TickCount64;
                if (now - _lastPolledMoveDispatchMs < throttleMs)
                {
                    return;
                }

                _lastPolledMoveDispatchMs = now;
            }
        }
        else
        {
            _lastPolledTouchDown = touching;
            if (touching)
            {
                _lastPolledMoveDispatchMs = Environment.TickCount64;
            }
        }

        RouteTouch(point, touching);
    }

    private void ProcessPolledTap(Point point, bool touching)
    {
        if (touching)
        {
            if (!_lastPolledTouchDown)
            {
                _tapTracking = true;
                _tapCanceled = false;
                _tapStartPoint = point;
            }
            else if (_tapTracking && !_tapCanceled && DistanceExceeded(_tapStartPoint, point, _factory.Options.TouchTapMaxMoveDip))
            {
                _tapCanceled = true;
            }

            _tapLastPoint = point;
            _lastPolledTouchDown = true;
            return;
        }

        if (!_lastPolledTouchDown)
        {
            return;
        }

        bool shouldTap = _tapTracking && !_tapCanceled && !DistanceExceeded(_tapStartPoint, point, _factory.Options.TouchTapMaxMoveDip);
        _lastPolledTouchDown = false;
        _tapTracking = false;

        if (!shouldTap)
        {
            return;
        }

        RouteTouch(_tapLastPoint, isDown: true);
        RouteTouch(_tapLastPoint, isDown: false);
    }

    private static bool DistanceExceeded(Point a, Point b, double limit)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return dx * dx + dy * dy > limit * limit;
    }

    private Point MapToWindow(EvdevTouchDevice device, int rawX, int rawY)
    {
        double x = Normalize(rawX, device.XRange);
        double y = Normalize(rawY, device.YRange);

        if (_factory.Options.SwapTouchAxes)
        {
            (x, y) = (y, x);
        }

        if (_factory.Options.InvertTouchX)
        {
            x = 1.0 - x;
        }

        if (_factory.Options.InvertTouchY)
        {
            y = 1.0 - y;
        }

        var size = ClientSizeDip;
        return new Point(
            Math.Clamp(x, 0.0, 1.0) * Math.Max(1, size.Width),
            Math.Clamp(y, 0.0, 1.0) * Math.Max(1, size.Height));
    }

    private static double Normalize(int value, EvdevTouchDevice.AxisRange range)
    {
        int span = range.Maximum - range.Minimum;
        if (span <= 0)
        {
            return 0;
        }

        return (value - range.Minimum) / (double)span;
    }

    private Point ClampToClient(Point point)
        => new(
            Math.Clamp(point.X, 0, Math.Max(1, _widthDip)),
            Math.Clamp(point.Y, 0, Math.Max(1, _heightDip)));

    private struct TouchState
    {
        public int CurrentSlot;
        public int? RawX;
        public int? RawY;
        public bool PositionKnown;
        public bool ContactKnown;
        public bool Touching;
    }
}
