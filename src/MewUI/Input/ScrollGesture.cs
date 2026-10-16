using System.Diagnostics;

using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Input;

/// <summary>
/// Turns finger movement into scrolling for one window, and lets a released finger coast.
/// </summary>
/// <remarks>
/// Movement is delivered as wheel input in notches worth <see cref="ThemeMetrics.ScrollWheelStep"/>,
/// so content tracks the finger one to one. Timestamps are <see cref="Stopwatch"/> ticks.
/// </remarks>
internal sealed class ScrollGesture
{
    // Coasting speed decays as v0 * DECAY^seconds. Total travel is v0 / -ln(DECAY), so squaring the
    // 0.15 that Avalonia's ScrollGestureRecognizer and UIScrollView use halves the distance while the
    // coast still leaves the finger at the speed the finger had.
    internal const double COAST_DECAY_PER_SECOND = 0.0225;
    internal const double COAST_MAX_SPEED_DIP = 4000;
    internal const double SAMPLE_WINDOW_SECONDS = 0.1;
    private const int SAMPLE_CAPACITY = 8;

    // A movement this much larger on one axis than the other scrolls along that axis only. Without the
    // lock a mostly vertical drag banks its small sideways part and releases it a device pixel at a
    // time, which reads as the content shivering left and right.
    private const double AXIS_LOCK_RATIO = 2;

    private const double FALLBACK_FRAME_INTERVAL_MS = 1000.0 / 60;
    private const int FRAME_INTERVAL_SAMPLES = 9;

    // Longer gaps are a loop waking from sleep, not the display's rhythm.
    private const double MAX_FRAME_INTERVAL_MS = 50;

    private readonly record struct PanSample(double DeltaX, double DeltaY, double Seconds);

    private readonly Window _window;
    private readonly PanSample[] _samples = new PanSample[SAMPLE_CAPACITY];
    private int _sampleCount;
    private int _sampleNext;
    private long _sampleTimestamp = long.MinValue;

    private readonly double[] _frameIntervals = new double[FRAME_INTERVAL_SAMPLES];
    private readonly double[] _frameIntervalScratch = new double[FRAME_INTERVAL_SAMPLES];
    private int _frameIntervalCount;
    private int _frameIntervalNext;
    private long _lastTickTimestamp = long.MinValue;

    private Point _point;
    private Point _screenPoint;
    private ModifierKeys _modifiers;

    // Element that scrolled for this gesture, kept so the gesture cannot change owner.
    private UIElement? _target;
    private double _bankX;
    private double _bankY;
    private bool _lockX;
    private bool _lockY;

    private AnimationClock? _clock;
    private bool _coasting;
    private double _coastSpeed;
    private double _coastDirectionX;
    private double _coastDirectionY;
    private double _coastSentDistance;
    private long _coastStart;
    private bool _advancing;

    internal ScrollGesture(Window window) => _window = window;

    /// <summary>True while a released pan is still moving the content.</summary>
    internal bool IsCoasting => _coasting;

    /// <summary>
    /// One device pixel, the smallest movement that can change what is on screen. Movement under it is
    /// banked rather than sent, so a slow drag or a high refresh rate does not lose it a little at a time.
    /// </summary>
    private double MinStepDip => 96.0 / Math.Max(1, _window.Dpi);

    private double CoastEndSpeedDip => MinStepDip * 1000.0 / FrameIntervalMs;

    /// <summary>
    /// Starts a pan whose routing point is <paramref name="point"/>, in window surface coordinates.
    /// <paramref name="travel"/> is the movement that made the press a pan, which decides the axis.
    /// </summary>
    internal void Begin(Point point, Point screenPoint, Vector travel, ModifierKeys modifiers)
    {
        Stop();
        _point = point;
        _screenPoint = screenPoint;
        _modifiers = modifiers;
        _target = null;
        _bankX = 0;
        _bankY = 0;
        ClearSamples();

        double absX = Math.Abs(travel.X);
        double absY = Math.Abs(travel.Y);
        _lockX = absY >= absX * AXIS_LOCK_RATIO;
        _lockY = absX >= absY * AXIS_LOCK_RATIO && !_lockX;
        _window.SetScrollGestureActive(true);
    }

    /// <summary>Scrolls by one finger movement in DIPs.</summary>
    internal void Pan(double deltaX, double deltaY, long timestamp)
    {
        if (_lockX)
        {
            deltaX = 0;
        }

        if (_lockY)
        {
            deltaY = 0;
        }

        RecordSample(deltaX, deltaY, timestamp);
        Send(deltaX, deltaY);
    }

