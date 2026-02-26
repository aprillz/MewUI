using System.Diagnostics;

namespace Aprillz.MewUI.Platform.MacOS;

/// <summary>
/// macOS UI dispatcher placeholder.
/// A full implementation will integrate with CFRunLoop/NSRunLoop to wake the UI thread.
/// </summary>
internal sealed class MacOSDispatcher : SynchronizationContext, IDispatcher, IDispatcherCore
{
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private readonly DispatcherQueue _queue = new();
    private readonly object _timersGate = new();
    private readonly List<ScheduledTimer> _timers = new();
    private long _nextTimerId;
    private Action? _wake;
    private int _wakeRequested;

    public bool IsOnUIThread => Environment.CurrentManagedThreadId == _uiThreadId;

    public DispatcherOperation BeginInvoke(Action action)
        => BeginInvoke(action, DispatcherPriority.Background);

    public DispatcherOperation BeginInvoke(Action action, DispatcherPriority priority)
    {
        ArgumentNullException.ThrowIfNull(action);
        var op = _queue.EnqueueWithOperation(priority, action);
        RequestWake();
        return op;
    }

    public bool PostMerged(DispatcherMergeKey mergeKey, Action action, DispatcherPriority priority)
    {
        var enqueued = _queue.EnqueueMerged(priority, mergeKey, action);
        if (enqueued)
        {
            RequestWake();
        }
        return enqueued;
    }

    public void Invoke(Action action)
    {
        if (action == null)
        {
            return;
        }

        if (IsOnUIThread)
        {
            action();
            return;
        }

        using var gate = new ManualResetEventSlim(false);
        Exception? error = null;
        _queue.Enqueue(DispatcherPriority.Input, () =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { gate.Set(); }
        });
        RequestWake();

        gate.Wait();
        if (error != null)
        {
            throw new AggregateException(error);
        }
    }

    public IDisposable Schedule(TimeSpan dueTime, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (dueTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(dueTime), dueTime, "DueTime must be non-negative.");
        }

        var id = Interlocked.Increment(ref _nextTimerId);
        var handle = new TimerHandle(this, id);

        var dueAt = Stopwatch.GetTimestamp() + (long)(dueTime.TotalSeconds * Stopwatch.Frequency);
        if (dueTime == TimeSpan.Zero)
        {
            dueAt = Stopwatch.GetTimestamp();
        }

        lock (_timersGate)
        {
            _timers.Add(new ScheduledTimer(id, dueAt, action));
        }

        RequestWake();
        return handle;
    }

    public void ProcessWorkItems()
    {
        _queue.Process();
        ProcessTimers();
    }

    internal void SetWake(Action? wake) => _wake = wake;

    internal void ClearWakeRequest()
        => Interlocked.Exchange(ref _wakeRequested, 0);

    internal bool HasPendingWork
    {
        get
        {
            if (_queue.HasWork)
            {
                return true;
            }

            long now = Stopwatch.GetTimestamp();
            lock (_timersGate)
            {
                for (int i = 0; i < _timers.Count; i++)
                {
                    var timer = _timers[i];
                    if (timer.Canceled)
                    {
                        continue;
                    }

                    if (timer.DueAtTicks <= now)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }

    internal int GetPollTimeoutMs(int maxMs)
    {
        if (maxMs <= 0)
        {
            return 0;
        }

        long now = Stopwatch.GetTimestamp();
        long? nextDue = null;

        lock (_timersGate)
        {
            for (int i = 0; i < _timers.Count; i++)
            {
                var timer = _timers[i];
                if (timer.Canceled)
                {
                    continue;
                }

                if (nextDue == null || timer.DueAtTicks < nextDue.Value)
                {
                    nextDue = timer.DueAtTicks;
                }
            }
        }

        if (nextDue == null)
        {
            // No timers: caller can wait indefinitely (until an OS event or an explicit wake).
            return -1;
        }

        long deltaTicks = nextDue.Value - now;
        if (deltaTicks <= 0)
        {
            return 0;
        }

        double ms = (double)deltaTicks * 1000.0 / Stopwatch.Frequency;
        if (double.IsNaN(ms) || double.IsInfinity(ms) || ms <= 0)
        {
            return 0;
        }

        return (int)Math.Min(maxMs, Math.Max(0, Math.Ceiling(ms)));
    }

    private void ProcessTimers()
    {
        List<Action>? dueActions = null;
        var now = Stopwatch.GetTimestamp();

        lock (_timersGate)
        {
            for (int i = _timers.Count - 1; i >= 0; i--)
            {
                var timer = _timers[i];
                if (timer.Canceled)
                {
                    _timers.RemoveAt(i);
                    continue;
                }

                if (timer.DueAtTicks <= now)
                {
                    dueActions ??= new List<Action>();
                    dueActions.Add(timer.Action);
                    _timers.RemoveAt(i);
                }
            }
        }

        if (dueActions == null)
        {
            return;
        }

        for (int i = 0; i < dueActions.Count; i++)
        {
            _queue.Enqueue(DispatcherPriority.Background, dueActions[i]);
        }
    }

    private void RequestWake()
    {
        if (Interlocked.Exchange(ref _wakeRequested, 1) == 1)
        {
            return;
        }

        _wake?.Invoke();
    }

    public override void Post(SendOrPostCallback d, object? state)
        => BeginInvoke(() => d(state));

    public override void Send(SendOrPostCallback d, object? state)
        => Invoke(() => d(state));

    private sealed class TimerHandle : IDisposable
    {
        private MacOSDispatcher? _dispatcher;
        private readonly long _id;

        public TimerHandle(MacOSDispatcher dispatcher, long id)
        {
            _dispatcher = dispatcher;
            _id = id;
        }

        public void Dispose()
        {
            var dispatcher = Interlocked.Exchange(ref _dispatcher, null);
            if (dispatcher == null)
            {
                return;
            }

            lock (dispatcher._timersGate)
            {
                for (int i = 0; i < dispatcher._timers.Count; i++)
                {
                    if (dispatcher._timers[i].Id == _id)
                    {
                        dispatcher._timers[i] = dispatcher._timers[i] with { Canceled = true };
                        break;
                    }
                }
            }
        }
    }

    private readonly record struct ScheduledTimer(long Id, long DueAtTicks, Action Action)
    {
        public bool Canceled { get; init; }
    }
}
