using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.Test.Controls;

/// <summary>
/// Covers the placement fallback backends resolve through: a CenterOwner window with no realized owner
/// centers on the screen instead of landing on a backend default position.
/// </summary>
[TestClass]
public sealed class WindowStartupLocationTests
{
    [TestMethod]
    public void CenterOwner_WithoutRealizedOwner_FallsBackToCenterScreen()
    {
        Assert.AreEqual(
            WindowStartupLocation.CenterScreen,
            Window.ResolveEffectiveStartupLocation(WindowStartupLocation.CenterOwner, hasRealizedOwner: false));
    }

    [TestMethod]
    public void CenterOwner_WithRealizedOwner_KeepsCenterOwner()
    {
        Assert.AreEqual(
            WindowStartupLocation.CenterOwner,
            Window.ResolveEffectiveStartupLocation(WindowStartupLocation.CenterOwner, hasRealizedOwner: true));
    }

    [TestMethod]
    public void ManualAndCenterScreen_IgnoreTheOwner()
    {
        foreach (bool hasRealizedOwner in (bool[])[false, true])
        {
            Assert.AreEqual(
                WindowStartupLocation.Manual,
                Window.ResolveEffectiveStartupLocation(WindowStartupLocation.Manual, hasRealizedOwner));
            Assert.AreEqual(
                WindowStartupLocation.CenterScreen,
                Window.ResolveEffectiveStartupLocation(WindowStartupLocation.CenterScreen, hasRealizedOwner));
        }
    }

    [TestMethod]
    public void OwnerlessWindow_ResolvesThroughTheFallback()
    {
        var window = new Window { StartupLocation = WindowStartupLocation.CenterOwner };

        Assert.IsNull(window.Owner);
        Assert.AreEqual(WindowStartupLocation.CenterScreen, window.EffectiveStartupLocation);
    }

    [TestMethod]
    public void MessageBoxWindow_StartsAsCenterOwner()
    {
        // The managed prompt defaults to CenterOwner, so an ownerless prompt relies on the fallback above.
        var prompt = new MessageBoxWindow("message");

        Assert.AreEqual(WindowStartupLocation.CenterOwner, prompt.StartupLocation);
        Assert.AreEqual(WindowStartupLocation.CenterScreen, prompt.EffectiveStartupLocation);
    }
}
