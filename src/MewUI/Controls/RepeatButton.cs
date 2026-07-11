namespace Aprillz.MewUI.Controls;

/// <summary>
/// A button that raises <see cref="Button.Click"/> once on press, then repeatedly while the
/// mouse stays pressed and hovering over it, after an initial delay.
/// </summary>
public class RepeatButton : Button
{
    public static readonly MewProperty<double> DelayProperty =
        MewProperty<double>.Register<RepeatButton>(nameof(Delay), 400.0, MewPropertyOptions.None);

    public static readonly MewProperty<double> IntervalProperty =
        MewProperty<double>.Register<RepeatButton>(nameof(Interval), 80.0, MewPropertyOptions.None);

    /// <summary>Gets or sets the delay, in milliseconds, between the initial press and the first repeated click.</summary>
    public double Delay
    {
        get => GetValue(DelayProperty);
        set => SetValue(DelayProperty, value);
    }

    /// <summary>Gets or sets the interval, in milliseconds, between repeated clicks.</summary>
    public double Interval
    {
        get => GetValue(IntervalProperty);
        set => SetValue(IntervalProperty, value);
    }

    private DispatcherTimer? _repeatTimer;
    private bool _isHeld;
    private bool _inDelayPhase;

    private protected override bool SuppressClickOnMouseUp => true;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButton.Left || !IsPressed)
        {
            return;
        }

        _isHeld = true;
        RaiseClick();

        _inDelayPhase = true;
        var timer = _repeatTimer ??= CreateRepeatTimer();
        timer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, Delay));
        timer.Start();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButton.Left)
        {
            return;
        }

        StopRepeat();

        // A leave earlier in the hold clears IsPressed (Button.OnMouseLeave) without releasing
        // capture, so the base call above skips its own release; make sure it still happens here.
        if (IsMouseCaptured && FindVisualRoot() is Window window)
        {
            window.ReleaseMouseCapture();
            e.Handled = true;
        }
    }

    protected override void OnEnabledChanged()
    {
        base.OnEnabledChanged();

        if (!IsEffectivelyEnabled)
        {
            StopRepeat();
        }
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);

        if (newRoot == null)
        {
            StopRepeat();
        }
    }

    private DispatcherTimer CreateRepeatTimer()
    {
        var timer = new DispatcherTimer();
        timer.Tick += OnRepeatTick;
        return timer;
    }

    private void OnRepeatTick()
    {
        if (!_isHeld || !IsEffectivelyEnabled || !IsMouseCaptured)
        {
            StopRepeat();
            return;
        }

        if (_inDelayPhase)
        {
            _inDelayPhase = false;
            _repeatTimer!.Interval = TimeSpan.FromMilliseconds(Math.Max(1, Interval));
        }

        // Pointer away from the button pauses repeating without stopping the timer; the next
        // tick fires again as soon as it is back over (mirrors capture-independent IsMouseOver).
        if (IsMouseOver)
        {
            RaiseClick();
        }
    }

    private void StopRepeat()
    {
        _isHeld = false;
        _repeatTimer?.Stop();
    }

    protected override void OnDispose()
    {
        if (_repeatTimer != null)
        {
            _repeatTimer.Tick -= OnRepeatTick;
            _repeatTimer.Dispose();
            _repeatTimer = null;
        }

        base.OnDispose();
    }
}
