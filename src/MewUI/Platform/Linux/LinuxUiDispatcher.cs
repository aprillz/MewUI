using System.Diagnostics;

namespace Aprillz.MewUI.Platform.Linux;

internal sealed class LinuxUiDispatcher : SynchronizationContext, IUiDispatcher
{
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private readonly UiDispatcherQueue _queue = new();
    private readonly object _timersGate = new();
    private readonly List<ScheduledTimer> _timers = new();
    private long _nextTimerId;
    private Action? _wake;
    private int _wakeRequested;

    public bool IsOnUIThread => Environment.CurrentManagedThreadId == _uiThreadId;

    public void Post(Action action)
    {
        if (action != null)
        {
            Post(action, UiDispatcherPriority.Background);
        }
    }

    public void Post(Action action, UiDispatcherPriority priority)
    {
        ArgumentNullException.ThrowIfNull(action);
        _queue.Enqueue(priority, action);
        RequestWake();
    }

    public bool PostMerged(DispatcherMergeKey mergeKey, Action action, UiDispatcherPriority priority)
    {
        var enqueued = _queue.EnqueueMerged(priority, mergeKey, action);
        if (enqueued)
        {
            RequestWake();
        }
        return enqueued;
    }

    public void Send(Action action)
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
        Post(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { gate.Set(); }
        }, UiDispatcherPriority.Input);

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
        // Pump the queue/timers until they become stable (bounded).
        // Work items can schedule new work at a higher priority; without re-pumping,
        // the platform loop can observe HasPendingWork=true continuously and never block in poll().
        const int MaxPumpIterations = 32;
        for (int i = 0; i < MaxPumpIterations; i++)
        {
            _queue.Process();
            ProcessTimers();

            GetPendingState(out var queueHasWork, out var dueTimers, out _);
            if (!queueHasWork && dueTimers == 0)
            {
                break;
            }
        }

    }

    internal void SetWake(Action? wake)
    {
        _wake = wake;
    }

    internal void ClearWakeRequest()
    {
        Interlocked.Exchange(ref _wakeRequested, 0);
    }

    internal bool HasPendingWork
    {
        get
        {
            if (_queue.HasWork)
            {
                return true;
            }

            // Only treat timers as "pending work" when they are due now.
            // Future timers should not prevent the platform host loop from blocking in poll.
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
            return maxMs;
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

    internal void GetPendingState(out bool queueHasWork, out int dueTimers, out int totalTimers)
    {
        queueHasWork = _queue.HasWork;

        long now = Stopwatch.GetTimestamp();
        int due = 0;
        int total = 0;
        lock (_timersGate)
        {
            for (int i = 0; i < _timers.Count; i++)
            {
                var timer = _timers[i];
                if (timer.Canceled)
                {
                    continue;
                }

                total++;
                if (timer.DueAtTicks <= now)
                {
                    due++;
                }
            }
        }

        dueTimers = due;
        totalTimers = total;
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

        foreach (var action in dueActions)
        {
            action();
        }
    }

    private void CancelTimer(long id)
    {
        lock (_timersGate)
        {
            for (int i = 0; i < _timers.Count; i++)
            {
                if (_timers[i].Id == id)
                {
                    _timers[i] = _timers[i] with { Canceled = true };
                    return;
                }
            }
        }
    }

    private void RequestWake()
    {
        var wake = _wake;
        if (wake == null)
        {
            return;
        }

        if (Interlocked.Exchange(ref _wakeRequested, 1) == 0)
        {
            wake();
        }
    }

    public override void Post(SendOrPostCallback d, object? state)
        => Post(() => d(state));

    public override void Send(SendOrPostCallback d, object? state)
        => Send(() => d(state));

    private readonly record struct ScheduledTimer(long Id, long DueAtTicks, Action Action, bool Canceled = false);

    private sealed class TimerHandle : IDisposable
    {
        private LinuxUiDispatcher? _dispatcher;
        private long _id;

        public TimerHandle(LinuxUiDispatcher dispatcher, long id)
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

            var id = Interlocked.Exchange(ref _id, 0);
            if (id != 0)
            {
                dispatcher.CancelTimer(id);
            }
        }
    }
}
