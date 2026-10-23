using Aprillz.MewUI;
using Aprillz.MewUI.Headless;

namespace MewUI.Headless.Test;

/// <summary>Gate 4: a timer on the virtual clock fires only once the clock reaches its interval.</summary>
[TestClass]
public sealed class Gate4VirtualTimeTests
{
    [TestMethod]
    public void AdvanceTime_FiresDispatcherTimerAtItsInterval()
    {
        var session = HeadlessTestHost.Shared;
        int ticks = 0;
        var timer = session.Run(() =>
        {
            var created = new DispatcherTimer(TimeSpan.FromMilliseconds(300));
            created.Tick += () => ticks++;
            created.Start();
            return created;
        });
        try
        {
            session.AdvanceTime(TimeSpan.FromMilliseconds(299));
            Assert.AreEqual(0, ticks, "the timer fired before its interval elapsed");

            session.AdvanceTime(TimeSpan.FromMilliseconds(1));
            Assert.AreEqual(1, ticks, "the timer did not fire when its interval elapsed");
        }
        finally
        {
            session.Run(timer.Stop);
        }
    }

    [TestMethod]
    public void AdvanceTime_RunsEveryIntervalItCrosses()
    {
        var session = HeadlessTestHost.Shared;
        int ticks = 0;
        var timer = session.Run(() =>
        {
            var created = new DispatcherTimer(TimeSpan.FromMilliseconds(100));
            created.Tick += () => ticks++;
            created.Start();
            return created;
        });
        try
        {
            session.AdvanceTime(TimeSpan.FromMilliseconds(350));

            Assert.AreEqual(3, ticks);
        }
        finally
        {
            session.Run(timer.Stop);
        }
    }

    [TestMethod]
    public void RunJobs_DoesNotMoveTheVirtualClock()
    {
        var session = HeadlessTestHost.Shared;
        int ticks = 0;
        var timer = session.Run(() =>
        {
            var created = new DispatcherTimer(TimeSpan.FromMilliseconds(1));
            created.Tick += () => ticks++;
            created.Start();
            return created;
        });
        try
        {
            Thread.Sleep(20);
            session.RunJobs();

            Assert.AreEqual(0, ticks);
        }
        finally
        {
            session.Run(timer.Stop);
        }
    }
}
