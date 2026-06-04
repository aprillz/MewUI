using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering.Framebuffer;

namespace Aprillz.MewUI.Platform.Framebuffer;

internal sealed class FramebufferInputPump : IDisposable
{
    private readonly FramebufferWindowBackend _window;
    private readonly FramebufferOptions _options;
    private readonly Thread _thread;
    private readonly CancellationTokenSource _cts = new();
    private TslibTouchDevice? _tslibDevice;
    private EvdevTouchDevice? _device;
    private TouchState _state;
    private readonly object _touchDispatchGate = new();
    private readonly List<TouchDispatch> _pendingTouches = new();
    private bool _touchDispatchScheduled;
    private bool _lastDispatchedTouchDown;
    private long _lastTouchMoveDispatchMs;
    private bool _disposed;

    public FramebufferInputPump(FramebufferWindowBackend window, FramebufferOptions options)
    {
        _window = window;
        _options = options;
        _thread = new Thread(ReadLoop, maxStackSize: 128 * 1024)
        {
            IsBackground = true,
            Name = "MewUI framebuffer touch input",
        };
        _thread.Start();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        _thread.Join(TimeSpan.FromSeconds(1));
        _cts.Dispose();
        _tslibDevice?.Dispose();
        _tslibDevice = null;
        _device?.Dispose();
        _device = null;
    }

    private void ReadLoop()
    {
        try
        {
            if (TryRunTslibLoop())
            {
                return;
            }

            _device = EvdevTouchDevice.TryOpen(_options);
            if (_device is null)
            {
                Console.WriteLine("[MewUI.Framebuffer] Touch input disabled: no evdev touch device found.");
                return;
            }

            Console.WriteLine($"[MewUI.Framebuffer] Touch input: {_device.Path} ({_device.Name}) X={_device.XRange.Minimum}..{_device.XRange.Maximum} Y={_device.YRange.Minimum}..{_device.YRange.Maximum}");

            Span<EvdevTouchDevice.InputEvent> events = stackalloc EvdevTouchDevice.InputEvent[32];
            _ = _device.Read(events);

            while (!_cts.IsCancellationRequested)
            {
                if (_device.Poll(100) <= 0)
                {
                    continue;
                }

                int count = _device.Read(events);
                for (int i = 0; i < count; i++)
                {
                    if (_options.LogTouchInput)
                    {
                        Console.WriteLine($"[MewUI.Framebuffer] evdev type=0x{events[i].type:X2} code=0x{events[i].code:X3} value={events[i].value}");
                    }

                    ProcessEvent(events[i]);
                }
            }
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[MewUI.Framebuffer] Touch input stopped: {ex.Message}");
        }
    }

    private bool TryRunTslibLoop()
    {
        if (!_options.UseTslibTouchInput)
        {
            if (_options.LogTouchInput)
            {
                Console.WriteLine("[MewUI.Framebuffer] tslib disabled; using evdev.");
            }

            return false;
        }

        _tslibDevice = TslibTouchDevice.TryOpen(_options);
        if (_tslibDevice is null)
        {
            if (_options.LogTouchInput)
            {
                Console.WriteLine("[MewUI.Framebuffer] tslib unavailable; falling back to evdev.");
            }

            return false;
        }

        Console.WriteLine($"[MewUI.Framebuffer] Touch input: tslib {_tslibDevice.Path}");

        Span<TslibTouchDevice.TouchSample> samples = stackalloc TslibTouchDevice.TouchSample[16];
        while (!_cts.IsCancellationRequested)
        {
            if (_tslibDevice.Poll(100) <= 0)
            {
                continue;
            }

            int count = _tslibDevice.Read(samples);
            for (int i = 0; i < count; i++)
            {
                ProcessTslibSample(samples[i]);
            }
        }

        return true;
    }

    private void ProcessTslibSample(TslibTouchDevice.TouchSample sample)
    {
        var point = MapTslibToWindow(sample.x, sample.y);
        bool touching = sample.pressure > 0;

        if (_options.LogTouchInput)
        {
            Console.WriteLine($"[MewUI.Framebuffer] tslib sample raw=({sample.x},{sample.y}) p={sample.pressure} mapped=({point.X:0.0},{point.Y:0.0}) down={touching}");
        }

        DispatchTouch(point, touching);
    }

