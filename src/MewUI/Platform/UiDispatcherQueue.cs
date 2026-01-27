using System.Collections.Concurrent;

namespace Aprillz.MewUI.Platform;

internal sealed class UiDispatcherQueue
{
    internal readonly struct WorkItem
    {
        public required Action Action { get; init; }
        public DispatcherMergeKey? MergeKey { get; init; }
        public ManualResetEventSlim? Signal { get; init; }
    }

    private readonly ConcurrentQueue<WorkItem>[] _queues;
    private readonly ConcurrentDictionary<DispatcherMergeKey, byte> _mergeKeys = new();

    public bool HasWork
    {
        get
        {
            for (int i = 0; i < _queues.Length; i++)
            {
                if (!_queues[i].IsEmpty)
                {
                    return true;
                }
            }

            return false;
        }
    }

    public UiDispatcherQueue()
    {
        _queues = new ConcurrentQueue<WorkItem>[Enum.GetValues<UiDispatcherPriority>().Length];
        for (int i = 0; i < _queues.Length; i++)
        {
            _queues[i] = new ConcurrentQueue<WorkItem>();
        }
    }

    public void Enqueue(UiDispatcherPriority priority, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        EnqueueInternal(priority, new WorkItem { Action = action });
    }

    public bool EnqueueMerged(UiDispatcherPriority priority, DispatcherMergeKey mergeKey, Action action)
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

    public void EnqueueWithSignal(UiDispatcherPriority priority, Action action, ManualResetEventSlim signal)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(signal);
        EnqueueInternal(priority, new WorkItem { Action = action, Signal = signal });
    }

    public void Process()
    {
        // Process highest priority first.
        for (int p = 0; p < _queues.Length; p++)
        {
            var queue = _queues[p];
            while (queue.TryDequeue(out var item))
            {
                try
                {
                    item.Action();
                }
                catch (Exception ex)
                {
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
                        Application.Quit();
                    }

                    return;
                }
                finally
                {
                    if (item.MergeKey != null)
                    {
                        _mergeKeys.TryRemove(item.MergeKey, out _);
                    }

                    item.Signal?.Set();
                }
            }
        }
    }

    private void EnqueueInternal(UiDispatcherPriority priority, in WorkItem item)
    {
        int idx = (int)priority;
        if ((uint)idx >= (uint)_queues.Length)
        {
            idx = (int)UiDispatcherPriority.Background;
        }

        _queues[idx].Enqueue(item);
    }
}
