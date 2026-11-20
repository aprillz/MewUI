using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Core;

/// <summary>
/// A window the framework keeps for its own use (a tray menu's host) hears run-wide broadcasts but is not one of the
/// application's windows: it is not listed, not closed by a system quit, and never ends the run.
/// </summary>
[TestClass]
public sealed class FrameworkInternalWindowTests
{
    [TestMethod]
    public void AMenuHost_IsNotOneOfTheApplicationsWindows()
    {
        using var runtime = new ApplicationRuntime(static _ => { });
        var userWindow = new Window();
        var menuHost = new Window { Kind = WindowKind.MenuHost };

        runtime.Register(userWindow);
        runtime.Register(menuHost);

        CollectionAssert.AreEqual(new[] { userWindow }, runtime.Windows.ToArray());
        CollectionAssert.AreEqual(new[] { userWindow }, runtime.SnapshotWindows());
        CollectionAssert.AreEqual(new[] { userWindow, menuHost }, runtime.SnapshotAllWindows(), "the host misses run-wide broadcasts");
    }

    [TestMethod]
    public void ClosingAMenuHost_NeverEndsTheRun()
    {
        using var runtime = new ApplicationRuntime(static _ => { });
        var menuHost = new Window { Kind = WindowKind.MenuHost };
        runtime.Register(menuHost);

        Assert.IsFalse(runtime.Unregister(menuHost, ShutdownMode.OnLastWindowClose));
        Assert.AreEqual(0, runtime.SnapshotAllWindows().Length);
    }
}
