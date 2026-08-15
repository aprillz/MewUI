using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Core;

/// <summary>
/// What makes the render loop draw a frame. A clock attached to an element does not select that
/// element's window: its tick invalidates what it animates, and the invalidation is the render's only
/// reason. Clocks with no owner keep the application-wide demand they have always had.
/// </summary>
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

    [TestMethod]
    public void OwnedClock_DoesNotSelectItsWindowByItself()
    {
        var window = new Window();
        var owner = new Border();
        window.Content = owner;

        var clock = new AnimationClock(TimeSpan.FromSeconds(10))
            .AttachTo(owner);
        var settings = new RenderLoopSettings();

        clock.Start();
        using var pulse = AnimationManager.Instance.BeginPulse(settings);

        Assert.IsFalse(pulse.HasApplicationRenderDemand);
        Assert.IsFalse(pulse.ShouldRender(window, needsRender: false));
        Assert.IsTrue(pulse.ShouldRender(window, needsRender: true));

        clock.Stop();
    }

    [TestMethod]
    public void UnownedPublicClock_RetainsApplicationWidePulseDemand()
    {
        var clock = new AnimationClock(TimeSpan.FromSeconds(10));
        var settings = new RenderLoopSettings();

        clock.Start();
        using var pulse = AnimationManager.Instance.BeginPulse(settings);

        Assert.IsTrue(pulse.HasApplicationRenderDemand);
        Assert.IsTrue(pulse.ShouldRender(new Window(), needsRender: false));

        clock.Stop();
    }

    [TestMethod]
    public void Pulse_CentralizesApplicationWideRenderPolicy()
    {
        var window = new Window();
        var settings = new RenderLoopSettings();

        using (var pulse = AnimationManager.Instance.BeginPulse(settings))
        {
            Assert.IsFalse(pulse.ShouldRender(window, needsRender: false));
            Assert.IsTrue(pulse.ShouldRender(window, needsRender: true));

            settings.Continuous = true;
            Assert.IsFalse(pulse.ShouldRender(window, needsRender: false),
                "Render policy must remain stable for the lifetime of one pulse.");
        }

        using (var continuousPulse = AnimationManager.Instance.BeginPulse(settings))
        {
            Assert.IsTrue(continuousPulse.ShouldRender(window, needsRender: false));
        }

        settings.Continuous = false;
        settings.VSyncEnabled = false;
        using (var vsyncOffPulse = AnimationManager.Instance.BeginPulse(settings))
        {
            Assert.IsTrue(vsyncOffPulse.ShouldRender(window, needsRender: false));
        }
    }
}
