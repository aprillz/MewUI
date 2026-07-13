using Aprillz.MewUI;
using Aprillz.MewUI.Rendering.Gdi;

namespace MewUI.Test;

[TestClass]
public static class AssemblyFixture
{
    /// <summary>
    /// Registers the process-wide graphics factory before any test runs. Swapping the factory
    /// mid-run races text measurement in concurrently executing tests (flaky text-dependent
    /// arrange assertions), so the swap must happen exactly once, up front.
    /// </summary>
    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        // Headless tests exercise the in-surface popup path (no real OS windows to host native popups).
        PopupManager.PreferNativePopups = false;

        if (OperatingSystem.IsWindows())
        {
            Application.DefaultGraphicsFactory = new GdiGraphicsFactory();
        }
    }
}
