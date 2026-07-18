using Aprillz.MewUI;
using Aprillz.MewUI.Platform;

namespace MewUI.Test.Core;

[TestClass]
public sealed class ManagedUiDispatcherTests
{
    [TestMethod]
    public void BeginInvoke_RunsOnProcessWorkItems()
    {
        var dispatcher = new TestDispatcher();
        bool ran = false;
        dispatcher.BeginInvoke(() => ran = true);

        Assert.IsTrue(dispatcher.HasPendingWork);
        dispatcher.ProcessWorkItems();

        Assert.IsTrue(ran);
        Assert.IsFalse(dispatcher.HasPendingWork);
    }

    [TestMethod]
    public void ZeroDelayTimer_IsDueAndRuns()
    {
        var dispatcher = new TestDispatcher();
        bool ran = false;
        dispatcher.Schedule(TimeSpan.Zero, () => ran = true);

        Assert.IsTrue(dispatcher.HasPendingWork);
        dispatcher.ProcessWorkItems();

        Assert.IsTrue(ran);
    }

    [TestMethod]
    public void CanceledTimer_DoesNotRun()
    {
        var dispatcher = new TestDispatcher();
        bool ran = false;
        IDisposable handle = dispatcher.Schedule(TimeSpan.Zero, () => ran = true);
        handle.Dispose();

        dispatcher.ProcessWorkItems();

        Assert.IsFalse(ran);
    }

    [TestMethod]
    public void NoTimers_PollTimeoutUsesPolicy()
    {
        var dispatcher = new TestDispatcher();

        // TestDispatcher's NoTimerPollTimeout returns maxMs unchanged.
        Assert.AreEqual(500, dispatcher.GetPollTimeoutMs(500));
    }

    [TestMethod]
    public void FutureTimer_IsNotPendingAndBoundsPoll()
    {
        var dispatcher = new TestDispatcher();
        dispatcher.Schedule(TimeSpan.FromSeconds(10), static () => { });

        Assert.IsFalse(dispatcher.HasPendingWork);

        int timeout = dispatcher.GetPollTimeoutMs(500);
        Assert.IsTrue(timeout > 0 && timeout <= 500, $"Expected a bounded poll timeout, got {timeout}.");
    }

    [TestMethod]
    public void Wake_FiresOncePerDrainCycle()
    {
        var dispatcher = new TestDispatcher();
        int wakes = 0;
        dispatcher.SetWake(() => wakes++);

        dispatcher.BeginInvoke(static () => { });
        dispatcher.BeginInvoke(static () => { });
        Assert.AreEqual(1, wakes, "Coalesced posts should request a single wake until cleared.");

        dispatcher.ClearWakeRequest();
        dispatcher.BeginInvoke(static () => { });
        Assert.AreEqual(2, wakes);
    }

    private sealed class TestDispatcher : ManagedUiDispatcher
    {
        protected override int MaxPumpIterations => 8;

        protected override int NoTimerPollTimeout(int maxMs) => maxMs;

        protected override void DispatchDueTimer(Action action) => action();
    }
}
