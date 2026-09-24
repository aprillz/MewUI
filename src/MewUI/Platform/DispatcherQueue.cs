using System.Collections.Concurrent;

namespace Aprillz.MewUI.Platform;

internal sealed class DispatcherQueue
{
    internal readonly struct WorkItem
    {
        public Action? Action { get; init; }
        public DispatcherMergeKey? MergeKey { get; init; }
        public ManualResetEventSlim? Signal { get; init; }
        public DispatcherOperation? Operation { get; init; }
    }

    private readonly ConcurrentQueue<WorkItem>[] _queues;
    private readonly ConcurrentDictionary<DispatcherMergeKey, byte> _mergeKeys = new();

    public bool HasWork
    {
        get
        {
            for (int i = _queues.Length - 1; i >= 0; i--)
            {
                if (!_queues[i].IsEmpty)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public DispatcherQueue()
    {
        _queues = new ConcurrentQueue<WorkItem>[Enum.GetValues<DispatcherPriority>().Length];
        for (int i = 0; i < _queues.Length; i++)
        {
            _queues[i] = new ConcurrentQueue<WorkItem>();
        }
    }

    public void Enqueue(DispatcherPriority priority, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        EnqueueInternal(priority, new WorkItem { Action = action });
    }

    public DispatcherOperation EnqueueWithOperation(DispatcherPriority priority, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var op = new DispatcherOperation(priority, action);
        EnqueueInternal(priority, new WorkItem { Operation = op });
        return op;
    }

    public bool EnqueueMerged(DispatcherPriority priority, DispatcherMergeKey mergeKey, Action action)
    {
        ArgumentNullException.ThrowIfNull(mergeKey);
        ArgumentNullException.ThrowIfNull(action);

        if (!_mergeKeys.TryAdd(mergeKey, 0))
        {
            return false;
        }

        EnqueueInternal(priority, new WorkItem { Action = action, MergeKey = mergeKey });
        return true;
    }

    public void EnqueueWithSignal(DispatcherPriority priority, Action action, ManualResetEventSlim signal)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(signal);
        EnqueueInternal(priority, new WorkItem { Action = action, Signal = signal });
    }

    public void Process()
    {
        bool processedAny = false;

        // Process highest priority first, going back up whenever an item posted higher-priority work:
        // layout requested by a background item has to run before the idle items queued behind it.
        for (int priority = _queues.Length - 1; priority >= 0; priority--)
        {
            var queue = _queues[priority];
            while (!HasWorkAbove(priority) && queue.TryDequeue(out var item))
            {
                // If the operation's priority was changed while pending,
                // re-enqueue to the correct queue without running cleanup.
                var op = item.Operation;
                if (op != null && op.Status != DispatcherOperationStatus.Aborted)
                {
                    int target = (int)op.Priority;
                    if (target != priority && (uint)target < (uint)_queues.Length)
                    {
                        _queues[target].Enqueue(item);
                        continue;
                    }
                }

                // Merge key lifetime is enqueue until execution starts: remove it here, before
                // the action runs, so a re-post with the same key during execution enqueues a
                // fresh item instead of being silently dropped by EnqueueMerged.
                if (item.MergeKey != null)
                {
                    _mergeKeys.TryRemove(item.MergeKey, out _);
                }

                bool faulted = false;
                try
                {
                    Action action;
                    if (op != null)
                    {
                        if (!op.TryMarkExecuting())
                        {
                            continue;
                        }

                        action = op.TakeActionForExecution();
                    }
                    else
                    {
                        action = item.Action
                            ?? throw new InvalidOperationException("Dispatcher work item has no action.");
                    }

                    processedAny = true;
                    action();
                }
                catch (Exception ex)
                {
                    // The operation reaches its Faulted terminal state carrying the exception,
                    // independent of how the app-level handler routes it below.
                    faulted = true;
                    op?.MarkFaulted(ex);

                    // Dispatcher-level exception handling:
                    // - If the app handler marks it as handled, continue processing.
                    // - Otherwise, record fatal and request shutdown to unwind the message loop.
                    if (Application.IsRunning && Application.Current.TryHandleDispatcherException(ex))
                    {
                        continue;
                    }

                    if (Application.IsRunning)
                    {
                        Application.Current.NotifyFatalDispatcherException(ex);
                        Application.Shutdown();
                    }

                    return;
                }
                finally
                {
                    if (!faulted)
                    {
                        op?.MarkCompleted();
                    }

                    item.Signal?.Set();
                }
            }

            if (HasWorkAbove(priority))
            {
                priority = _queues.Length;
            }
        }

        // Pure-evaluation command model: after a dispatcher turn that ran work (and so may have
        // mutated UI-facing state), re-query the registered command sources. The pass runs
        // synchronously here (not as a queued item) so it cannot keep the queue busy by itself.
        if (processedAny)
        {
            NotifyDrainCompleted();
        }
    }

    private bool HasWorkAbove(int priority)
    {
        for (int index = _queues.Length - 1; index > priority; index--)
        {
            if (!_queues[index].IsEmpty)
            {
                return true;
            }
        }

        return false;
    }

    private static void NotifyDrainCompleted()
    {
        if (!Application.IsRunning)
        {
            return;
        }

        try
        {
            var windows = Application.Current.SnapshotWindows();
            for (int i = 0; i < windows.Length; i++)
            {
                windows[i].EvaluateCommandStates();
            }
        }
        catch (Exception ex)
        {
            // A throwing CanExecute is a programming error; route it through the same policy as
            // dispatcher work items instead of swallowing it.
            if (Application.IsRunning && !Application.Current.TryHandleDispatcherException(ex))
            {
                Application.Current.NotifyFatalDispatcherException(ex);
                Application.Shutdown();
            }
        }
    }

    private void EnqueueInternal(DispatcherPriority priority, in WorkItem item)
    {
        int idx = (int)priority;
        if ((uint)idx >= (uint)_queues.Length)
        {
            idx = (int)DispatcherPriority.Background;
        }

        _queues[idx].Enqueue(item);
    }
}
