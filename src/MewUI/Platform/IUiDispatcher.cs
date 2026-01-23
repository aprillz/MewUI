namespace Aprillz.MewUI
{
    public enum UiDispatcherPriority
    {
        Input = 0,
        Layout = 1,
        Render = 2,
        Background = 3,
        Idle = 4,
    }
}

namespace Aprillz.MewUI.Platform
{
    public sealed class DispatcherMergeKey
    {
        public UiDispatcherPriority Priority { get; }

        internal DispatcherMergeKey(UiDispatcherPriority priority)
        {
            Priority = priority;
        }

        public override string ToString() => $"DispatcherMergeKey({Priority})";
    }

    public interface IUiDispatcher
    {
        bool IsOnUIThread { get; }

        void Post(Action action);

        void Post(Action action, UiDispatcherPriority priority);

        /// <summary>
        /// Posts an action to the UI thread, merging duplicates by <paramref name="mergeKey"/>.
        /// If an item with the same key is already pending, the action is not enqueued.
        /// </summary>
        bool PostMerged(DispatcherMergeKey mergeKey, Action action, UiDispatcherPriority priority);

        void Send(Action action);

        /// <summary>
        /// Schedules an action to run on the UI thread after <paramref name="dueTime"/>.
        /// </summary>
        IDisposable Schedule(TimeSpan dueTime, Action action);

        void ProcessWorkItems();
    }
}