using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// Locks the close coordinator's phase-gate invariants (subplan 02, step 3): each teardown step runs
/// once and in order, regardless of how many times a backend drives it. The real Win32/X11/macOS
/// destroy handlers call these in the same sequence; these tests exercise the core gate directly.
/// </summary>
[TestClass]
public sealed class WindowClosePhaseTests
{
    [TestMethod]
    public void RaiseClosed_CalledTwice_FiresClosedOnce()
    {
        var window = new Window();
        int closedCount = 0;
        window.Closed += () => closedCount++;

        window.RaiseClosed();
        window.RaiseClosed();

        Assert.AreEqual(1, closedCount);
    }

    [TestMethod]
    public void DisposeVisualTree_CalledTwice_DisposesContentOnce()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Headless window uses the Windows-only GDI factory.");
            return;
        }

        var window = HeadlessWindow.Create();
        var content = new DisposeCountingContent();
        window.Content = content;
        window.PerformLayout();

        window.RaiseClosed();
        window.DisposeVisualTree();
        window.DisposeVisualTree();

        Assert.AreEqual(1, content.DisposeCount);
    }

    [TestMethod]
    public void FullCloseSequence_DrivenTwice_RunsEachStepOnce()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Headless window uses the Windows-only GDI factory.");
            return;
        }

        var window = HeadlessWindow.Create();
        var content = new DisposeCountingContent();
        window.Content = content;
        window.PerformLayout();
        int closedCount = 0;
        window.Closed += () => closedCount++;

        // A backend that delivered its destroy notifications twice (e.g. Win32's WM_CLOSE then an
        // out-of-band WM_DESTROY) must still tear down exactly once.
        for (int pass = 0; pass < 2; pass++)
        {
            window.RaiseClosed();
            window.DisposeVisualTree();
        }

        Assert.AreEqual(1, closedCount);
        Assert.AreEqual(1, content.DisposeCount);
    }

    private sealed class DisposeCountingContent : ContentControl, IDisposable
    {
        public int DisposeCount { get; private set; }

        public void Dispose() => DisposeCount++;
    }
}
