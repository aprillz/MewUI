using Aprillz.MewUI.Animation;

namespace MewUI.Test.Core;

[TestClass]
[DoNotParallelize]
public sealed class AnimationRenderDemandTests
{
    [TestCleanup]
    public void Cleanup() => AnimationManager.Reset();

    [TestMethod]
    public void PausedClock_DoesNotKeepContinuousRenderDemandAlive()
    {
        AnimationManager.Reset();
        var clock = new AnimationClock(TimeSpan.FromSeconds(10));

        clock.Start();
        Assert.IsTrue(AnimationManager.Instance.HasRenderDemand);

        clock.Pause();
        Assert.IsFalse(AnimationManager.Instance.HasRenderDemand);

        clock.Resume();
        Assert.IsTrue(AnimationManager.Instance.HasRenderDemand);

        clock.Stop();
        Assert.IsFalse(AnimationManager.Instance.HasRenderDemand);
    }
}
