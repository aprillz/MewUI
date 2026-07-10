using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class WindowCloseAsyncTests
{
    [TestMethod]
    public async Task CloseAsync_WithoutHandlers_ClosesAndReturnsTrue()
    {
        var window = new Window();
        bool closedRaised = false;
        window.Closed += () => closedRaised = true;

        bool closed = await window.CloseAsync();

        Assert.IsTrue(closed);
        Assert.IsTrue(closedRaised);
    }

    [TestMethod]
    public async Task CloseAsync_ClosingCancels_ReturnsFalseAndKeepsWindow()
    {
        var window = new Window();
        bool closedRaised = false;
        window.Closed += () => closedRaised = true;
        window.Closing += args => args.Cancel = true;

        bool closed = await window.CloseAsync();

        Assert.IsFalse(closed);
        Assert.IsFalse(closedRaised);
    }

    [TestMethod]
    public async Task CloseAsync_AlreadyClosed_CompletesTrue()
    {
        var window = new Window();
        window.Close();

        bool closed = await window.CloseAsync();

        Assert.IsTrue(closed);
    }

    [TestMethod]
    public async Task CloseAsync_DeferralAllows_ClosesAfterComplete()
    {
        var window = new Window();
        int closingCount = 0;
        bool closedRaised = false;
        window.Closed += () => closedRaised = true;
        ClosingDeferral? deferral = null;
        window.Closing += args =>
        {
            closingCount++;
            deferral = args.GetDeferral();
        };

        var closeTask = window.CloseAsync();

        Assert.IsFalse(closeTask.IsCompleted);
        Assert.IsFalse(closedRaised);

        deferral!.Complete();

        Assert.IsTrue(await closeTask);
        Assert.IsTrue(closedRaised);
        Assert.AreEqual(1, closingCount);
    }

    [TestMethod]
    public async Task CloseAsync_DeferralCancels_ReturnsFalseAndKeepsWindow()
    {
        var window = new Window();
        bool closedRaised = false;
        window.Closed += () => closedRaised = true;
        ClosingEventArgs? captured = null;
        ClosingDeferral? deferral = null;
        window.Closing += args =>
        {
            captured = args;
            deferral = args.GetDeferral();
        };

        var closeTask = window.CloseAsync();
        captured!.Cancel = true;
        deferral!.Complete();

        Assert.IsFalse(await closeTask);
        Assert.IsFalse(closedRaised);
    }

    [TestMethod]
    public async Task CloseAsync_MultipleDeferrals_AnyCancelWins()
    {
        var window = new Window();
        bool closedRaised = false;
        window.Closed += () => closedRaised = true;
        ClosingEventArgs? captured = null;
        ClosingDeferral? first = null;
        ClosingDeferral? second = null;
        window.Closing += args =>
        {
            captured = args;
            first = args.GetDeferral();
            second = args.GetDeferral();
        };

        var closeTask = window.CloseAsync();

        first!.Complete();
        Assert.IsFalse(closeTask.IsCompleted);

        captured!.Cancel = true;
        second!.Complete();

        Assert.IsFalse(await closeTask);
        Assert.IsFalse(closedRaised);
    }

    [TestMethod]
    public async Task Close_WhileDecisionPending_JoinsWithoutSecondClosingRound()
    {
        var window = new Window();
        int closingCount = 0;
        ClosingDeferral? deferral = null;
        window.Closing += args =>
        {
            closingCount++;
            deferral ??= args.GetDeferral();
        };

        var closeTask = window.CloseAsync();
        window.Close();
        var joinedTask = window.CloseAsync();

        Assert.AreEqual(1, closingCount);

        deferral!.Complete();

        Assert.IsTrue(await closeTask);
        Assert.IsTrue(await joinedTask);
        Assert.AreEqual(1, closingCount);
    }

    [TestMethod]
    public async Task Deferral_CompletedInsideHandler_DegradesToSyncDecision()
    {
        var window = new Window();
        window.Closing += args =>
        {
            using var deferral = args.GetDeferral();
            args.Cancel = true;
        };

        Assert.IsFalse(await window.CloseAsync());
    }

    [TestMethod]
    public async Task Deferral_CompleteIsIdempotent()
    {
        var window = new Window();
        ClosingDeferral? deferral = null;
        window.Closing += args => deferral = args.GetDeferral();

        var closeTask = window.CloseAsync();
        deferral!.Complete();
        deferral.Complete();
        deferral.Dispose();

        Assert.IsTrue(await closeTask);
    }

    [TestMethod]
    public async Task CloseAsync_AfterCancelledClose_RunsClosingAgain()
    {
        var window = new Window();
        int closingCount = 0;
        bool cancelNext = true;
        window.Closing += args =>
        {
            closingCount++;
            args.Cancel = cancelNext;
        };

        Assert.IsFalse(await window.CloseAsync());

        cancelNext = false;

        Assert.IsTrue(await window.CloseAsync());
        Assert.AreEqual(2, closingCount);
    }
}
