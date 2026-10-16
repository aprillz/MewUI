using System.Diagnostics;

using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Input;

/// <summary>
/// Reads touch and pen contacts for one window and routes them as the pointer events controls
/// understand: a tap is a click, a drag past the threshold is a scroll, a hold is a right click.
/// </summary>
/// <remarks>
/// Only the first finger down drives a gesture. Later fingers are tracked but not routed: a single
/// pointer event model cannot express two presses at once without breaking capture and pressed state.
/// Timestamps are <see cref="Stopwatch"/> ticks and positions are window surface DIPs.
/// </remarks>
internal sealed class PointerGestureRecognizer
{
    internal const double PAN_THRESHOLD_DIP = 8;
    internal const double LONG_PRESS_MS = 500;

    // A finger is granted more room than a mouse to chain taps into a double tap.
    private const uint MULTI_TAP_WINDOW_MS = 500;
    private const int MULTI_TAP_SLOP_TOUCH_DIP = 24;
    private const int MULTI_TAP_SLOP_PEN_DIP = 4;

    private readonly Window _window;
    private readonly ScrollGesture _scroll;
    private readonly ClickCountTracker _taps = new();
    private readonly ClickCountTracker _penClicks = new();
    private readonly List<int> _secondary = new();
    private DispatcherTimer? _longPressTimer;

    private int _primaryId = -1;

    // The press was spent stopping a coast, so nothing of its contact reaches a control.
    private bool _swallowPrimary;

    // Whether the primary contact may still become a pan or a hold. False once something captured to
    // consume the movement itself (a slider, a scroll bar, text selection).
    private bool _tracking;
    private bool _panning;
    private bool _longPressed;
    private int _clickCount;
    private Point _start;
    private Point _startScreen;
    private Point _last;
    private long _longPressDeadline;

    private int _penId = -1;
    private int _penClickCount;

    internal PointerGestureRecognizer(Window window)
    {
        _window = window;
        _scroll = new ScrollGesture(window);
    }

    /// <summary>True while a released pan is still moving the content.</summary>
    internal bool IsCoasting => _scroll.IsCoasting;

    internal void Down(int pointerId, PointerType pointerType, Point position, Point screenPosition, long timestamp, ModifierKeys modifiers)
    {
        if (pointerType == PointerType.Pen)
        {
            PenDown(pointerId, position, screenPosition, timestamp, modifiers);
            return;
        }

        if (_primaryId == pointerId)
        {
            // A second press for a contact still down means its release never arrived.
            Cancel(pointerId);
        }

        if (_primaryId != -1)
        {
            if (!_secondary.Contains(pointerId))
            {
                _secondary.Add(pointerId);
            }

            // A second finger means the first one is not holding still to ask for a menu.
            StopLongPress();
            _longPressDeadline = long.MaxValue;
            return;
        }

        _primaryId = pointerId;
        _tracking = false;
        _panning = false;
        _longPressed = false;

        if (_scroll.IsCoasting)
        {
            // A press during a coast is spent stopping it. Letting the same tap through would also
            // activate whatever it landed on, which no touch platform does.
            _scroll.Stop();
            _swallowPrimary = true;
            _taps.Reset();
            return;
        }

        _swallowPrimary = false;
        _clickCount = _taps.Update(
            MouseButton.Left,
            (int)Math.Round(position.X),
            (int)Math.Round(position.Y),
            ToMilliseconds(timestamp),
            MULTI_TAP_WINDOW_MS,
            MULTI_TAP_SLOP_TOUCH_DIP,
            MULTI_TAP_SLOP_TOUCH_DIP);

        WindowInputRouter.MouseButton(_window, position, screenPosition, MouseButton.Left, isDown: true,
            leftDown: true, rightDown: false, middleDown: false, _clickCount, modifiers, PointerType.Touch);

        if (_primaryId != pointerId || CaptureConsumesDrag())
        {
            return;
        }

        _tracking = true;
        _start = position;
        _startScreen = screenPosition;
        _last = position;
        _longPressDeadline = timestamp + MillisecondsToTicks(LONG_PRESS_MS);
        StartLongPressTimer();
    }

    internal void Move(int pointerId, PointerType pointerType, Point position, Point screenPosition, long timestamp, ModifierKeys modifiers)
    {
        if (pointerType == PointerType.Pen)
        {
            WindowInputRouter.MouseMove(_window, position, screenPosition, leftDown: _penId == pointerId,
                rightDown: false, middleDown: false, modifiers, PointerType.Pen);
            return;
        }

        if (pointerId != _primaryId || _swallowPrimary || _longPressed)
        {
            return;
        }

        if (!_tracking)
        {
            WindowInputRouter.MouseMove(_window, position, screenPosition, leftDown: true,
                rightDown: false, middleDown: false, modifiers, PointerType.Touch);
            return;
        }

        if (_panning)
        {
            _scroll.Pan(position.X - _last.X, position.Y - _last.Y, timestamp);
            _last = position;
            return;
        }

        // A late timer must not let a hold turn into a drag that already happened.
        Tick(timestamp);
        if (_longPressed)
        {
            return;
        }

        var travel = new Vector(position.X - _start.X, position.Y - _start.Y);
        double distance = Math.Sqrt((travel.X * travel.X) + (travel.Y * travel.Y));
        if (distance > PAN_THRESHOLD_DIP)
        {
            BeginPan(position, travel, distance, timestamp, modifiers);
            return;
        }

        WindowInputRouter.MouseMove(_window, position, screenPosition, leftDown: true,
            rightDown: false, middleDown: false, modifiers, PointerType.Touch);
        if (CaptureConsumesDrag())
        {
            _tracking = false;
            StopLongPress();
        }
    }

