using System.Diagnostics;

namespace Aprillz.MewUI.Platform;

/// <summary>
/// Shared UI-thread dispatcher for platform hosts that own a managed work queue and timer heap and
/// wake the UI thread through a host-supplied callback. Win32 uses OS timers and window messages
/// instead and does not derive from this type. Concrete hosts supply three policies that genuinely
/// differ between platforms: <see cref="MaxPumpIterations"/>, <see cref="NoTimerPollTimeout"/> and
/// <see cref="DispatchDueTimer"/>.
/// </summary>
internal abstract class ManagedUiDispatcher : SynchronizationContext, IDispatcher, IDispatcherCore
{
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private readonly DispatcherQueue _queue = new();
    private readonly object _timersGate = new();
    private readonly List<ScheduledTimer> _timers = new();
    private long _nextTimerId;
    private Action? _wake;
    private int _wakeRequested;

    public bool IsOnUIThread => Environment.CurrentManagedThreadId == _uiThreadId;

    // Work items can post new higher-priority work, so a single pass can leave the host loop spinning
    // on pending work; re-pump up to this many passes per ProcessWorkItems call.
    protected abstract int MaxPumpIterations { get; }

    public DispatcherOperation BeginInvoke(Action action)
        => BeginInvoke(DispatcherPriority.Normal, action);

    public DispatcherOperation BeginInvoke(DispatcherPriority priority, Action action)
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

        var dueAt = dueTime == TimeSpan.Zero
            ? Stopwatch.GetTimestamp()
            : Stopwatch.GetTimestamp() + (long)(dueTime.TotalSeconds * Stopwatch.Frequency);

        lock (_timersGate)
        {
            _timers.Add(new ScheduledTimer(id, dueAt, action));
        }

        RequestWake();
        return handle;
    }

    public void ProcessWorkItems()
    {
        int maxPasses = Math.Max(1, MaxPumpIterations);
        for (int i = 0; i < maxPasses; i++)
        {
            _queue.Process();
            ProcessDueTimers();

            if (i + 1 >= maxPasses)
            {
                break;
            }

            GetPendingState(out var queueHasWork, out var dueTimers, out _);
            if (!queueHasWork && dueTimers == 0)
            {
                break;
            }
        }
    }

    internal void SetWake(Action? wake) => _wake = wake;

    internal void ClearWakeRequest() => Interlocked.Exchange(ref _wakeRequested, 0);

    internal bool HasPendingWork
    {
        get
        {
            if (_queue.HasWork)
            {
                return true;
            }

            // Only treat timers as pending when they are due now; future timers must not stop the host
            // loop from blocking in its OS wait.
            long now = Stopwatch.GetTimestamp();
            lock (_timersGate)
            {
                for (int i = 0; i < _timers.Count; i++)
                {
                    var timer = _timers[i];
                    if (!timer.Canceled && timer.DueAtTicks <= now)
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
            return NoTimerPollTimeout(maxMs);
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

    // Poll timeout to report when no timers are scheduled. X11 caps it so best-effort DPI polling still
    // wakes; macOS returns -1 to wait until an OS event or an explicit wake.
    protected abstract int NoTimerPollTimeout(int maxMs);

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

    private void ProcessDueTimers()
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
            DispatchDueTimer(dueActions[i]);
        }
    }

    // Runs one due timer callback. X11 runs it inline; macOS re-queues it at Background so it flows
    // through the queue's priority ordering and exception routing.
    protected abstract void DispatchDueTimer(Action action);

    /// <summary>Enqueues <paramref name="action"/> on the work queue at the given priority.</summary>
    protected void EnqueueOnQueue(DispatcherPriority priority, Action action)
        => _queue.Enqueue(priority, action);

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

    protected void RequestWake()
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
        => BeginInvoke(() => d(state));

    public override void Send(SendOrPostCallback d, object? state)
        => Invoke(() => d(state));

    private readonly record struct ScheduledTimer(long Id, long DueAtTicks, Action Action, bool Canceled = false);

    private sealed class TimerHandle : IDisposable
    {
        private ManagedUiDispatcher? _dispatcher;
        private long _id;

        public TimerHandle(ManagedUiDispatcher dispatcher, long id)
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