    /// <summary>Ends the pan at <paramref name="timestamp"/> and coasts from the speed the finger left it with.</summary>
    internal void Release(long timestamp)
    {
        bool estimated = TryEstimateVelocity(timestamp, out double velocityX, out double velocityY);
        ClearSamples();
        double speed = Math.Sqrt((velocityX * velocityX) + (velocityY * velocityY));
        if (!estimated || speed < CoastEndSpeedDip)
        {
            _window.SetScrollGestureActive(false);
            return;
        }

        _coasting = true;
        _coastSpeed = Math.Min(speed, COAST_MAX_SPEED_DIP);
        _coastDirectionX = velocityX / speed;
        _coastDirectionY = velocityY / speed;
        _coastSentDistance = 0;

        // The curve starts at the release itself, so the first frame already moves. Stamping the first
        // frame instead leaves the content parked for a frame or two after the finger lifts.
        _coastStart = timestamp;
        _lastTickTimestamp = long.MinValue;
        _clock ??= CreateClock();
        _clock.Start();
    }

    /// <summary>Stops a pan or a coast and forgets its target.</summary>
    internal void Stop()
    {
        _clock?.Stop();
        _coasting = false;
        _coastSentDistance = 0;
        _target = null;
        _bankX = 0;
        _bankY = 0;
        ClearSamples();
        _window.SetScrollGestureActive(false);
    }

    private AnimationClock CreateClock()
    {
        // The duration only paces the clock's own bookkeeping: the coast reads the pulse time and ends
        // itself, so the clock repeats until stopped.
        var clock = new AnimationClock(TimeSpan.FromSeconds(1)) { RepeatCount = -1 };
        clock.TickCallback = _ => OnTick();
        clock.AttachTo(_window);
        return clock;
    }

    private void OnTick()
    {
        // Driving the scroll runs application code, and a pulse started from inside it would read the
        // distance already sent before this call finishes writing it.
        if (!_coasting || _advancing)
        {
            return;
        }

        _advancing = true;
        try
        {
            if (!Advance(AnimationManager.Instance.PulseTimestamp))
            {
                Stop();
            }
        }
        catch
        {
            // A throw out of the scroll would otherwise repeat every frame for the rest of the coast.
            Stop();
            throw;
        }
        finally
        {
            _advancing = false;
        }
    }

    /// <summary>Moves the coast on to <paramref name="timestamp"/>; false means it is over.</summary>
    private bool Advance(long timestamp)
    {
        if (_window.Handle == 0)
        {
            return false;
        }

        NoteFrame(timestamp);

        double elapsed = Math.Max(0, Stopwatch.GetElapsedTime(_coastStart, timestamp).TotalSeconds);
        double remaining = Math.Pow(COAST_DECAY_PER_SECOND, elapsed);
        if (_coastSpeed * remaining < CoastEndSpeedDip)
        {
            return false;
        }

        // The integral of the decay curve, so a long frame and two short ones cover the same ground and
        // a dropped frame loses no movement.
        double travelled = _coastSpeed * (remaining - 1) / Math.Log(COAST_DECAY_PER_SECOND);
        double step = travelled - _coastSentDistance;
        _coastSentDistance = travelled;
        if (step <= 0)
        {
            return true;
        }

        // Nothing scrolled means the content hit its end and there is nowhere left to coast.
        return Send(_coastDirectionX * step, _coastDirectionY * step);
    }