    private void ProcessEvent(EvdevTouchDevice.InputEvent ev)
    {
        if (ev.IsMtSlot)
        {
            _state.CurrentSlot = ev.value;
            return;
        }

        if (ev.IsMtTrackingId)
        {
            if (_state.CurrentSlot == 0)
            {
                _state.Touching = ev.value >= 0;
                _state.ContactKnown = true;
            }
            return;
        }

        if (ev.IsTouchButton)
        {
            _state.Touching = ev.value != 0;
            _state.ContactKnown = true;
            return;
        }

        if (ev.IsMtX || ev.IsAbsX)
        {
            if (!ev.IsMtX || _state.CurrentSlot == 0)
            {
                _state.RawX = ev.value;
                _state.PositionKnown = _state.RawY.HasValue;
            }
            return;
        }

        if (ev.IsMtY || ev.IsAbsY)
        {
            if (!ev.IsMtY || _state.CurrentSlot == 0)
            {
                _state.RawY = ev.value;
                _state.PositionKnown = _state.RawX.HasValue;
            }
            return;
        }

        if (!ev.IsSynReport || !_state.PositionKnown)
        {
            return;
        }

        bool touching = _state.ContactKnown ? _state.Touching : _state.Touching || _state.RawX.HasValue;
        var point = MapToWindow(_state.RawX!.Value, _state.RawY!.Value);
        if (_options.LogTouchInput)
        {
            Console.WriteLine($"[MewUI.Framebuffer] touch raw=({_state.RawX.Value},{_state.RawY.Value}) mapped=({point.X:0.0},{point.Y:0.0}) down={touching}");
        }

        DispatchTouch(point, touching);
    }

    private Point MapToWindow(int rawX, int rawY)
    {
        var device = _device ?? throw new ObjectDisposedException(nameof(EvdevTouchDevice));

        double x = Normalize(rawX, device.XRange);
        double y = Normalize(rawY, device.YRange);

        if (_options.SwapTouchAxes)
        {
            (x, y) = (y, x);
        }

        if (_options.InvertTouchX)
        {
            x = 1.0 - x;
        }

        if (_options.InvertTouchY)
        {
            y = 1.0 - y;
        }

        var size = _window.ClientSizeDip;
        return new Point(
            Math.Clamp(x, 0.0, 1.0) * Math.Max(1, size.Width),
            Math.Clamp(y, 0.0, 1.0) * Math.Max(1, size.Height));
    }

    private Point MapTslibToWindow(int rawX, int rawY)
    {
        double x = rawX;
        double y = rawY;

        if (_options.SwapTouchAxes)
        {
            (x, y) = (y, x);
        }

        var size = _window.ClientSizeDip;
        if (_options.InvertTouchX)
        {
            x = size.Width - x;
        }

        if (_options.InvertTouchY)
        {
            y = size.Height - y;
        }

        return new Point(
            Math.Clamp(x, 0, Math.Max(1, size.Width)),
            Math.Clamp(y, 0, Math.Max(1, size.Height)));
    }

    private void DispatchTouch(Point point, bool touching)
    {
        if (_disposed)
        {
            return;
        }

        if (touching == _lastDispatchedTouchDown)
        {
            if (!touching || _options.TouchClickOnly)
            {
                return;
            }

            int throttleMs = _options.TouchMoveThrottleMs;
            if (throttleMs > 0)
            {
                long now = Environment.TickCount64;
                if (now - _lastTouchMoveDispatchMs < throttleMs)
                {
                    return;
                }

                _lastTouchMoveDispatchMs = now;
            }
        }
        else
        {
            _lastDispatchedTouchDown = touching;
            if (touching)
            {
                _lastTouchMoveDispatchMs = Environment.TickCount64;
            }
        }

        if (_options.LogTouchInput)
        {
            Console.WriteLine($"[MewUI.Framebuffer] enqueue touch ({point.X:0.0},{point.Y:0.0}) down={touching}");
        }

        bool scheduleDispatch = false;
        lock (_touchDispatchGate)
        {
            if (_disposed)
            {
                return;
            }

            var next = new TouchDispatch(point, touching);
            if (_pendingTouches.Count > 0 && _pendingTouches[^1].IsDown == touching)
            {
                _pendingTouches[^1] = next;
            }
            else
            {
                _pendingTouches.Add(next);
            }

            if (!_touchDispatchScheduled)
            {
                _touchDispatchScheduled = true;
                scheduleDispatch = true;
            }
        }

        if (scheduleDispatch)
        {
            _window.DispatchInput(ProcessTouchQueue);
        }
    }

    private void ProcessTouchQueue()
    {
        while (true)
        {
            TouchDispatch touch;
            lock (_touchDispatchGate)
            {
                if (_disposed || _pendingTouches.Count == 0)
                {
                    _touchDispatchScheduled = false;
                    return;
                }

                touch = _pendingTouches[0];
                _pendingTouches.RemoveAt(0);
            }

            if (_options.LogTouchInput)
            {
                Console.WriteLine($"[MewUI.Framebuffer] dispatch touch ({touch.Point.X:0.0},{touch.Point.Y:0.0}) down={touch.IsDown}");
            }

            _window.RouteTouch(touch.Point, touch.IsDown);
        }
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

    private struct TouchState
    {
        public int CurrentSlot;
        public int? RawX;
        public int? RawY;
        public bool PositionKnown;
        public bool ContactKnown;
        public bool Touching;
    }

    private readonly record struct TouchDispatch(Point Point, bool IsDown);
}
