using System.Collections.Concurrent;
using System.Diagnostics;

namespace Aprillz.MewUI.Platform.Linux;

internal sealed class LinuxUiDispatcher : SynchronizationContext, IUiDispatcher
{
    private readonly int _uiThreadId = Environment.CurrentManagedThreadId;
    private readonly ConcurrentQueue<Action> _queue = new();
    private readonly object _timersGate = new();
    private readonly List<ScheduledTimer> _timers = new();
    private long _nextTimerId;

    public bool IsOnUIThread => Environment.CurrentManagedThreadId == _uiThreadId;

    public void Post(Action action)
    {
        if (action == null)
        {
            return;
        }

        _queue.Enqueue(action);
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
        _queue.Enqueue(() =>
        {
            try { action(); }
            catch (Exception ex) { error = ex; }
            finally { gate.Set(); }
        });

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

        return handle;
    }

    public void ProcessWorkItems()
    {
        while (_queue.TryDequeue(out var action))
        {
            action();
        }

        ProcessTimers();
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
