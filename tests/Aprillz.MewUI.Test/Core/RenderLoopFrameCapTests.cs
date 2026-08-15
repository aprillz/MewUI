using Aprillz.MewUI;

namespace MewUI.Test.Core;

/// <summary>
/// Covers the frame cap the render loop holds itself to: an explicit target always wins, and a backend
/// that cannot pace on the display refresh gets a fallback instead of running as fast as the CPU allows.
/// </summary>
[TestClass]
public sealed class RenderLoopFrameCapTests
{
    [TestMethod]
    public void BackendThatPacesOnRefresh_LeavesPacingToThePresent()
    {
        var settings = new RenderLoopSettings();

        Assert.AreEqual(0, settings.EffectiveFrameCap(backendSupportsVSync: true));
    }

    [TestMethod]
    public void BackendWithoutVSync_FallsBackToACap()
    {
        var settings = new RenderLoopSettings();

        Assert.IsTrue(settings.VSyncEnabled);
        Assert.AreEqual(RenderLoopSettings.VSyncFallbackFps, settings.EffectiveFrameCap(backendSupportsVSync: false));
    }

    [TestMethod]
    public void BackendWithoutVSync_HoldsTheScreensRefreshRate()
    {
        var settings = new RenderLoopSettings();

        // A faster screen must not be capped down to the fixed fallback.
        Assert.AreEqual(144, settings.EffectiveFrameCap(backendSupportsVSync: false, displayRefreshHz: 144));
        Assert.AreEqual(30, settings.EffectiveFrameCap(backendSupportsVSync: false, displayRefreshHz: 30));
    }

    [TestMethod]
    public void RefreshRate_DoesNotCapABackendThatPacesItself()
    {
        var settings = new RenderLoopSettings();

        Assert.AreEqual(0, settings.EffectiveFrameCap(backendSupportsVSync: true, displayRefreshHz: 144));
    }

    [TestMethod]
    public void UnreportedRefreshRate_KeepsTheFixedFallback()
    {
        var settings = new RenderLoopSettings();

        Assert.AreEqual(
            RenderLoopSettings.VSyncFallbackFps,
            settings.EffectiveFrameCap(backendSupportsVSync: false, displayRefreshHz: 0));
    }

    [TestMethod]
    public void VSyncTurnedOff_MeansUncappedEvenWithoutBackendSupport()
    {
        var settings = new RenderLoopSettings { VSyncEnabled = false };

        // Turning VSync off is the explicit request for an unlimited loop, so nothing substitutes a cap.
        Assert.AreEqual(0, settings.EffectiveFrameCap(backendSupportsVSync: false));
        Assert.AreEqual(0, settings.EffectiveFrameCap(backendSupportsVSync: true));
    }

    [TestMethod]
    public void TargetFps_WinsOverEveryFallback()
    {
        var settings = new RenderLoopSettings { TargetFps = 30 };

        Assert.AreEqual(30, settings.EffectiveFrameCap(backendSupportsVSync: true));
        Assert.AreEqual(30, settings.EffectiveFrameCap(backendSupportsVSync: false));

        settings.VSyncEnabled = false;
        Assert.AreEqual(30, settings.EffectiveFrameCap(backendSupportsVSync: false));
    }
}