    internal void Up(int pointerId, PointerType pointerType, Point position, Point screenPosition, long timestamp, ModifierKeys modifiers)
    {
        if (pointerType == PointerType.Pen)
        {
            PenUp(pointerId, position, screenPosition, modifiers);
            return;
        }

        if (_secondary.Remove(pointerId) || pointerId != _primaryId)
        {
            return;
        }

        bool swallowed = _swallowPrimary;
        bool longPressed = _longPressed;
        bool panning = _panning;
        EndPrimary();

        if (swallowed || longPressed)
        {
            return;
        }

        if (panning)
        {
            // The press was cancelled when the scroll began, so a release here would report a click.
            _scroll.Release(timestamp);
            return;
        }

        WindowInputRouter.MouseButton(_window, position, screenPosition, MouseButton.Left, isDown: false,
            leftDown: false, rightDown: false, middleDown: false, Math.Max(1, _clickCount), modifiers, PointerType.Touch);
    }

    internal void Cancel(int pointerId)
    {
        if (pointerId == _penId)
        {
            _penId = -1;
            WindowInputRouter.PointerCancel(_window);
            return;
        }

        if (_secondary.Remove(pointerId) || pointerId != _primaryId)
        {
            return;
        }

        bool routed = !_swallowPrimary && !_longPressed;
        EndPrimary();
        _scroll.Stop();

        // A press that became a scroll or was cancelled is not part of a tap chain.
        _taps.Reset();
        if (routed)
        {
            WindowInputRouter.PointerCancel(_window);
        }
    }

    /// <summary>Turns a held primary contact into a right click once it has rested long enough.</summary>
    internal void Tick(long timestamp)
    {
        if (!_tracking || _panning || _longPressed || _primaryId == -1 || timestamp < _longPressDeadline)
        {
            return;
        }

        StopLongPress();
        _longPressed = true;

        // The left press is spent: what was pressed must let go before the right click lands, or the
        // menu opens over a control still holding a touch press.
        WindowInputRouter.PointerCancel(_window);
        WindowInputRouter.MouseButton(_window, _start, _startScreen, MouseButton.Right, isDown: true,
            leftDown: false, rightDown: true, middleDown: false, clickCount: 1, ModifierKeys.None, PointerType.Touch);
        WindowInputRouter.MouseButton(_window, _start, _startScreen, MouseButton.Right, isDown: false,
            leftDown: false, rightDown: false, middleDown: false, clickCount: 1, ModifierKeys.None, PointerType.Touch);
    }

    private void BeginPan(Point position, Vector travel, double distance, long timestamp, ModifierKeys modifiers)
    {
        StopLongPress();
        _panning = true;

        // Nothing took the press, so abandon it rather than leaving the control under the finger pressed.
        WindowInputRouter.PointerCancel(_window);

        // Only the threshold itself is given up: anchoring at the crossing point instead would drop this
        // whole move, which makes a gesture travel less than the finger did.
        double consumed = PAN_THRESHOLD_DIP / distance;
        _last = new Point(_start.X + travel.X * consumed, _start.Y + travel.Y * consumed);
        _scroll.Begin(_start, _startScreen, travel, modifiers);
        _scroll.Pan(position.X - _last.X, position.Y - _last.Y, timestamp);
        _last = position;
    }

    private void PenDown(int pointerId, Point position, Point screenPosition, long timestamp, ModifierKeys modifiers)
    {
        _penId = pointerId;
        _penClickCount = _penClicks.Update(
            MouseButton.Left,
            (int)Math.Round(position.X),
            (int)Math.Round(position.Y),
            ToMilliseconds(timestamp),
            MULTI_TAP_WINDOW_MS,
            MULTI_TAP_SLOP_PEN_DIP,
            MULTI_TAP_SLOP_PEN_DIP);
        WindowInputRouter.MouseButton(_window, position, screenPosition, MouseButton.Left, isDown: true,
            leftDown: true, rightDown: false, middleDown: false, _penClickCount, modifiers, PointerType.Pen);
    }

    private void PenUp(int pointerId, Point position, Point screenPosition, ModifierKeys modifiers)
    {
        if (pointerId != _penId)
        {
            return;
        }

        _penId = -1;
        WindowInputRouter.MouseButton(_window, position, screenPosition, MouseButton.Left, isDown: false,
            leftDown: false, rightDown: false, middleDown: false, Math.Max(1, _penClickCount), modifiers, PointerType.Pen);
    }

    private void EndPrimary()
    {
        StopLongPress();
        _primaryId = -1;
        _swallowPrimary = false;
        _tracking = false;
        _panning = false;
        _longPressed = false;
    }

    /// <summary>
    /// True when the element holding capture consumes the movement itself (a slider, a scroll bar, a
    /// splitter, text selection, drag and drop, a popup dismiss watch) and so keeps it.
    /// </summary>
    private bool CaptureConsumesDrag()
    {
        // A command source captures only to decide whether the release counts as a click, so a drag may
        // take the gesture away from it.
        return _window.CapturedElement is UIElement captured && captured is not CommandSourceControl;
    }

    private void StartLongPressTimer()
    {
        if (_longPressTimer == null)
        {
            _longPressTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(LONG_PRESS_MS));
            _longPressTimer.Tick += OnLongPressTimer;
        }

        _longPressTimer.Stop();
        _longPressTimer.Start();
    }

    private void StopLongPress() => _longPressTimer?.Stop();

    private void OnLongPressTimer()
    {
        _longPressTimer?.Stop();
        Tick(Stopwatch.GetTimestamp());
    }

    private static uint ToMilliseconds(long timestamp)
        => unchecked((uint)(long)(timestamp * 1000.0 / Stopwatch.Frequency));

    private static long MillisecondsToTicks(double milliseconds)
        => (long)(milliseconds * Stopwatch.Frequency / 1000.0);
}
