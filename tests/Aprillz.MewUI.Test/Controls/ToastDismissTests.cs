using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

/// <summary>
/// A toast leaves through the same transition it arrived by. The presenter is taken off the overlay when
/// it goes idle, so idle has to mean "the run out has played", not "the content was dropped".
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class ToastDismissTests
{
    [TestMethod]
    public void TheContentLeavesThroughItsTransitionBeforeTheToastGoesIdle()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var presenter = new ToastPresenter();
        int idle = 0;
        presenter.BecameIdle += () => idle++;

        var window = HeadlessWindow.Create();
        window.Content = presenter;
        window.PerformLayout();

        presenter.Show("Saved", TimeSpan.FromMilliseconds(10));
        window.PerformLayout();

        presenter.Hide();
        Assert.AreEqual(0, idle, "the toast went idle before its exit could play");

        // The exit is a 300 ms transition; stepping past it is what finishes the run.
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        long frame = System.Diagnostics.Stopwatch.Frequency / 60;
        for (int step = 0; step <= 30; step++)
        {
            Aprillz.MewUI.Animation.AnimationManager.Instance.UpdateAt(start + (frame * step));
        }

        Assert.AreEqual(1, idle, "the toast never reported itself idle after the exit");

        window.Close();
    }
}
