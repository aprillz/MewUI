using Aprillz.MewUI;
using Aprillz.MewUI.Platform;

namespace MewUI.Test.Core;

/// <summary>
/// Verifies the platform/backend surface-kind handshake (subplan 02): a mismatched pair fails with a
/// clear, actionable error at registration instead of at the first render's surface downcast (P1-10),
/// while a matching pair or a not-yet-complete registration passes.
/// </summary>
[TestClass]
public sealed class SurfaceKindHandshakeTests
{
    [TestMethod]
    public void MatchingPair_Passes()
    {
        // Win32 platform + Direct2D backend (both Win32) is a valid combination.
        Application.ValidateSurfaceKinds(PlatformSurfaceKind.Win32, PlatformSurfaceKind.Win32, "Win32", "Direct2D");
    }

    [TestMethod]
    public void MismatchedPair_ThrowsWithBothOrigins()
    {
        var ex = Assert.ThrowsExactly<InvalidOperationException>(
            () => Application.ValidateSurfaceKinds(PlatformSurfaceKind.X11, PlatformSurfaceKind.Win32, "X11", "Direct2D"));

        // The message names both sides so the misconfiguration is obvious.
        StringAssert.Contains(ex.Message, "Direct2D");
        StringAssert.Contains(ex.Message, "X11");
        StringAssert.Contains(ex.Message, "Win32");
    }

    [TestMethod]
    public void OnlyOneRegistered_DoesNotThrow()
    {
        // Registration is order-independent: with only one side known, no check fires yet.
        Application.ValidateSurfaceKinds(PlatformSurfaceKind.Win32, null, "Win32", null);
        Application.ValidateSurfaceKinds(null, PlatformSurfaceKind.MacOS, null, "MewVG.MacOS");
        Application.ValidateSurfaceKinds(null, null, null, null);
    }
}
