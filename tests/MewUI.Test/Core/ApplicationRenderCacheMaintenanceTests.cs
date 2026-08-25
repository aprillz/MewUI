using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Core;

[TestClass]
public sealed class ApplicationRenderCacheMaintenanceTests
{
    [TestMethod]
    public void RuntimeSchedulesIdleMaintenanceAndTrimsOnShutdown()
    {
        var modes = new List<RenderCacheMaintenanceMode>();
        var dispatcher = new TestDispatcher();
        var runtime = new ApplicationRuntime(modes.Add);

        runtime.StartRenderCacheMaintenance(dispatcher);

        Assert.AreEqual(1, dispatcher.PendingCount);
        dispatcher.RunNext();
        CollectionAssert.AreEqual(
            new[] { RenderCacheMaintenanceMode.Idle },
            modes);
        Assert.AreEqual(1, dispatcher.PendingCount);

        runtime.Dispose();

        CollectionAssert.AreEqual(
            new[]
            {
                RenderCacheMaintenanceMode.Idle,
                RenderCacheMaintenanceMode.Shutdown,
            },
            modes);
        Assert.AreEqual(0, dispatcher.PendingCount);

        runtime.Dispose();
        Assert.AreEqual(2, modes.Count);
    }

    [TestMethod]
    public void IdleMaintenanceFailure_DoesNotStopTheScheduler()
    {
        int calls = 0;
        var dispatcher = new TestDispatcher();
        var runtime = new ApplicationRuntime(mode =>
        {
            if (mode == RenderCacheMaintenanceMode.Idle && ++calls == 1)
            {
                throw new InvalidOperationException("probe");
            }
        });

        runtime.StartRenderCacheMaintenance(dispatcher);

        Assert.Throws<InvalidOperationException>(() => dispatcher.RunNext());
        Assert.AreEqual(1, dispatcher.PendingCount);
        dispatcher.RunNext();
        Assert.AreEqual(2, calls);

        runtime.Dispose();
        Assert.AreEqual(0, dispatcher.PendingCount);
    }

    private sealed class TestDispatcher : IDispatcher, IDispatcherCore
    {
        private readonly Queue<ScheduledItem> _scheduled = new();

        public bool IsOnUIThread => true;

        public int PendingCount => _scheduled.Count(static item => !item.IsCanceled);

        public DispatcherOperation BeginInvoke(Action action) =>
            throw new NotSupportedException();

        public DispatcherOperation BeginInvoke(DispatcherPriority priority, Action action) =>
            throw new NotSupportedException();

        public void Invoke(Action action) => action();

        public bool PostMerged(
            DispatcherMergeKey mergeKey,
            Action action,
            DispatcherPriority priority) => throw new NotSupportedException();

        public void ProcessWorkItems() => throw new NotSupportedException();

        public IDisposable Schedule(TimeSpan dueTime, Action action)
        {
            Assert.IsGreaterThan(TimeSpan.Zero, dueTime);
            var item = new ScheduledItem(action);
            _scheduled.Enqueue(item);
            return item;
        }

        public void RunNext()
        {
            while (_scheduled.Count != 0)
            {
                var item = _scheduled.Dequeue();
                if (!item.IsCanceled)
                {
                    item.Action();
                    return;
                }
            }
            Assert.Fail("No scheduled maintenance callback was available.");
        }

        private sealed class ScheduledItem(Action action) : IDisposable
        {
            public Action Action { get; } = action;

            public bool IsCanceled { get; private set; }

            public void Dispose() => IsCanceled = true;
        }
    }
}
