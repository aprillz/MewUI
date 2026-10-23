using Aprillz.MewUI;

namespace MewUI.Headless.Test;

[TestClass]
public static class AssemblyFixture
{
    /// <summary>
    /// Registers one graphics backend for the process before any session starts: the operating
    /// system's default, or on Windows the one MEWUI_HEADLESS_TEST_BACKEND names (Gdi, Direct2D).
    /// </summary>
    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        if (OperatingSystem.IsWindows())
        {
            string? requested = Environment.GetEnvironmentVariable("MEWUI_HEADLESS_TEST_BACKEND");
            if (string.Equals(requested, "Direct2D", StringComparison.OrdinalIgnoreCase))
            {
                Direct2DBackend.Register();
            }
            else
            {
                GdiBackend.Register();
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            MewVGMacOSBackend.Register();
        }
        else if (OperatingSystem.IsLinux())
        {
            MewVGX11Backend.Register();
        }
    }

    [AssemblyCleanup]
    public static void Cleanup() => HeadlessTestHost.Shutdown();
}
