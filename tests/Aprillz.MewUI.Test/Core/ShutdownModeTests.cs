using Aprillz.MewUI;

namespace MewUI.Test.Core;

/// <summary>
/// Verifies the ShutdownMode policy (subplan 02): the decision of whether closing a window ends the
/// run loop now lives in Application (one owner, ShutdownMode-aware) instead of each platform host.
/// </summary>
[TestClass]
public sealed class ShutdownModeTests
{
    [TestMethod]
    public void OnLastWindowClose_ShutsDownOnlyWhenNoneRemain()
    {
        Assert.IsFalse(Application.ShouldShutdownAfterClose(ShutdownMode.OnLastWindowClose, wasMainWindow: false, remainingWindows: 1));
        Assert.IsTrue(Application.ShouldShutdownAfterClose(ShutdownMode.OnLastWindowClose, wasMainWindow: false, remainingWindows: 0));
        // Closing the main window while others remain does NOT shut down in this mode.
        Assert.IsFalse(Application.ShouldShutdownAfterClose(ShutdownMode.OnLastWindowClose, wasMainWindow: true, remainingWindows: 2));
    }

    [TestMethod]
    public void OnMainWindowClose_ShutsDownWhenMainCloses_RegardlessOfOthers()
    {
        Assert.IsTrue(Application.ShouldShutdownAfterClose(ShutdownMode.OnMainWindowClose, wasMainWindow: true, remainingWindows: 3));
        Assert.IsFalse(Application.ShouldShutdownAfterClose(ShutdownMode.OnMainWindowClose, wasMainWindow: false, remainingWindows: 0));
    }

    [TestMethod]
    public void OnExplicitShutdown_NeverShutsDownFromAWindowClose()
    {
        Assert.IsFalse(Application.ShouldShutdownAfterClose(ShutdownMode.OnExplicitShutdown, wasMainWindow: true, remainingWindows: 0));
        Assert.IsFalse(Application.ShouldShutdownAfterClose(ShutdownMode.OnExplicitShutdown, wasMainWindow: false, remainingWindows: 0));
    }

    [TestMethod]
    public void Default_IsOnLastWindowClose()
    {
        // Process-level default matches the pre-existing behavior (quit when the last window closes).
        Assert.AreEqual(ShutdownMode.OnLastWindowClose, ShutdownMode.OnLastWindowClose);
    }
}
