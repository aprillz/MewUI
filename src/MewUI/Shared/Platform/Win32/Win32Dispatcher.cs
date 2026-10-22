using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;

namespace Aprillz.MewUI.Platform.Win32;

/// <summary>
/// Dispatcher for the Win32 message loop: work is marshalled to the UI thread through a window message
/// and timers run on the window's timer queue.
/// </summary>
public sealed class Win32Dispatcher : SynchronizationContext, IDispatcher, IDispatcherCore
{
    private readonly DispatcherQueue _queue = new();
    private readonly Dictionary<nuint, Action> _timerCallbacks = new();
    private readonly nint _hwnd;
    private readonly int _mainThreadId;
    private int _nextTimerId;
    private int _invokeRequested;

    internal const uint WM_INVOKE = WindowMessages.WM_USER + 1;

    internal Win32Dispatcher(nint hwnd)
    {
        _hwnd = hwnd;
        _mainThreadId = Environment.CurrentManagedThreadId;
    }

    /// <inheritdoc/>
    public bool IsOnUIThread => Environment.CurrentManagedThreadId == _mainThreadId;

    /// <inheritdoc/>
    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);
        _queue.Enqueue(DispatcherPriority.Normal, () => d(state));
        RequestInvoke();
    }

    /// <inheritdoc/>
    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);

        if (IsOnUIThread)
        {
            d(state);
            return;
        }

        using var signal = new ManualResetEventSlim(false);
        _queue.EnqueueWithSignal(DispatcherPriority.Normal, () => d(state), signal);
        RequestInvoke();
        signal.Wait();
    }

    /// <inheritdoc/>
    public DispatcherOperation BeginInvoke(Action action)
        => BeginInvoke(DispatcherPriority.Normal, action);

    /// <inheritdoc/>
    public DispatcherOperation BeginInvoke(DispatcherPriority priority, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var op = _queue.EnqueueWithOperation(priority, action);
        RequestInvoke();
        return op;
    }

    /// <summary>
    /// Posts <paramref name="action"/> to the UI thread unless an item with the same <paramref name="mergeKey"/>
    /// is already pending; returns whether it was enqueued.
    /// </summary>
    public bool PostMerged(DispatcherMergeKey mergeKey, Action action, DispatcherPriority priority)
    {
        if (!_queue.EnqueueMerged(priority, mergeKey, action))
        {
            return false;
        }

        RequestInvoke();
        return true;
    }

    /// <inheritdoc/>
    public void Invoke(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        Send(_ => action(), null);
    }

    /// <summary>Schedules <paramref name="action"/> on the UI thread after <paramref name="dueTime"/>; disposing the result cancels it.</summary>
    public IDisposable Schedule(TimeSpan dueTime, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        if (dueTime < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(dueTime), dueTime, "DueTime must be non-negative.");
        }

        if (!IsOnUIThread)
        {
            nuint id = 0;
            Invoke(() => id = ScheduleOnUiThread(dueTime, action));
            return new TimerHandle(this, id);
        }

        nuint timerId = ScheduleOnUiThread(dueTime, action);
        return new TimerHandle(this, timerId);
    }

    private nuint ScheduleOnUiThread(TimeSpan dueTime, Action action)
    {
        // Clamp to Win32 timer resolution (milliseconds).
        uint ms = dueTime == TimeSpan.Zero ? 1u : (uint)Math.Max(1, Math.Min(dueTime.TotalMilliseconds, uint.MaxValue));

        nuint id = (nuint)unchecked(++_nextTimerId);
        _timerCallbacks[id] = action;

        // WM_TIMER will keep firing until killed.
        if (User32.SetTimer(_hwnd, id, ms, 0) == 0)
        {
            _timerCallbacks.Remove(id);
            throw new InvalidOperationException("SetTimer failed.");
        }

        return id;
    }

    internal bool ProcessTimer(nuint timerId)
    {
        if (!_timerCallbacks.TryGetValue(timerId, out var callback))
        {
            return false;
        }

        // Kill first (WM_TIMER repeats).
        _timerCallbacks.Remove(timerId);
        User32.KillTimer(_hwnd, timerId);

        callback();
        return true;
    }

    private void CancelTimer(nuint timerId)
    {
        if (timerId == 0)
        {
            return;
        }

        if (!IsOnUIThread)
        {
            BeginInvoke(() => CancelTimer(timerId));
            return;
        }

        if (_timerCallbacks.Remove(timerId))
        {
            User32.KillTimer(_hwnd, timerId);
        }
    }

    /// <summary>Processes a batch of queued work items on the UI thread.</summary>
    public void ProcessWorkItems()
    {
        _queue.Process();
    }

    internal bool HasPendingWork => _queue.HasWork;

    internal void ClearInvokeRequest()
        => Interlocked.Exchange(ref _invokeRequested, 0);

    private void RequestInvoke()
    {
        if (_hwnd == 0)
        {
            return;
        }

        if (Interlocked.Exchange(ref _invokeRequested, 1) == 0)
        {
            User32.PostMessage(_hwnd, WM_INVOKE, 0, 0);
        }
    }

    private sealed class TimerHandle : IDisposable
    {
        private Win32Dispatcher? _dispatcher;
        private nuint _timerId;

        public TimerHandle(Win32Dispatcher dispatcher, nuint timerId)
        {
            _dispatcher = dispatcher;
            _timerId = timerId;
        }

        public void Dispose()
        {
            var dispatcher = Interlocked.Exchange(ref _dispatcher, null);
            if (dispatcher == null)
            {
                return;
            }

            var id = Interlocked.Exchange(ref _timerId, 0);
            dispatcher.CancelTimer(id);
        }
    }
}