    /// <summary>Scrolls by a finger movement; false means nothing took it and there is no room left.</summary>
    private bool Send(double deltaX, double deltaY)
    {
        double step = _window.ThemeInternal.Metrics.ScrollWheelStep;
        if (step <= 0)
        {
            return false;
        }

        // Scrolling ignores a step worth less than half a DIP, so movement is banked until it is worth
        // forwarding, and only the part actually sent is taken out of the bank.
        _bankX += deltaX;
        _bankY += deltaY;
        double sendX = Math.Abs(_bankX) >= MinStepDip ? _bankX : 0;
        double sendY = Math.Abs(_bankY) >= MinStepDip ? _bankY : 0;
        if (sendX == 0 && sendY == 0)
        {
            // Banked, not refused: the movement is still owed and the next call carries it.
            return true;
        }

        _bankX -= sendX;
        _bankY -= sendY;

        // A notch is worth ScrollWheelStep DIPs and notches may be fractional, so dividing the finger
        // delta by the step makes the content track the finger one to one.
        var delta = new Vector(sendX / step, sendY / step);

        // The gesture stays with whatever first scrolled for it. Hit-testing the start point again every
        // move would hand the gesture to any other scrollable the scrolling brought under it. A pinned
        // target that stops handling (out of room, or virtualized away) falls back to the point, which is
        // also how the gesture reaches an outer scrollable.
        var handler = WindowInputRouter.MouseWheel(
            _window, _point, _screenPoint, delta, false, false, false, _modifiers, _target);
        if (handler == null && _target != null)
        {
            handler = WindowInputRouter.MouseWheel(
                _window, _point, _screenPoint, delta, false, false, false, _modifiers, routeFrom: null);
        }

        if (handler != null)
        {
            _target = handler;
        }

        return handler != null;
    }

    private void RecordSample(double deltaX, double deltaY, long timestamp)
    {
        double seconds = _sampleTimestamp == long.MinValue
            ? 0
            : Math.Max(0, Stopwatch.GetElapsedTime(_sampleTimestamp, timestamp).TotalSeconds);
        _sampleTimestamp = timestamp;
        _samples[_sampleNext] = new PanSample(deltaX, deltaY, seconds);
        _sampleNext = (_sampleNext + 1) % SAMPLE_CAPACITY;
        if (_sampleCount < SAMPLE_CAPACITY)
        {
            _sampleCount++;
        }
    }

    private void ClearSamples()
    {
        _sampleCount = 0;
        _sampleNext = 0;
        _sampleTimestamp = long.MinValue;
    }

    private bool TryEstimateVelocity(long timestamp, out double velocityX, out double velocityY)
    {
        velocityX = 0;
        velocityY = 0;
        if (_sampleCount < 2)
        {
            return false;
        }

        // A finger that came to rest before lifting leaves nothing but stale samples. That is a hold, and
        // reading the earlier movement would throw content the user had already parked.
        if (Stopwatch.GetElapsedTime(_sampleTimestamp, timestamp).TotalSeconds > SAMPLE_WINDOW_SECONDS)
        {
            return false;
        }

        // A single last delta is too noisy on a touch screen to read a throw from, so the release averages
        // the trailing window.
        double sumX = 0;
        double sumY = 0;
        double seconds = 0;
        for (int count = 0; count < _sampleCount && seconds < SAMPLE_WINDOW_SECONDS; count++)
        {
            int index = ((_sampleNext - 1 - count) % SAMPLE_CAPACITY + SAMPLE_CAPACITY) % SAMPLE_CAPACITY;
            var sample = _samples[index];
            if (sample.Seconds <= 0)
            {
                break;
            }

            sumX += sample.DeltaX;
            sumY += sample.DeltaY;
            seconds += sample.Seconds;
        }

        if (seconds <= 0)
        {
            return false;
        }

        velocityX = sumX / seconds;
        velocityY = sumY / seconds;
        return true;
    }

    private void NoteFrame(long timestamp)
    {
        if (_lastTickTimestamp != long.MinValue)
        {
            double interval = Stopwatch.GetElapsedTime(_lastTickTimestamp, timestamp).TotalMilliseconds;
            if (interval > 0 && interval <= MAX_FRAME_INTERVAL_MS)
            {
                _frameIntervals[_frameIntervalNext] = interval;
                _frameIntervalNext = (_frameIntervalNext + 1) % FRAME_INTERVAL_SAMPLES;
                if (_frameIntervalCount < FRAME_INTERVAL_SAMPLES)
                {
                    _frameIntervalCount++;
                }
            }
        }

        _lastTickTimestamp = timestamp;
    }

    /// <summary>The median of the recent pulse intervals, so one long frame does not move the estimate.</summary>
    private double FrameIntervalMs
    {
        get
        {
            // Below one device pixel per frame the content moves every second or third frame instead of
            // every one, so the coast ends where that would begin; 120Hz halves the step a speed makes.
            if (_frameIntervalCount == 0)
            {
                return FALLBACK_FRAME_INTERVAL_MS;
            }

            Array.Copy(_frameIntervals, _frameIntervalScratch, _frameIntervalCount);
            Array.Sort(_frameIntervalScratch, 0, _frameIntervalCount);
            return _frameIntervalScratch[_frameIntervalCount / 2];
        }
    }
}
