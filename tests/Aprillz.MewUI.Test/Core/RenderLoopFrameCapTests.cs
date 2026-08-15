using Aprillz.MewUI;

namespace MewUI.Test.Core;

/// <summary>
/// Covers the frame cap the render loop holds itself to: an explicit target always wins, turning VSync
/// off is the one request that leaves the loop unlimited, and everything else follows the screen.
/// </summary>
[TestClass]
public sealed class RenderLoopFrameCapTests
{
    [TestMethod]
    public void ByDefault_TheLoopFollowsTheScreen()
    {
        var settings = new RenderLoopSettings();

        Assert.IsTrue(settings.VSyncEnabled);
        Assert.AreEqual(144, settings.EffectiveFrameCap(displayRefreshHz: 144));
        Assert.AreEqual(30, settings.EffectiveFrameCap(displayRefreshHz: 30));
    }

    [TestMethod]
    public void UnreportedRefreshRate_KeepsTheFixedFallback()
    {
        var settings = new RenderLoopSettings();

        Assert.AreEqual(
            RenderLoopSettings.VSyncFallbackFps,
            settings.EffectiveFrameCap(displayRefreshHz: 0));
    }

    [TestMethod]
    public void VSyncTurnedOff_MeansUncapped()
    {
        var settings = new RenderLoopSettings { VSyncEnabled = false };

        // Turning VSync off is the explicit request for an unlimited loop, so nothing substitutes a cap.
        Assert.AreEqual(0, settings.EffectiveFrameCap());
        Assert.AreEqual(0, settings.EffectiveFrameCap(displayRefreshHz: 144));
    }

    [TestMethod]
    public void TargetFps_WinsOverEveryFallback()
    {
        var settings = new RenderLoopSettings { TargetFps = 30 };

        Assert.AreEqual(30, settings.EffectiveFrameCap());
        Assert.AreEqual(30, settings.EffectiveFrameCap(displayRefreshHz: 144));

        settings.VSyncEnabled = false;
        Assert.AreEqual(30, settings.EffectiveFrameCap(displayRefreshHz: 144));
    }
}
