namespace Aprillz.MewUI;

/// <summary>
/// Defines the relative priority used by <see cref="IDispatcher"/> when ordering work items.
/// Lower values run first.
/// </summary>
public enum DispatcherPriority
{
    /// <summary>
    /// Input processing (mouse/keyboard).
    /// </summary>
    Input = 0,

    /// <summary>
    /// Layout processing (measure/arrange).
    /// </summary>
    Layout = 1,

    /// <summary>
    /// Rendering work (invalidate/paint).
    /// </summary>
    Render = 2,

    /// <summary>
    /// Background work that should not block interactive UI.
    /// </summary>
    Background = 3,

    /// <summary>
    /// Lowest priority work that should only run when idle.
    /// </summary>
    Idle = 4,
}

/// <summary>
/// Identifies a merge bucket for merged dispatcher posts.
/// </summary>
public sealed class DispatcherMergeKey
{
    /// <summary>
    /// Gets the associated priority for this merge key (diagnostics only).
    /// </summary>
    public DispatcherPriority Priority { get; }

    internal DispatcherMergeKey(DispatcherPriority priority)
    {
        Priority = priority;
    }

    public override string ToString() => $"DispatcherMergeKey({Priority})";
}

/// <summary>
/// Represents the status of a <see cref="DispatcherOperation"/>.
/// </summary>
public enum DispatcherOperationStatus
{
    /// <summary>The operation is queued and has not started.</summary>
    Pending,
    /// <summary>The operation is currently executing.</summary>
    Executing,
    /// <summary>The operation has completed.</summary>
    Completed,
    /// <summary>The operation was aborted before execution.</summary>
    Aborted,
}

/// <summary>
/// Represents an asynchronous operation dispatched via <see cref="IDispatcher.BeginInvoke(Action)"/>.
/// </summary>
public sealed class DispatcherOperation
{
    private volatile DispatcherOperationStatus _status;
    internal Action? Action;

    internal DispatcherOperation(Action action)
    {
        Action = action;
        _status = DispatcherOperationStatus.Pending;
    }

    /// <summary>
    /// Gets the current status of this operation.
    /// </summary>
    public DispatcherOperationStatus Status => _status;

    /// <summary>
    /// Attempts to abort the operation. Returns <see langword="true"/> if the operation
    /// was successfully cancelled before execution; otherwise <see langword="false"/>.
    /// </summary>
    public bool Abort()
    {
        if (_status != DispatcherOperationStatus.Pending)
        {
            return false;
        }

        _status = DispatcherOperationStatus.Aborted;
        Action = null;
        return true;
    }

    internal void MarkExecuting() => _status = DispatcherOperationStatus.Executing;
    internal void MarkCompleted() => _status = DispatcherOperationStatus.Completed;
}

/// <summary>
/// UI-thread dispatcher used to schedule work items and timers.
/// Platform hosts typically create one dispatcher per window handle.
/// </summary>
public interface IDispatcher
{
    /// <summary>
    /// Gets whether the caller is currently running on the UI thread.
    /// </summary>
    bool IsOnUIThread { get; }

    /// <summary>
    /// Asynchronously executes an action on the UI thread at the default priority.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <returns>A handle that can be used to abort the operation.</returns>
    DispatcherOperation BeginInvoke(Action action);

    /// <summary>
    /// Asynchronously executes an action on the UI thread at the specified priority.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    /// <param name="priority">Work item priority.</param>
    /// <returns>A handle that can be used to abort the operation.</returns>
    DispatcherOperation BeginInvoke(Action action, DispatcherPriority priority);

    /// <summary>
    /// Executes an action synchronously on the UI thread.
    /// If called from the UI thread, the action runs immediately.
    /// If called from another thread, blocks until the action completes.
    /// </summary>
    /// <param name="action">The action to execute.</param>
    void Invoke(Action action);

}

/// <summary>
/// Internal dispatcher operations used by the framework infrastructure.
/// </summary>
internal interface IDispatcherCore
{
    /// <summary>
    /// Posts an action to the UI thread, merging duplicates by <paramref name="mergeKey"/>.
    /// If an item with the same key is already pending, the action is not enqueued.
    /// </summary>
    bool PostMerged(DispatcherMergeKey mergeKey, Action action, DispatcherPriority priority);

    /// <summary>
    /// Processes a batch of queued work items.
    /// </summary>
    void ProcessWorkItems();

    /// <summary>
    /// Schedules an action to run on the UI thread after <paramref name="dueTime"/>.
    /// </summary>
    /// <param name="dueTime">Time to wait before running the action.</param>
    /// <param name="action">The action to execute.</param>
    /// <returns>A handle that cancels the scheduled action when disposed.</returns>
    IDisposable Schedule(TimeSpan dueTime, Action action);
}
