using Aprillz.MewUI;

namespace MewUI.MewDock.Test;

[TestClass]
public static class AssemblyFixture
{
    /// <summary>Registers the process-wide graphics factory once, before any test measures text.</summary>
    [AssemblyInitialize]
    public static void Initialize(TestContext context)
    {
        if (OperatingSystem.IsWindows())
        {
            GdiBackend.Register();
        }
    }
}
