using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.Animation;

namespace MewUI.Test.Core;

/// <summary>
/// Covers what makes the render loop pulse for animations: the loop pulls the active-clock state each
/// frame, and a clock times from its first pulse rather than from Start.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class AnimationLoopActivationTests
{
    [TestCleanup]
    public void Cleanup() => AnimationManager.Reset();

    [TestMethod]
    public void ClockStartedBeforeARun_MakesTheLoopContinuous()
    {
        AnimationManager.Reset();
        var clock = new AnimationClock(TimeSpan.FromSeconds(10));

        // No application exists yet, which is the ordinary case for a tree built before Run.
        Assert.IsFalse(Application.IsRunning);
        clock.Start();

        var settings = new RenderLoopSettings();
        Assert.IsTrue(settings.AnimationActive);
        Assert.IsTrue(settings.IsContinuous);

        clock.Stop();
        Assert.IsFalse(settings.AnimationActive);
        Assert.IsFalse(settings.IsContinuous);
    }

    [TestMethod]
    public void PauseAndResume_TrackThroughTheSameSettingsInstance()
    {
        AnimationManager.Reset();
        var clock = new AnimationClock(TimeSpan.FromSeconds(10));
        var settings = new RenderLoopSettings();

        clock.Start();
        Assert.IsTrue(settings.AnimationActive);

        clock.Pause();
        Assert.IsFalse(settings.AnimationActive);

        clock.Resume();
        Assert.IsTrue(settings.AnimationActive);
    }

    [TestMethod]
    public void FirstUpdate_RebasesTimingSoADelayedPulseStartsAtZero()
    {
        AnimationManager.Reset();
        var duration = TimeSpan.FromMilliseconds(1000);
        var clock = new AnimationClock(duration);

        clock.Start();

        // A pulse arriving a full duration after Start would otherwise report the animation as finished.
        long lateFirstPulse = Stopwatch.GetTimestamp() + Stopwatch.Frequency;
        clock.Update(lateFirstPulse);

        Assert.IsTrue(clock.IsRunning);
        Assert.AreEqual(0.0, clock.RawProgress, 0.01);

        long quarterIn = lateFirstPulse + (Stopwatch.Frequency / 4);
        clock.Update(quarterIn);

        Assert.AreEqual(0.25, clock.RawProgress, 0.02);
    }

    [TestMethod]
    public void RestartAfterAnUpdate_RebasesAgain()
    {
        AnimationManager.Reset();
        var clock = new AnimationClock(TimeSpan.FromMilliseconds(1000));

        clock.Start();
        long first = Stopwatch.GetTimestamp();
        clock.Update(first);
        clock.Update(first + (Stopwatch.Frequency / 2));
        Assert.AreEqual(0.5, clock.RawProgress, 0.02);

        clock.Stop();
        clock.Start();
        clock.Update(first + Stopwatch.Frequency * 5);

        Assert.AreEqual(0.0, clock.RawProgress, 0.01);
    }
}
