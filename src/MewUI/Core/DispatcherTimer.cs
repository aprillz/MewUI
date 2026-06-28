namespace Aprillz.MewUI;

/// <summary>
/// UI-thread timer that raises <see cref="Tick"/> on the application's dispatcher.
/// </summary>
public sealed class DispatcherTimer : IDisposable
{
    private readonly object _gate = new();
    private IDisposable? _scheduled;
    private TimeSpan _interval = TimeSpan.FromMilliseconds(1000);
    private bool _isEnabled;
    private bool _subscribedToDispatcherChanged;

    public DispatcherTimer() { }

    public DispatcherTimer(TimeSpan interval)
    {
        Interval = interval;
    }

    /// <summary>
    /// Occurs when the timer interval elapses on the UI dispatcher.
    /// </summary>
    public event Action? Tick;

    /// <summary>
    /// Gets whether the timer is currently running.
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            lock (_gate)
            {
                return _isEnabled;
            }
        }
    }

    /// <summary>
    /// Gets or sets the time between <see cref="Tick"/> events.
    /// </summary>
    public TimeSpan Interval
    {
        get
        {
            lock (_gate)
            {
                return _interval;
            }
        }
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Interval must be greater than zero.");
            }

            lock (_gate)
            {
                _interval = value;
                if (_isEnabled)
                {
                    Reschedule();
                }
            }
        }
    }

    /// <summary>
    /// Starts the timer. If the application dispatcher is not yet available, the timer will start once it becomes available.
    /// </summary>
    public void Start()
    {
        var dispatcher = TryGetDispatcher();
        if (dispatcher == null)
        {
            lock (_gate)
            {
                if (_isEnabled)
                {
                    return;
                }

                _isEnabled = true;
                SubscribeToDispatcherChanged();
            }

            return;
        }

        dispatcher.Invoke(() =>
        {
            lock (_gate)
            {
                if (_isEnabled)
                {
                    return;
                }

                _isEnabled = true;
                UnsubscribeFromDispatcherChanged();
                _scheduled?.Dispose();
                _scheduled = (dispatcher as IDispatcherCore)!.Schedule(_interval, OnTick);
            }
        });
    }

    /// <summary>
    /// Stops the timer and cancels any scheduled tick.
    /// </summary>
    public void Stop()
    {
        var dispatcher = TryGetDispatcher();
        if (dispatcher == null)
        {
            lock (_gate)
            {
                _isEnabled = false;
                UnsubscribeFromDispatcherChanged();
                _scheduled?.Dispose();
                _scheduled = null;
            }
            return;
        }

        dispatcher.Invoke(() =>
        {
            lock (_gate)
            {
                if (!_isEnabled)
                {
                    return;
                }

                _isEnabled = false;
                UnsubscribeFromDispatcherChanged();
                _scheduled?.Dispose();
                _scheduled = null;
            }
        });
    }

    private void OnTick()
    {
        var dispatcher = TryGetDispatcher();
        if (dispatcher == null)
        {
            Stop();
            return;
        }

        lock (_gate)
        {
            if (!_isEnabled)
            {
                return;
            }

            // One-shot schedule; re-arm after firing (WPF-style).
            _scheduled?.Dispose();
            _scheduled = null;
        }

        Tick?.Invoke();

        lock (_gate)
        {
            if (!_isEnabled)
            {
                return;
            }

            // The handler may have re-armed us (e.g. Stop()+Start() inside Tick): in that case _scheduled
            // is already a fresh tick, so re-arming again here would leak it and double-fire. Only schedule
            // when the handler left us without a pending tick.
            if (_scheduled != null)
            {
                return;
            }

            _scheduled = (dispatcher as IDispatcherCore)!.Schedule(_interval, OnTick);
        }
    }

    private void Reschedule()
    {
        var dispatcher = TryGetDispatcher();
        if (dispatcher == null)
        {
            return;
        }

        dispatcher.Invoke(() =>
        {
            lock (_gate)
            {
                if (!_isEnabled)
                {
                    return;
                }

                _scheduled?.Dispose();
                _scheduled = (dispatcher as IDispatcherCore)!.Schedule(_interval, OnTick);
            }
        });
    }

    private void SubscribeToDispatcherChanged()
    {
        if (_subscribedToDispatcherChanged)
        {
            return;
        }

        _subscribedToDispatcherChanged = true;
        Application.DispatcherChanged += OnDispatcherChanged;
    }

    private void UnsubscribeFromDispatcherChanged()
    {
        if (!_subscribedToDispatcherChanged)
        {
            return;
        }

        _subscribedToDispatcherChanged = false;
        Application.DispatcherChanged -= OnDispatcherChanged;
    }

    private void OnDispatcherChanged(IDispatcher? dispatcher)
    {
        if (dispatcher == null)
        {
            return;
        }

        dispatcher.Invoke(() =>
        {
            lock (_gate)
            {
                if (!_isEnabled || _scheduled != null)
                {
                    return;
                }

                UnsubscribeFromDispatcherChanged();
                _scheduled = (dispatcher as IDispatcherCore)!.Schedule(_interval, OnTick);
            }
        });
    }

    private static IDispatcher? TryGetDispatcher()
    {
        if (!Application.IsRunning)
        {
            return null;
        }

        return Application.Current.Dispatcher;
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
