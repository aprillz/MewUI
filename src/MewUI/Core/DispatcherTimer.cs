using Aprillz.MewUI.Platform;

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

    public event EventHandler? Tick;

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

        dispatcher.Send(() =>
        {
            lock (_gate)
            {
                if (_isEnabled)
                {
                    return;
                }

                _isEnabled = true;
                UnsubscribeFromDispatcherChanged_NoLock();
                _scheduled?.Dispose();
                _scheduled = dispatcher.Schedule(_interval, OnTick);
            }
        });
    }

    public void Stop()
    {
        var dispatcher = TryGetDispatcher();
        if (dispatcher == null)
        {
            lock (_gate)
            {
                _isEnabled = false;
                UnsubscribeFromDispatcherChanged_NoLock();
                _scheduled?.Dispose();
                _scheduled = null;
            }
            return;
        }

        dispatcher.Send(() =>
        {
            lock (_gate)
            {
                if (!_isEnabled)
                {
                    return;
                }

                _isEnabled = false;
                UnsubscribeFromDispatcherChanged_NoLock();
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

        Tick?.Invoke(this, EventArgs.Empty);

        lock (_gate)
        {
            if (!_isEnabled)
            {
                return;
            }

            _scheduled = dispatcher.Schedule(_interval, OnTick);
        }
    }

    private void Reschedule()
    {
        var dispatcher = TryGetDispatcher();
        if (dispatcher == null)
        {
            return;
        }

        dispatcher.Send(() =>
        {
            lock (_gate)
            {
                if (!_isEnabled)
                {
                    return;
                }

                _scheduled?.Dispose();
                _scheduled = dispatcher.Schedule(_interval, OnTick);
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

    private void UnsubscribeFromDispatcherChanged_NoLock()
    {
        if (!_subscribedToDispatcherChanged)
        {
            return;
        }

        _subscribedToDispatcherChanged = false;
        Application.DispatcherChanged -= OnDispatcherChanged;
    }

    private void OnDispatcherChanged(IUiDispatcher? dispatcher)
    {
        if (dispatcher == null)
        {
            return;
        }

        dispatcher.Send(() =>
        {
            lock (_gate)
            {
                if (!_isEnabled || _scheduled != null)
                {
                    return;
                }

                UnsubscribeFromDispatcherChanged_NoLock();
                _scheduled = dispatcher.Schedule(_interval, OnTick);
            }
        });
    }

    private static IUiDispatcher? TryGetDispatcher()
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
