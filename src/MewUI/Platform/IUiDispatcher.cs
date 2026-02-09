namespace Aprillz.MewUI
{
    /// <summary>
    /// Defines the relative priority used by <see cref="Platform.IUiDispatcher"/> when ordering work items.
    /// Lower values run first.
    /// </summary>
    public enum UiDispatcherPriority
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
}

namespace Aprillz.MewUI.Platform
{
    /// <summary>
    /// Identifies a merge bucket for <see cref="IUiDispatcher.PostMerged"/>.
    /// </summary>
    public sealed class DispatcherMergeKey
    {
        /// <summary>
        /// Gets the associated priority for this merge key (diagnostics only).
        /// </summary>
        public UiDispatcherPriority Priority { get; }

        internal DispatcherMergeKey(UiDispatcherPriority priority)
        {
            Priority = priority;
        }

        public override string ToString() => $"DispatcherMergeKey({Priority})";
    }

    /// <summary>
    /// UI-thread dispatcher used to schedule work items and timers.
    /// Platform hosts typically create one dispatcher per window handle.
    /// </summary>
    public interface IUiDispatcher
    {
        /// <summary>
        /// Gets whether the caller is currently running on the UI thread.
        /// </summary>
        bool IsOnUIThread { get; }

        /// <summary>
        /// Posts an action to be executed on the UI thread at the default priority.
        /// </summary>
        void Post(Action action); 

        /// <summary>
        /// Posts an action to be executed on the UI thread at the specified priority.
        /// </summary>
        /// <param name="action">The action to execute.</param>
        /// <param name="priority">Work item priority.</param>
        void Post(Action action, UiDispatcherPriority priority);

        /// <summary>
        /// Posts an action to the UI thread, merging duplicates by <paramref name="mergeKey"/>.
        /// If an item with the same key is already pending, the action is not enqueued.
        /// </summary>
        /// <param name="mergeKey">Merge key that deduplicates pending work items.</param>
        /// <param name="action">The action to execute.</param>
        /// <param name="priority">Work item priority.</param>
        /// <returns><see langword="true"/> if the action was enqueued; otherwise, <see langword="false"/>.</returns>
        bool PostMerged(DispatcherMergeKey mergeKey, Action action, UiDispatcherPriority priority);

        /// <summary>
        /// Executes an action synchronously on the UI thread.
        /// </summary>
        void Send(Action action);

        /// <summary>
        /// Schedules an action to run on the UI thread after <paramref name="dueTime"/>.
        /// </summary>
        /// <param name="dueTime">Time to wait before running the action.</param>
        /// <param name="action">The action to execute.</param>
        /// <returns>A handle that cancels the scheduled action when disposed.</returns>
        IDisposable Schedule(TimeSpan dueTime, Action action);

        /// <summary>
        /// Processes a batch of queued work items.
        /// </summary>
        void ProcessWorkItems();
    }
}
